//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodecFastDecodeDifferentialSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util.Internal;
using Google.Protobuf;
using Xunit;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Differential test for the hand-rolled tag-dispatch decoder (<see cref="AkkaPduProtobuffCodec.DecodeMessageFast"/>,
    /// Phase-2 lever B2a) against the generated-protobuf path (<see cref="AkkaPduProtobuffCodec.DecodeMessage"/>) which
    /// is treated as the correctness oracle. The fast decoder MUST produce byte-identical results for every input —
    /// including canonical interop wire samples, generated envelopes, permuted field order (JVM/other-encoder
    /// interop), unknown trailing fields, and buffers split across many segments.
    /// </summary>
    public class AkkaPduCodecFastDecodeDifferentialSpec : AkkaSpec
    {
        // Canonical, interop-committed wire samples (copied from AkkaPduCodecWireFormatSpec).
        private const string AckAndMessageHex =
            "0A1B090A0000000000000012100B000000000000000C00000000000000128A010A350A33616B6B612E746370" +
            "3A2F2F57697265436F6D706174403132372E302E302E313A323535312F757365722F726563697069656E74" +
            "12140A0401020304107B1A0A6D616E69666573742D6122320A30616B6B612E7463703A2F2F5769726543" +
            "6F6D706174403132372E302E302E313A323535312F757365722F73656E646572292A00000000000000";

        private const string UnsequencedMessageHex =
            "128A010A350A33616B6B612E7463703A2F2F57697265436F6D706174403132372E302E302E313A323535" +
            "312F757365722F726563697069656E7412140A0401020304107B1A0A6D616E69666573742D6122320A30" +
            "616B6B612E7463703A2F2F57697265436F6D706174403132372E302E302E313A323535312F757365722F" +
            "73656E64657229FFFFFFFFFFFFFFFF";

        private const string PureAckHex = "0A1B090A0000000000000012100B000000000000000C00000000000000";

        private static readonly Address LocalAddress = new("akka.tcp", "WireCompat", "127.0.0.1", 2551);

        private readonly AkkaPduProtobuffCodec _codec;

        public AkkaPduCodecFastDecodeDifferentialSpec(ITestOutputHelper output)
            : base(ConfigurationFactory.ParseString("akka.actor.provider = remote"), output)
        {
            _codec = new AkkaPduProtobuffCodec(Sys);
        }

        public static IEnumerable<object[]> CanonicalSamples()
        {
            yield return new object[] { "AckAndMessage", AckAndMessageHex };
            yield return new object[] { "Unsequenced", UnsequencedMessageHex };
            yield return new object[] { "PureAck", PureAckHex };
            // Unknown trailing field (field 3, varint) appended to a full envelope.
            yield return new object[] { "AckAndMessage+unknownVarint", AckAndMessageHex + "1801" };
            // Unknown trailing field (field 6, length-delimited) appended.
            yield return new object[] { "AckAndMessage+unknownLenDelim", AckAndMessageHex + "3203AABBCC" };
        }

        [Theory(DisplayName = "DecodeMessageFast matches DecodeMessage on canonical wire samples")]
        [MemberData(nameof(CanonicalSamples))]
        public void Fast_matches_oracle_on_canonical_samples(string label, string hex)
        {
            AssertEquivalentBothLayouts(label, Convert.FromHexString(hex));
        }

        [Fact(DisplayName = "DecodeMessageFast matches DecodeMessage on generated envelopes")]
        public void Fast_matches_oracle_on_generated_envelopes()
        {
            foreach (var (label, bytes) in GeneratedCorpus())
                AssertEquivalentBothLayouts(label, bytes);
        }

        [Fact(DisplayName = "DecodeMessageFast matches DecodeMessage with permuted field order (JVM interop)")]
        public void Fast_matches_oracle_with_permuted_field_order()
        {
            // RemoteEnvelope fields written in REVERSE order (seq, sender, message, recipient) and the
            // top-level container with envelope-before-ack — exercising true tag-dispatch order independence.
            var withAck = BuildPermutedContainer(
                RecipientPath("recipient"), SenderPath("sender"), Payload(), seq: 42, includeAck: true);
            AssertEquivalentBothLayouts("permuted+ack", withAck);

            var noAck = BuildPermutedContainer(
                RecipientPath("recipient"), SenderPath("sender"), Payload(), seq: 42, includeAck: false);
            AssertEquivalentBothLayouts("permuted+noAck", noAck);

            // Permuted, with seq omitted entirely (undefined) and no sender.
            var minimal = BuildPermutedContainer(
                RecipientPath("recipient"), senderPath: null, Payload(), seq: null, includeAck: false);
            AssertEquivalentBothLayouts("permuted+minimal", minimal);
        }

        [Fact(DisplayName = "DecodeMessageFast resolve cache is correct across repeats and localAddress changes")]
        public void Fast_resolve_cache_correct_across_repeats_and_localaddress_changes()
        {
            var bytes = _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), Payload(), ActorRef("sender"), new SeqNo(5), null).ToByteArray();

            // Repeated decodes force byte-keyed cache hits; each must still equal the oracle.
            for (var i = 0; i < 5; i++)
                AssertEquivalentBothLayouts($"repeat{i}", bytes);

            // Same path bytes, DIFFERENT localAddress: the cache's localAddress guard must re-resolve
            // through the provider rather than serve the entry cached for LocalAddress.
            var otherLocal = new Address("akka.tcp", "WireCompat", "127.0.0.1", 9999);
            AssertEquivalent("otherLocal",
                _codec.DecodeMessage(ByteString.CopyFrom(bytes), RemoteProvider, otherLocal),
                _codec.DecodeMessageFast(new ReadOnlySequence<byte>(bytes), RemoteProvider, otherLocal));

            // Back to the original localAddress — still correct.
            AssertEquivalentBothLayouts("backToOriginal", bytes);
        }

        // ---- corpus ----

        private IEnumerable<(string label, byte[] bytes)> GeneratedCorpus()
        {
            var ack = new Ack(new SeqNo(10), new[] { new SeqNo(11), new SeqNo(12) });

            yield return ("gen:full", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), Payload(), ActorRef("sender"), new SeqNo(42), ack).ToByteArray());

            yield return ("gen:noAck", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), Payload(), ActorRef("sender"), new SeqNo(42)).ToByteArray());

            yield return ("gen:noSender", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), Payload(), seqOption: new SeqNo(7)).ToByteArray());

            yield return ("gen:noSeqNoAck", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), Payload(), ActorRef("sender")).ToByteArray());

            yield return ("gen:emptyAckNacks", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), Payload(), ActorRef("sender"), new SeqNo(1), new Ack(new SeqNo(3))).ToByteArray());

            // Large payload + empty manifest.
            yield return ("gen:largePayload", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"),
                new SerializedMessage { SerializerId = 5, Message = ByteString.CopyFrom(new byte[4096]) },
                ActorRef("sender"), new SeqNo(99)).ToByteArray());

            // Non-ASCII actor names exercise the UTF-8 path decode.
            yield return ("gen:unicodePaths", _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient-Ä-名前"), Payload(), ActorRef("sender-Ω-送信"), new SeqNo(2), ack).ToByteArray());

            // Pure ack (no envelope).
            yield return ("gen:pureAck", _codec.ConstructPureAck(ack).ToByteArray());
        }

        private byte[] BuildPermutedContainer(string recipientPath, string? senderPath, SerializedMessage message, ulong? seq, bool includeAck)
        {
            using var envStream = new MemoryStream();
            var eos = new CodedOutputStream(envStream);
            if (seq is { } s)
            {
                eos.WriteTag(5, WireFormat.WireType.Fixed64);
                eos.WriteFixed64(s);
            }
            if (senderPath is not null)
            {
                eos.WriteTag(4, WireFormat.WireType.LengthDelimited);
                eos.WriteMessage(new ActorRefData { Path = senderPath });
            }
            eos.WriteTag(2, WireFormat.WireType.LengthDelimited);
            eos.WriteMessage(message);
            eos.WriteTag(1, WireFormat.WireType.LengthDelimited);
            eos.WriteMessage(new ActorRefData { Path = recipientPath });
            eos.Flush();
            var envelopeBytes = envStream.ToArray();

            using var outStream = new MemoryStream();
            var os = new CodedOutputStream(outStream);
            // envelope (field 2) BEFORE ack (field 1) — reversed top-level order.
            // WriteBytes emits the length prefix + raw bytes after the tag, embedding the manually
            // (permuted-order) serialized RemoteEnvelope verbatim.
            os.WriteTag(2, WireFormat.WireType.LengthDelimited);
            os.WriteBytes(ByteString.CopyFrom(envelopeBytes));
            if (includeAck)
            {
                os.WriteTag(1, WireFormat.WireType.LengthDelimited);
                os.WriteMessage(new AcknowledgementInfo { CumulativeAck = 10, Nacks = { 11UL, 12UL } });
            }
            os.Flush();
            return outStream.ToArray();
        }

        // ---- assertions ----

        private void AssertEquivalentBothLayouts(string label, byte[] bytes)
        {
            // Single contiguous segment.
            AssertEquivalent($"{label} [single]",
                _codec.DecodeMessage(ByteString.CopyFrom(bytes), RemoteProvider, LocalAddress),
                _codec.DecodeMessageFast(new ReadOnlySequence<byte>(bytes), RemoteProvider, LocalAddress));

            // Worst-case fragmentation: one byte per segment (stresses cross-segment varints/fixed64/strings).
            var split = SplitEveryByte(bytes);
            AssertEquivalent($"{label} [split]",
                _codec.DecodeMessage(ByteString.CopyFrom(split.ToArray()), RemoteProvider, LocalAddress),
                _codec.DecodeMessageFast(split, RemoteProvider, LocalAddress));
        }

        private static void AssertEquivalent(string label, AckAndMessage oracle, AckAndMessage fast)
        {
            // Ack
            if (oracle.AckOption is null)
            {
                Assert.True(fast.AckOption is null, $"{label}: expected null ack");
            }
            else
            {
                Assert.True(fast.AckOption is not null, $"{label}: expected non-null ack");
                Assert.Equal(oracle.AckOption.CumulativeAck, fast.AckOption!.CumulativeAck);
                Assert.Equal(oracle.AckOption.Nacks.ToArray(), fast.AckOption.Nacks.ToArray());
            }

            // Message
            if (oracle.MessageOption is null)
            {
                Assert.True(fast.MessageOption is null, $"{label}: expected null message");
                return;
            }

            Assert.True(fast.MessageOption is not null, $"{label}: expected non-null message");
            var o = oracle.MessageOption;
            var f = fast.MessageOption!;

            Assert.Equal(o.Recipient.Path, f.Recipient.Path);
            Assert.Equal(o.RecipientAddress, f.RecipientAddress);
            Assert.Equal(o.Seq, f.Seq);

            if (o.SenderOptional is null)
                Assert.True(f.SenderOptional is null, $"{label}: expected null sender");
            else
            {
                Assert.True(f.SenderOptional is not null, $"{label}: expected non-null sender");
                Assert.Equal(o.SenderOptional.Path, f.SenderOptional!.Path);
            }

            if (o.SerializedMessage is null)
                Assert.True(f.SerializedMessage is null, $"{label}: expected null serialized message");
            else
            {
                Assert.True(f.SerializedMessage is not null, $"{label}: expected non-null serialized message");
                Assert.Equal(o.SerializedMessage.SerializerId, f.SerializedMessage!.SerializerId);
                Assert.Equal(o.SerializedMessage.MessageManifest, f.SerializedMessage.MessageManifest);
                Assert.Equal(o.SerializedMessage.Message, f.SerializedMessage.Message);
            }
        }

        // ---- helpers ----

        private IRemoteActorRefProvider RemoteProvider =>
            Sys.AsInstanceOf<ExtendedActorSystem>().Provider.AsInstanceOf<IRemoteActorRefProvider>();

        private IActorRef ActorRef(string name) =>
            new FixedActorRef(new RootActorPath(LocalAddress) / "user" / name, Sys.AsInstanceOf<ExtendedActorSystem>().Provider);

        private string RecipientPath(string name) => (new RootActorPath(LocalAddress) / "user" / name).ToSerializationFormat();

        private string SenderPath(string name) => (new RootActorPath(LocalAddress) / "user" / name).ToSerializationFormat();

        private static SerializedMessage Payload() => new()
        {
            SerializerId = 123,
            MessageManifest = ByteString.CopyFromUtf8("manifest-a"),
            Message = ByteString.CopyFrom(1, 2, 3, 4)
        };

        private static ReadOnlySequence<byte> SplitEveryByte(byte[] bytes)
        {
            if (bytes.Length == 0)
                return ReadOnlySequence<byte>.Empty;

            var first = new Segment(new[] { bytes[0] });
            var current = first;
            for (var i = 1; i < bytes.Length; i++)
                current = current.Append(new[] { bytes[i] });

            return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
        }

        private sealed class FixedActorRef : MinimalActorRef
        {
            public FixedActorRef(ActorPath path, IActorRefProvider provider)
            {
                Path = path;
                Provider = provider;
            }

            public override ActorPath Path { get; }
            public override IActorRefProvider Provider { get; }
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

            public Segment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
                Next = segment;
                return segment;
            }
        }
    }
}
