#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Serialization;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public sealed class ArteryReadBoundarySpec : AkkaSpec
{
    public ArteryReadBoundarySpec(ITestOutputHelper output)
        : base(ActorSystemSetup.Create(Serializers()), output) { }

    private static SerializationSetup Serializers() => SerializationSetup.Create(system =>
        ImmutableHashSet.Create(
            SerializerDetails.Create("boundary-v2", new BytesV2(system), ImmutableHashSet.Create<Type>(typeof(Bytes)))));
    private const string SenderPath = "akka://peer@127.0.0.1:4321/user/sender-é😀";
    private const string RecipientPath = "akka://local/user/receiver-é😀";
    private const string WireManifest = "boundary-é😀";

    [Fact(DisplayName = "Artery frames match contiguous decoding at every field byte and nearby TCP chunk offset")]
    public async Task Should_match_contiguous_decoding_across_chunk_boundary_sweep()
    {
        var probe = CreateTestProbe();
        foreach (var size in new[] { 0, 1, 65535, 65536, 65537, 131073, 1024 * 1024 })
        {
            var payload = new byte[size];
            new Random(size).NextBytes(payload);
            var target = Frame(RecipientPath, payload);
            var payloadStart = target.Length - size;
            // Sweeping every header/literal byte also covers every field start/end at -8..+8.
            var offsets = Enumerable.Range(-8, payloadStart + 17)
                .Concat(Enumerable.Range(target.Length - 8, 17)).Distinct();
            foreach (var offset in offsets)
            {
                var empty = Frame(RecipientPath, Array.Empty<byte>());
                var boundary = ((offset + empty.Length + 65535) / 65536) * 65536;
                var padding = Frame(RecipientPath, new byte[boundary - offset - empty.Length]);
                var wire = padding.Concat(target).Concat(empty).ToArray();
                var pipe = new System.IO.Pipelines.Pipe(new System.IO.Pipelines.PipeOptions(pauseWriterThreshold: 0));
                await pipe.Writer.WriteAsync(wire);
                new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref).Start();
                var split = (await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).Data;
                var baseline = new ArteryFrameParser(4 * 1024 * 1024);
                var chunked = new ArteryFrameParser(4 * 1024 * 1024);
                baseline.Append(wire);
                foreach (var memory in split) chunked.Append(memory);
                for (var frame = 0; frame < 3; frame++)
                {
                    Assert.True(baseline.TryReadFrame(out var contiguous));
                    Assert.True(chunked.TryReadFrame(out var segmented));
                    Assert.Equal(contiguous.ToArray(), segmented.ToArray());
                    var expected = ArteryEnvelopeCodec.Decode(contiguous);
                    var actual = ArteryEnvelopeCodec.Decode(segmented);
                    Assert.Equal(expected.Header, actual.Header);
                    Assert.True(expected.TryGetSenderPath(out var sender));
                    Assert.True(actual.TryGetSenderPath(out var actualSender));
                    Assert.Equal(sender, actualSender);
                    Assert.True(expected.TryGetRecipientPath(out var recipient));
                    Assert.True(actual.TryGetRecipientPath(out var actualRecipient));
                    Assert.Equal(recipient, actualRecipient);
                    Assert.True(expected.TryGetManifest(out var manifest));
                    Assert.True(actual.TryGetManifest(out var actualManifest));
                    Assert.Equal(manifest, actualManifest);
                    Assert.Equal(expected.Payload.ToArray(), actual.Payload.ToArray());
                }
                Assert.False(chunked.TryReadFrame(out _));
                Assert.False(baseline.HasBufferedBytes);
                Assert.False(chunked.HasBufferedBytes);
                await pipe.Writer.CompleteAsync();
                await pipe.Reader.CompleteAsync();
            }
        }
    }

    [Theory(DisplayName = "Artery inbound lanes decode every fixed-header and literal byte split through real TCP copying")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Should_decode_all_engineered_boundary_offsets(int lanes)
    {
        var payload = new byte[65537];
        new Random(91).NextBytes(payload);
        var target = Frame(RecipientPath, payload);
        var payloadStart = target.Length - payload.Length;
        // Every internal byte of the 4-byte prefix, 48-byte fixed header,
        // all three literal lengths and UTF-8 data, plus payload boundaries.
        var offsets = Enumerable.Range(1, payloadStart + 2)
            .Concat(new[] { payloadStart + 65535, payloadStart + 65536 }).ToArray();
        var materializer = Sys.Materializer();
        foreach (var offset in offsets)
        {
            var overhead = 5 + Frame(RecipientPath, Array.Empty<byte>()).Length;
            var boundary = ((offset + overhead + 65535) / 65536) * 65536;
            var padding = Frame(RecipientPath, new byte[boundary - offset - overhead]);
            var wire = new byte[] { 65, 75, 75, 65, (byte)ArteryStreamId.Ordinary }.Concat(padding).Concat(target).ToArray();
            var pipe = new System.IO.Pipelines.Pipe(new System.IO.Pipelines.PipeOptions(pauseWriterThreshold: 0));
            var probe = CreateTestProbe();
            await pipe.Writer.WriteAsync(wire);
            new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref).Start();
            var data = (await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).Data;
            Assert.Equal(65536, data.First.Length);
            Assert.Equal(target[offset], data.Slice(boundary, 1).FirstSpan[0]);
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry,
                new UniqueAddress(new Address("akka", "local", "127.0.0.1", 1234), 1), (_, _) => { });
            context.CompleteHandshake(new UniqueAddress(new Address("akka", "peer", "127.0.0.1", 4321), 42));
            var received = CreateTestProbe();
            var stage = new ArteryInboundProcessingStage(2 * 1024 * 1024, 4 * 1024 * 1024,
                Sys.Serialization, Sys.Scheduler, inboundLanes: lanes, inboundLaneBufferSize: 64,
                inboundContext: context, dispatchOrdinary: (message, decoded, _) =>
                {
                    Assert.True(decoded.TryGetSenderPath(out var sender));
                    Assert.Equal(SenderPath, sender);
                    Assert.True(decoded.TryGetRecipientPath(out var recipient));
                    Assert.Equal(RecipientPath, recipient);
                    Assert.True(decoded.TryGetManifest(out var manifest));
                    Assert.Equal(WireManifest, manifest);
                    received.Ref.Tell(message);
                });
            await Source.Single(data).Via(stage).RunWith(Sink.Ignore<IInboundEnvelope>(), materializer)
                .WaitAsync(TimeSpan.FromSeconds(10));
            await received.ExpectMsgAsync<Bytes>(TimeSpan.FromSeconds(10));
            var actual = await received.ExpectMsgAsync<Bytes>(TimeSpan.FromSeconds(10));
            Assert.Equal(payload, actual.Data);
            await pipe.Writer.CompleteAsync();
            await pipe.Reader.CompleteAsync();
        }
    }

    private static byte[] Frame(string recipient, byte[] payload)
    {
        var frame = new byte[ArteryEnvelopeCodec.MaxEncodedSize(SenderPath, recipient, WireManifest, payload.Length)];
        ArteryEnvelopeCodec.Encode(frame, 42, 987661, SenderPath, recipient, WireManifest, payload);
        return frame;
    }

    private sealed record Bytes(byte[] Data);
    private sealed class BytesV2(ExtendedActorSystem system) : SerializerV2(system)
    {
        public override int Identifier => 987661;
        public override string Manifest(object obj) => WireManifest;
        public override int Serialize(object obj, IBufferWriter<byte> writer)
        {
            var bytes = ((Bytes)obj).Data;
            bytes.CopyTo(writer.GetSpan(bytes.Length));
            writer.Advance(bytes.Length);
            return bytes.Length;
        }
        public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
        {
            Assert.Equal(WireManifest, manifest);
            return new Bytes(bytes.ToArray());
        }
    }
}
