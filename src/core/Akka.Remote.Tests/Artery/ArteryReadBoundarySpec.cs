#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
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
            SerializerDetails.Create("boundary-v2", new BytesV2(system), ImmutableHashSet.Create<Type>(typeof(Bytes))),
            SerializerDetails.Create("boundary-v1", new BytesV1(system), ImmutableHashSet.Create<Type>(typeof(LegacyBytes)))));
    private const string SenderPath = "akka://peer@127.0.0.1:4321/user/sender-é😀";
    private const string RecipientPath = "akka://local/user/receiver-é😀";
    private const string WireManifest = "boundary-é😀";

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

    [Theory(DisplayName = "Two real Artery systems retain large V2, V1 and selection payloads with lanes and TLS")]
    [InlineData(1, false)]
    [InlineData(4, false)]
    [InlineData(1, true)]
    [InlineData(4, true)]
    public async Task Should_exchange_large_messages_over_real_connections(int lanes, bool tls)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(System.Net.IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        ActorSystemSetup Setup()
        {
            var config = ConfigurationFactory.ParseString($$"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.inbound-lanes = {{lanes}}
                akka.remote.artery.advanced.maximum-frame-size = 2m
                akka.remote.artery.advanced.maximum-large-frame-size = 4m
                akka.remote.artery.large-message-destinations = ["/user/large"]
                """);
            var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(config), Serializers());
            var trust = TlsPeerPolicy.CustomTrust((peer, _, _, _, _) => peer?.Thumbprint == certificate.Thumbprint);
            return tls ? setup.And(new ArteryTlsSetup(ArteryTlsSettings.Mutual(certificate, trust, trust))) : setup;
        }
        var observedLanes = new ConcurrentQueue<int>();
        var a = ActorSystem.Create("BoundaryA", Setup());
        var b = ActorSystem.Create("BoundaryB",
            Setup().And(new ArteryTransportSetup(onInboundLanesInitialized: observedLanes.Enqueue)));
        var retained = new ConcurrentQueue<(ReadOnlySequence<byte> Input, byte[] Snapshot)>();
        ((BytesV2)b.Serialization.FindSerializerForType(typeof(Bytes))).Retained = retained;
        try
        {
            b.ActorOf(Props.Create(() => new Echo()), "ordinary");
            b.ActorOf(Props.Create(() => new Echo()), "large");
            var probe = CreateTestProbe(a);
            var address = RARP.For(b).Provider.DefaultAddress;
            var ordinary = await a.ActorSelection($"{address}/user/ordinary").ResolveOne(TimeSpan.FromSeconds(10));
            var large = await a.ActorSelection($"{address}/user/large").ResolveOne(TimeSpan.FromSeconds(10));
            foreach (var length in new[] { 65535, 65536, 65537, 131072, 131073, 1048577 })
            {
                var bytes = new byte[length];
                new Random(length).NextBytes(bytes);
                var target = length > 1048576 ? large : ordinary;
                target.Tell(new Bytes(bytes), probe.Ref);
                Assert.Equal(bytes, (await probe.ExpectMsgAsync<Bytes>(TimeSpan.FromSeconds(15))).Data);
                target.Tell(new LegacyBytes(bytes), probe.Ref);
                Assert.Equal(bytes, (await probe.ExpectMsgAsync<LegacyBytes>(TimeSpan.FromSeconds(15))).Data);
                a.ActorSelection($"{address}/user/ordinary").Tell(new Bytes(bytes), probe.Ref);
                Assert.Equal(bytes, (await probe.ExpectMsgAsync<Bytes>(TimeSpan.FromSeconds(15))).Data);
            }
            if (lanes == 4)
                Assert.Contains(4, observedLanes);
            else
                Assert.Empty(observedLanes);
            Assert.Contains(retained, item => !item.Input.IsSingleSegment);
        }
        finally { await Task.WhenAll(a.Terminate(), b.Terminate()).WaitAsync(TimeSpan.FromSeconds(15)); }
        foreach (var item in retained)
            Assert.Equal(item.Snapshot, item.Input.ToArray());
    }

    private sealed class Echo : ReceiveActor
    {
        public Echo() => ReceiveAny(message => Sender.Tell(message));
    }
    private sealed record Bytes(byte[] Data);
    private sealed record LegacyBytes(byte[] Data);
    private sealed class BytesV2(ExtendedActorSystem system) : SerializerV2(system)
    {
        public ConcurrentQueue<(ReadOnlySequence<byte> Input, byte[] Snapshot)>? Retained { get; set; }
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
            var copy = bytes.ToArray();
            Retained?.Enqueue((bytes, copy));
            return new Bytes(copy);
        }
    }
    private sealed class BytesV1(ExtendedActorSystem system) : SerializerWithStringManifest(system)
    {
        public override int Identifier => 987662;
        public override string Manifest(object obj) => WireManifest;
        public override byte[] ToBinary(object obj) => ((LegacyBytes)obj).Data;
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Assert.Equal(WireManifest, manifest);
            return new LegacyBytes(bytes);
        }
    }
}
