//-----------------------------------------------------------------------
// <copyright file="ArteryInboundLaneRoutingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Tests for how an inbound lane connection routes frames: the reader hashes the recipient
    /// literal's raw UTF-8 bytes to choose a lane and decodes no string; the lane consumer decodes
    /// the manifest, recipient and sender literals before deserialization.
    /// </summary>
    public class ArteryInboundLaneRoutingSpec : AkkaSpec
    {
        private const long OriginUid = 42;
        private const int Lanes = 4;

        public ArteryInboundLaneRoutingSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(int inboundLanes) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.inbound-lanes = {{inboundLanes}}
            akka.remote.artery.advanced.outbound-message-queue-size = 20000
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string RemotePath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        // ---------------------------------------------------------------- pure hash

        public static IEnumerable<object[]> AsciiPaths()
        {
            foreach (var lanes in new[] { 2, 3, 4, 7, 16 })
            {
                yield return new object[] { "akka://sys@127.0.0.1:25520/user/a", lanes };
                yield return new object[] { "akka://sys@127.0.0.1:25520/user/worker-17#123456789", lanes };
                yield return new object[] { "akka://sys@host.example:1/system/sharding/Orders/shard-9/entity-3", lanes };
                yield return new object[] { "", lanes };
            }
        }





        private static ReadOnlySequence<byte> Split(byte[] bytes) => TwoSegments(bytes, Math.Min(1, bytes.Length));

        /// <summary>A two-segment sequence over <paramref name="bytes"/> split at <paramref name="offset"/> (1 .. Length-1; otherwise a single segment).</summary>
        private static ReadOnlySequence<byte> TwoSegments(byte[] bytes, int offset)
        {
            if (offset <= 0 || offset >= bytes.Length)
                return new ReadOnlySequence<byte>(bytes);

            var first = new Segment(bytes.AsMemory(0, offset));
            var last = first.Append(bytes.AsMemory(offset));
            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

            public Segment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }
        }

        // ---------------------------------------------------------------- stage harness (raw frames)

        private sealed record Delivery(object Message, string? Sender, string Recipient);

        private static byte[] Preamble() => new[] { (byte)'A', (byte)'K', (byte)'K', (byte)'A', (byte)ArteryStreamId.Ordinary };

        /// <summary>Encodes one frame (length prefix included). <paramref name="mutate"/> may patch envelope bytes; offsets are relative to the envelope start.</summary>
        private byte[] Frame(string? recipient, object message, long origin = OriginUid, Action<byte[]>? mutate = null)
        {
            using var writer = ArteryEnvelopeCodec.Encode(Sys.Serialization, origin, "akka://local/user/sender", recipient, message);
            var bytes = writer.WrittenSpan.ToArray();
            mutate?.Invoke(bytes);
            return bytes;
        }

        private static void SetTag(byte[] frame, int tagOffset, uint tag) =>
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4 + tagOffset), tag);

        private ActorMaterializer? _materializer;

        private const long SentinelOrigin = 43;
        private const string SentinelRecipient = "akka://local/user/a";

        private static AssociationRegistryInboundContext NewContext(bool knownOrigin, Action<AssociationRegistry>? arrange)
        {
            var registry = new AssociationRegistry();
            var context = new AssociationRegistryInboundContext(registry,
                new UniqueAddress(new Address("akka", "local", "127.0.0.1", 1234), 1), (_, _) => { });
            if (knownOrigin)
                context.CompleteHandshake(new UniqueAddress(new Address("akka", "peer", "127.0.0.1", 4321), OriginUid));
            context.CompleteHandshake(new UniqueAddress(new Address("akka", "sentinel", "127.0.0.1", 4322), SentinelOrigin));
            arrange?.Invoke(registry);
            return context;
        }

        private Task<List<Delivery>> Run(bool knownOrigin, Action<AssociationRegistry>? arrange, int? expectedCount, params byte[][] frames) =>
            RunCore(knownOrigin, arrange, expectedCount, null, frames);

        /// <summary>
        /// Pushes <paramref name="frames"/> through one lane-enabled connection, delivered
        /// as the chunks <paramref name="split"/> produces from the whole byte stream (default: one chunk).
        /// Unless <paramref name="expectedCount"/> is given, a trailing sentinel frame from a second,
        /// always-known origin to <c>/user/a</c> marks the end: gating drops happen on the reader before the
        /// sentinel is routed, and lane drops precede it on the same lane, so once the sentinel arrives every
        /// earlier frame to <c>/user/a</c> has been processed. No wall-clock waits besides a failure timeout.
        /// </summary>
        private async Task<List<Delivery>> RunCore(
            bool knownOrigin, Action<AssociationRegistry>? arrange, int? expectedCount,
            Func<byte[], IEnumerable<byte[]>>? split, byte[][] frames)
        {
            var context = NewContext(knownOrigin, arrange);
            var delivered = new ConcurrentQueue<Delivery>();
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stage = new ArteryInboundProcessingStage(65536, 65536, Sys.Serialization, Sys.Scheduler,
                inboundLanes: Lanes, inboundLaneBufferSize: 64, inboundContext: context,
                dispatchOrdinary: (m, frame) =>
                {
                    var s = frame.TryGetSenderPath(out var sp) ? sp : null;
                    frame.TryGetRecipientPath(out var r);
                    delivered.Enqueue(new Delivery(m, s, r!));
                    if (expectedCount is { } count ? delivered.Count >= count : Equals(m, "sentinel"))
                        done.TrySetResult(true);
                });

            var all = expectedCount is null ? frames.Append(Frame(SentinelRecipient, "sentinel", SentinelOrigin)).ToArray() : frames;
            var bytes = Preamble().Concat(all.SelectMany(f => f)).ToArray();
            var chunks = split is null ? new[] { bytes } : split(bytes).ToArray();
            chunks.SelectMany(c => c).Should().Equal(bytes, "the split must preserve the byte stream");

            // One materializer per test instance, reused by every run (a test may run hundreds of
            // streams); it is released with the actor system when the spec is disposed.
            _materializer ??= ActorMaterializer.Create(Sys);
            await Source.From(chunks.Select(c => new ReadOnlySequence<byte>(c))).Via(stage)
                .RunWith(Sink.Ignore<IInboundEnvelope>(), _materializer)
                .WaitAsync(TimeSpan.FromSeconds(10));
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
            return delivered.Where(d => !Equals(d.Message, "sentinel")).ToList();
        }

        [Fact(DisplayName = "Inbound lanes: the lane consumer decodes manifest, sender and recipient and delivers per-recipient in send order")]
        public async Task Lane_decodes_literals_and_preserves_order_per_recipient()
        {
            const int recipients = 8;
            const int perRecipient = 100;
            var frames = new List<byte[]>();
            for (var i = 0; i < perRecipient; i++)
                for (var r = 0; r < recipients; r++)
                    frames.Add(Frame($"akka://local/user/r-{r}", $"{r}:{i}"));

            var delivered = await Run(true, null, recipients * perRecipient, frames.ToArray());

            delivered.Should().HaveCount(recipients * perRecipient);
            delivered.Should().OnlyContain(d => d.Sender == "akka://local/user/sender");
            for (var r = 0; r < recipients; r++)
            {
                var forRecipient = delivered.Where(d => d.Recipient == $"akka://local/user/r-{r}").Select(d => (string)d.Message).ToArray();
                forRecipient.Should().Equal(Enumerable.Range(0, perRecipient).Select(i => $"{r}:{i}"));
            }
        }




        [Fact(DisplayName = "Inbound lanes: a malformed recipient literal drops only that frame (detected on the reader)")]
        public async Task Malformed_recipient_literal_drops_one_frame()
        {
            var delivered = await Run(true, null, null,
                Frame("akka://local/user/a", "one"),
                Frame("akka://local/user/a", "bad", mutate: f => SetTag(f, ArteryEnvelopeHeader.RecipientTagOffset, 8u)),
                Frame("akka://local/user/a", "bad-too", mutate: f => SetTag(f, ArteryEnvelopeHeader.RecipientTagOffset, 60_000u)),
                Frame("akka://local/user/a", "two"));

            delivered.Select(d => d.Message).Should().Equal("one", "two");
        }

        [Fact(DisplayName = "Inbound lanes: a malformed sender or manifest literal drops only that frame (detected on the lane)")]
        public async Task Malformed_sender_or_manifest_literal_drops_one_frame()
        {
            var delivered = await Run(true, null, null,
                Frame("akka://local/user/a", "one"),
                Frame("akka://local/user/a", "bad-sender", mutate: f => SetTag(f, ArteryEnvelopeHeader.SenderTagOffset, 60_000u)),
                Frame("akka://local/user/a", "bad-manifest", mutate: f => SetTag(f, ArteryEnvelopeHeader.ManifestTagOffset, 8u)),
                Frame("akka://local/user/a", "two"));

            delivered.Select(d => d.Message).Should().Equal("one", "two");
        }



        // ---------------------------------------------------------------- multi-segment frame bodies

        private static string Describe(IEnumerable<Delivery> delivered) => string.Join("|",
            delivered.GroupBy(d => d.Recipient)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => g.Key + "=>" + string.Join(",", g.Select(d => $"{d.Message}@{d.Sender}"))));


        [Fact(DisplayName = "Inbound lanes: the stage delivers identically when the byte stream is cut in two at every offset (inside the preamble, length fields, header and each literal)")]
        public async Task Two_chunks_at_every_offset_deliver_identically()
        {
            var frames = new[]
            {
                Frame("akka://local/user/r-0", "m0"),
                Frame("akka://local/user/caf\u00e9-\u65e5", "m1"),
                Frame("akka://local/user/r-0", "m2")
            };

            var baseline = Describe(await RunCore(true, null, 3, null, frames));
            baseline.Should().Contain("m0@akka://local/user/sender,m2@akka://local/user/sender");

            var total = Preamble().Length + frames.Sum(f => f.Length);
            for (var offset = 1; offset < total; offset++)
            {
                var o = offset;
                var actual = Describe(await RunCore(true, null, 3, b => new[] { b[..o], b[o..] }, frames));
                actual.Should().Be(baseline, $"split at {o}");
            }
        }


        private static IEnumerable<byte[]> RandomChunks(byte[] bytes, Random random)
        {
            var position = 0;
            while (position < bytes.Length)
            {
                var size = Math.Min(random.Next(1, 23), bytes.Length - position);
                yield return bytes[position..(position + size)];
                position += size;
            }
        }


        // ---------------------------------------------------------------- routing key equals the decoded-string hash

        private static byte[] WithLastByte(string path, byte last)
        {
            var bytes = Encoding.UTF8.GetBytes(path);
            bytes[bytes.Length - 1] = last;
            return bytes;
        }

        public static IEnumerable<object[]> RecipientByteStrings()
        {
            yield return new object[] { Array.Empty<byte>() };
            yield return new object[] { Encoding.UTF8.GetBytes("akka://sys@127.0.0.1:25520/user/worker-17#123456789") };
            yield return new object[] { Encoding.UTF8.GetBytes("akka://sys@h:1/user/caf\u00e9-\u65e5\u672c") };
            yield return new object[] { WithLastByte("akka://local/user/a", 0x80) };
            yield return new object[] { WithLastByte("akka://local/user/a", 0x81) };
            yield return new object[] { new byte[] { 0xC3 } };                 // truncated two-byte sequence
            yield return new object[] { new byte[] { 0xE6, 0x97 } };           // truncated three-byte sequence
            yield return new object[] { new byte[] { 0xFF, 0xFE, 0x41 } };     // never valid in UTF-8
            yield return new object[] { new byte[] { 0xC0, 0x80, 0x41 } };     // overlong encoding
            yield return new object[] { new byte[] { 0x41, 0xF0, 0x9F, 0x98 } }; // truncated four-byte sequence
        }

        [Theory(DisplayName = "Inbound lanes: the routing key equals the legacy char hash of the decoded string for every input and every two-segment split")]
        [MemberData(nameof(RecipientByteStrings))]
        public void Routing_key_equals_decoded_string_hash(byte[] bytes)
        {
            foreach (var lanes in new[] { 2, 3, 4, 7, 16 })
            {
                var expected = ArteryLaneHash.FromChars(Encoding.UTF8.GetString(bytes), lanes);

                ArteryLaneHash.FromUtf8(new ReadOnlySequence<byte>(bytes), lanes).Should().Be(expected);
                for (var offset = 1; offset < bytes.Length; offset++)
                    ArteryLaneHash.FromUtf8(TwoSegments(bytes, offset), lanes).Should().Be(expected, $"lanes {lanes}, split at {offset}");
            }
        }



        // ---------------------------------------------------------------- backpressure and abort with a blocked lane

        private const string BlockedRecipient = "akka://local/user/blocked";

        /// <summary>
        /// A stage with a tiny lane buffer whose lane for <see cref="BlockedRecipient"/> blocks inside
        /// dispatch on its first item until <paramref name="release"/> is set.
        /// </summary>
        private ArteryInboundProcessingStage BlockingStage(
            ManualResetEventSlim release, TaskCompletionSource<bool> entered, ConcurrentQueue<Delivery> delivered, int laneBuffer) =>
            new(65536, 65536, Sys.Serialization, Sys.Scheduler,
                inboundLanes: Lanes, inboundLaneBufferSize: laneBuffer, inboundContext: NewContext(true, null),
                dispatchOrdinary: (m, frame) =>
                {
                    var s = frame.TryGetSenderPath(out var sp) ? sp : null;
                    frame.TryGetRecipientPath(out var r);
                    if (r == BlockedRecipient)
                    {
                        entered.TrySetResult(true);
                        release.Wait(TimeSpan.FromSeconds(60));
                    }

                    delivered.Enqueue(new Delivery(m, s, r!));
                });

        [Fact(DisplayName = "Inbound lanes: with a full lane channel the reader parks, and after release every frame is delivered in order and none is lost")]
        public async Task Full_lane_channel_parks_the_reader_without_losing_frames()
        {
            const int blockedFrames = 60; // many more than the lane buffer of 4
            const int otherFrames = 20;
            var frames = new List<byte[]>();
            for (var i = 0; i < blockedFrames; i++)
                frames.Add(Frame(BlockedRecipient, $"b:{i}"));
            for (var i = 0; i < otherFrames; i++)
                frames.Add(Frame("akka://local/user/free", $"f:{i}"));

            using var release = new ManualResetEventSlim(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = new ConcurrentQueue<Delivery>();
            var stage = BlockingStage(release, entered, delivered, laneBuffer: 4);
            var bytes = Preamble().Concat(frames.SelectMany(f => f)).ToArray();

            try
            {
                _materializer ??= ActorMaterializer.Create(Sys);
                var completion = Source.Single(new ReadOnlySequence<byte>(bytes)).Via(stage)
                    .RunWith(Sink.Ignore<IInboundEnvelope>(), _materializer);

                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

                // The blocked lane can hold at most 1 executing + 4 queued frames, so the reader must
                // be parked on it and cannot have consumed the whole input.
                completion.IsCompleted.Should().BeFalse("the reader is parked on the full lane");

                release.Set();
                await completion.WaitAsync(TimeSpan.FromSeconds(30));
                await AwaitAssertAsync(() => delivered.Count.Should().Be(blockedFrames + otherFrames), 30.Seconds());

                delivered.Where(d => d.Recipient == BlockedRecipient).Select(d => (string)d.Message)
                    .Should().Equal(Enumerable.Range(0, blockedFrames).Select(i => $"b:{i}"));
                delivered.Where(d => d.Recipient == "akka://local/user/free").Select(d => (string)d.Message)
                    .Should().Equal(Enumerable.Range(0, otherFrames).Select(i => $"f:{i}"));
            }
            finally
            {
                release.Set();
            }
        }


        // ---------------------------------------------------------------- logging of a malformed manifest from an unknown origin


        // ---------------------------------------------------------------- two real systems

        [Fact(DisplayName = "Inbound lanes: Tell, ActorSelection (exact and wildcard) and Ask round trips work between two systems at inbound-lanes=4")]
        public async Task Tell_Selection_And_Ask_Round_Trip()
        {
            var systemB = ActorSystem.Create("ArteryLaneRoutingB", ArteryConfig(Lanes));
            var systemA = ActorSystem.Create("ArteryLaneRoutingA", ArteryConfig(Lanes));
            try
            {
                var probeB = CreateTestProbe(systemB);
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                systemB.ActorOf(Props.Create(() => new Forward(probeB)), "sel-1");
                systemB.ActorOf(Props.Create(() => new Forward(probeB)), "sel-2");

                var echo = await systemA.ActorSelection(RemotePath(systemB, "echo")).ResolveOne(10.Seconds());

                (await echo.Ask<string>("tell-me", 10.Seconds())).Should().Be("tell-me");
                (await echo.Ask<string>("ask-me", 10.Seconds())).Should().Be("ask-me");

                systemA.ActorSelection(RemotePath(systemB, "sel-1")).Tell("exact");
                await probeB.ExpectMsgAsync("exact", 10.Seconds());

                systemA.ActorSelection($"akka://{systemB.Name}@127.0.0.1:{BoundPort(systemB)}/user/sel-*").Tell("wild");
                await probeB.ExpectMsgAsync("wild", 10.Seconds());
                await probeB.ExpectMsgAsync("wild", 10.Seconds());
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(m => Sender.Tell(m));
        }

        private sealed class Forward : ReceiveActor
        {
            public Forward(IActorRef target) => ReceiveAny(m => target.Tell(m));
        }
    }
}
