//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodecFastDecodeDifferentialSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
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
    /// Correctness tests for the hand-rolled tag-dispatch inbound decoder
    /// (<see cref="AkkaPduProtobuffCodec.DecodeMessageFast"/>).
    ///
    /// Every test follows the same readable shape: state explicit, human-readable inputs, run the FAST
    /// decoder, and assert the decoded fields equal those exact inputs — no oracle to trust and no hex to
    /// decode in your head. Each case is decoded both as one contiguous buffer AND split one-byte-per-segment
    /// (so cross-segment varints/fixed64/strings are exercised everywhere).
    ///
    /// Two areas can't be expressed by "encode with the codec, then decode":
    ///  • the FROZEN interop wire bytes — pinned to this build's encoder by
    ///    <see cref="Encoder_emits_exactly_the_frozen_interop_bytes"/>, then decoded to their documented values;
    ///  • field orders a DIFFERENT encoder (e.g. the JVM) may emit — protobuf allows any field order, but the
    ///    .NET codec only ever writes ascending order, so those bytes are built by hand.
    ///
    /// A final defense-in-depth test additionally cross-checks the fast decoder against the shipped
    /// generated-protobuf parser over a small corpus.
    /// </summary>
    public class AkkaPduCodecFastDecodeDifferentialSpec : AkkaSpec
    {
        private static readonly Address LocalAddress = new("akka.tcp", "WireCompat", "127.0.0.1", 2551);

        private readonly AkkaPduProtobuffCodec _codec;

        public AkkaPduCodecFastDecodeDifferentialSpec(ITestOutputHelper output)
            : base(ConfigurationFactory.ParseString("akka.actor.provider = remote"), output)
        {
            _codec = new AkkaPduProtobuffCodec(Sys);
        }

        // ===================== correctness: encode known values, fast-decode, assert exact values =====================

        [Fact(DisplayName = "Fast decode: full envelope (recipient, sender, seq, payload, ack)")]
        public void Decodes_full_envelope()
        {
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("alice"), Payload(7, "hello", 0xDE, 0xAD, 0xBE, 0xEF),
                ActorRef("bob"), new SeqNo(42), Ack(10, 11, 12)).ToByteArray();

            AssertFastDecode(wire, "full",
                recipient: "alice", sender: "bob", seq: new SeqNo(42),
                payload: Payload(7, "hello", 0xDE, 0xAD, 0xBE, 0xEF), ack: Ack(10, 11, 12));
        }

        [Fact(DisplayName = "Fast decode: envelope with no ack")]
        public void Decodes_envelope_without_ack()
        {
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("alice"), Payload(7, "hello", 1, 2, 3),
                ActorRef("bob"), new SeqNo(42)).ToByteArray();

            AssertFastDecode(wire, "no-ack",
                recipient: "alice", sender: "bob", seq: new SeqNo(42),
                payload: Payload(7, "hello", 1, 2, 3), ack: null);
        }

        [Fact(DisplayName = "Fast decode: envelope with no sender")]
        public void Decodes_envelope_without_sender()
        {
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("alice"), Payload(7, "hello", 1, 2, 3), seqOption: new SeqNo(7)).ToByteArray();

            AssertFastDecode(wire, "no-sender",
                recipient: "alice", sender: null, seq: new SeqNo(7),
                payload: Payload(7, "hello", 1, 2, 3), ack: null);
        }

        [Fact(DisplayName = "Fast decode: unsequenced envelope (seq undefined)")]
        public void Decodes_unsequenced_envelope()
        {
            // ConstructMessage with no seq writes SeqUndefined on the wire, which decodes back to a null Seq.
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("alice"), Payload(7, "hello", 1, 2, 3), ActorRef("bob")).ToByteArray();

            AssertFastDecode(wire, "unsequenced",
                recipient: "alice", sender: "bob", seq: null,
                payload: Payload(7, "hello", 1, 2, 3), ack: null);
        }

        [Fact(DisplayName = "Fast decode: pure ack (no message)")]
        public void Decodes_pure_ack()
        {
            var wire = _codec.ConstructPureAck(Ack(10, 11, 12)).ToByteArray();

            AssertFastDecode(wire, "pure-ack",
                recipient: null, sender: null, seq: null, payload: null, ack: Ack(10, 11, 12));
        }

        [Fact(DisplayName = "Fast decode: large payload + empty manifest")]
        public void Decodes_large_payload()
        {
            var big = Enumerable.Range(0, 4096).Select(i => (byte)i).ToArray();
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("alice"), Payload(5, "", big), ActorRef("bob"), new SeqNo(99)).ToByteArray();

            AssertFastDecode(wire, "large-payload",
                recipient: "alice", sender: "bob", seq: new SeqNo(99),
                payload: Payload(5, "", big), ack: null);
        }

        [Fact(DisplayName = "Fast decode: non-ASCII actor paths (UTF-8)")]
        public void Decodes_unicode_actor_paths()
        {
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("recipient-Ä-名前"), Payload(7, "m", 9),
                ActorRef("sender-Ω-送信"), new SeqNo(2), Ack(10, 11, 12)).ToByteArray();

            AssertFastDecode(wire, "unicode",
                recipient: "recipient-Ä-名前", sender: "sender-Ω-送信", seq: new SeqNo(2),
                payload: Payload(7, "m", 9), ack: Ack(10, 11, 12));
        }

        [Fact(DisplayName = "Fast decode: ack with cumulative only (no nacks)")]
        public void Decodes_ack_with_no_nacks()
        {
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("alice"), Payload(7, "m", 9), ActorRef("bob"), new SeqNo(1), Ack(3)).ToByteArray();

            AssertFastDecode(wire, "ack-no-nacks",
                recipient: "alice", sender: "bob", seq: new SeqNo(1), payload: Payload(7, "m", 9), ack: Ack(3));
        }

        // ===================== frozen interop wire bytes (golden) =====================

        // The exact bytes classic Akka.Remote puts on the wire for the documented messages below.
        // Encoder_emits_exactly_the_frozen_interop_bytes proves these equal THIS build's encoder output,
        // so they pin the interop wire format; the tests then decode them to their documented values.
        // AckAndMessage decodes to: recipient=/user/recipient, sender=/user/sender, seq=42,
        //   payload{serializerId=123, manifest="manifest-a", message=[1,2,3,4]}, ack{cumulative=10, nacks=[11,12]}.
        private const string AckAndMessageHex =
            "0A1B090A0000000000000012100B000000000000000C00000000000000128A010A350A33616B6B612E746370" +
            "3A2F2F57697265436F6D706174403132372E302E302E313A323535312F757365722F726563697069656E74" +
            "12140A0401020304107B1A0A6D616E69666573742D6122320A30616B6B612E7463703A2F2F5769726543" +
            "6F6D706174403132372E302E302E313A323535312F757365722F73656E646572292A00000000000000";

        // Same envelope, no ack, seq undefined => decodes to seq=null, ack=null.
        private const string UnsequencedMessageHex =
            "128A010A350A33616B6B612E7463703A2F2F57697265436F6D706174403132372E302E302E313A323535" +
            "312F757365722F726563697069656E7412140A0401020304107B1A0A6D616E69666573742D6122320A30" +
            "616B6B612E7463703A2F2F57697265436F6D706174403132372E302E302E313A323535312F757365722F" +
            "73656E64657229FFFFFFFFFFFFFFFF";

        // Pure ack, no envelope => decodes to no message, ack{cumulative=10, nacks=[11,12]}.
        private const string PureAckHex = "0A1B090A0000000000000012100B000000000000000C00000000000000";

        private static SerializedMessage CanonicalPayload() => Payload(123, "manifest-a", 1, 2, 3, 4);

        [Fact(DisplayName = "Fast decode: frozen interop bytes (AckAndMessage)")]
        public void Decodes_frozen_AckAndMessage_interop_bytes()
        {
            AssertFastDecode(Convert.FromHexString(AckAndMessageHex), "frozen:AckAndMessage",
                recipient: "recipient", sender: "sender", seq: new SeqNo(42), payload: CanonicalPayload(), ack: Ack(10, 11, 12));
        }

        [Fact(DisplayName = "Fast decode: frozen interop bytes (Unsequenced)")]
        public void Decodes_frozen_Unsequenced_interop_bytes()
        {
            AssertFastDecode(Convert.FromHexString(UnsequencedMessageHex), "frozen:Unsequenced",
                recipient: "recipient", sender: "sender", seq: null, payload: CanonicalPayload(), ack: null);
        }

        [Fact(DisplayName = "Fast decode: frozen interop bytes (PureAck)")]
        public void Decodes_frozen_PureAck_interop_bytes()
        {
            AssertFastDecode(Convert.FromHexString(PureAckHex), "frozen:PureAck",
                recipient: null, sender: null, seq: null, payload: null, ack: Ack(10, 11, 12));
        }

        [Fact(DisplayName = "The frozen interop bytes are exactly what this build's encoder emits (wire-format anchor)")]
        public void Encoder_emits_exactly_the_frozen_interop_bytes()
        {
            // If this fails, the wire format drifted — the frozen samples above would no longer be interop-valid.
            Assert.Equal(AckAndMessageHex, Convert.ToHexString(_codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), CanonicalPayload(), ActorRef("sender"), new SeqNo(42), Ack(10, 11, 12)).ToByteArray()));

            Assert.Equal(UnsequencedMessageHex, Convert.ToHexString(_codec.ConstructMessage(
                LocalAddress, ActorRef("recipient"), CanonicalPayload(), ActorRef("sender")).ToByteArray()));

            Assert.Equal(PureAckHex, Convert.ToHexString(_codec.ConstructPureAck(Ack(10, 11, 12)).ToByteArray()));
        }

        [Fact(DisplayName = "Fast decode: ignores unknown trailing fields a newer/other encoder may add")]
        public void Ignores_unknown_trailing_fields()
        {
            // Unknown field 3 (varint) and field 6 (length-delimited) appended to a full envelope.
            AssertFastDecode(Convert.FromHexString(AckAndMessageHex + "1801"), "unknown-varint",
                recipient: "recipient", sender: "sender", seq: new SeqNo(42), payload: CanonicalPayload(), ack: Ack(10, 11, 12));

            AssertFastDecode(Convert.FromHexString(AckAndMessageHex + "3203AABBCC"), "unknown-lendelim",
                recipient: "recipient", sender: "sender", seq: new SeqNo(42), payload: CanonicalPayload(), ack: Ack(10, 11, 12));
        }

        // ===================== field-order independence (JVM / other-encoder interop) =====================

        [Fact(DisplayName = "Fast decode: envelope with fields in reverse wire order (other-encoder interop)")]
        public void Decodes_envelope_with_reversed_field_order()
        {
            // protobuf permits any field order on the wire; the .NET codec always writes ascending order, so a
            // tag-dispatch decoder that assumed order would corrupt e.g. JVM-encoded messages. Build the SAME
            // logical messages with fields REVERSED and assert they decode to the same explicit values.
            var full = ReversedOrderEnvelope("alice", "bob", Payload(7, "hi", 1, 2), seq: new SeqNo(42), ack: Ack(10, 11, 12));
            AssertFastDecode(full, "reversed:full",
                recipient: "alice", sender: "bob", seq: new SeqNo(42), payload: Payload(7, "hi", 1, 2), ack: Ack(10, 11, 12));

            // This minimal envelope OMITS the seq field entirely. An absent seq field decodes to SeqNo(0)
            // (the proto3 default for the uint64 field), which is distinct from a PRESENT SeqUndefined
            // (=> null Seq, as in the unsequenced cases above). The fast decoder mirrors the generated parser here.
            var minimal = ReversedOrderEnvelope("alice", senderName: null, Payload(7, "hi", 1, 2), seq: null, ack: null);
            AssertFastDecode(minimal, "reversed:minimal",
                recipient: "alice", sender: null, seq: new SeqNo(0), payload: Payload(7, "hi", 1, 2), ack: null);
        }

        // ===================== resolve cache =====================

        [Fact(DisplayName = "Fast decode: byte-keyed resolve cache is correct across repeats and localAddress changes")]
        public void Resolve_cache_correct_across_repeats_and_localaddress()
        {
            var wire = _codec.ConstructMessage(
                LocalAddress, ActorRef("svc"), Payload(7, "m", 9), ActorRef("cli"), new SeqNo(5)).ToByteArray();

            // Repeated decodes force byte-keyed cache hits; each must still be correct.
            for (var i = 0; i < 5; i++)
                AssertFastDecode(wire, $"repeat{i}",
                    recipient: "svc", sender: "cli", seq: new SeqNo(5), payload: Payload(7, "m", 9), ack: null);

            // A DIFFERENT localAddress must not be served a ref cached for LocalAddress (the cache's guard).
            AssertFastDecodeWith(new Address("akka.tcp", "WireCompat", "127.0.0.1", 9999), wire, "otherLocal",
                recipient: "svc", sender: "cli", seq: new SeqNo(5), payload: Payload(7, "m", 9), ack: null);
        }

        // ===================== defense-in-depth: matches the shipped generated parser =====================

        [Fact(DisplayName = "Fast decoder matches the shipped generated parser over a corpus (defense-in-depth)")]
        public void Matches_the_generated_protobuf_parser()
        {
            // Belt-and-suspenders on top of the explicit-value tests above: the fast decoder must agree with the
            // existing generated-protobuf parser (an independent implementation) field-for-field.
            var corpus = new[]
            {
                _codec.ConstructMessage(LocalAddress, ActorRef("recipient"), CanonicalPayload(), ActorRef("sender"), new SeqNo(42), Ack(10, 11, 12)).ToByteArray(),
                _codec.ConstructMessage(LocalAddress, ActorRef("recipient"), CanonicalPayload(), ActorRef("sender"), new SeqNo(42)).ToByteArray(),
                _codec.ConstructMessage(LocalAddress, ActorRef("recipient"), CanonicalPayload(), seqOption: new SeqNo(7)).ToByteArray(),
                _codec.ConstructMessage(LocalAddress, ActorRef("recipient"), CanonicalPayload(), ActorRef("sender")).ToByteArray(),
                _codec.ConstructPureAck(Ack(10, 11, 12)).ToByteArray(),
            };

            for (var i = 0; i < corpus.Length; i++)
            {
                var wire = corpus[i];
                var oracle = _codec.DecodeMessage(ByteString.CopyFrom(wire), RemoteProvider, LocalAddress);
                var fast = _codec.DecodeMessageFast(new ReadOnlySequence<byte>(wire), RemoteProvider, LocalAddress);
                AssertSameDecode($"corpus[{i}]", oracle, fast);
            }
        }

        // ===================== assertions =====================

        private void AssertFastDecode(byte[] wire, string label,
            string? recipient, string? sender, SeqNo? seq, SerializedMessage? payload, Ack? ack)
            => AssertFastDecodeWith(LocalAddress, wire, label, recipient, sender, seq, payload, ack);

        private void AssertFastDecodeWith(Address localAddress, byte[] wire, string label,
            string? recipient, string? sender, SeqNo? seq, SerializedMessage? payload, Ack? ack)
        {
            // Decode both as one buffer and split one-byte-per-segment (stresses cross-segment parsing).
            var buffers = new (string variant, ReadOnlySequence<byte> buf)[]
            {
                ("contiguous", new ReadOnlySequence<byte>(wire)),
                ("split-1B", SplitEveryByte(wire)),
            };

            foreach (var (variant, buf) in buffers)
            {
                var who = $"{label} [{variant}]";
                var decoded = _codec.DecodeMessageFast(buf, RemoteProvider, localAddress);

                if (recipient is null)
                {
                    Assert.True(decoded.MessageOption is null, $"{who}: expected no message");
                }
                else
                {
                    Assert.True(decoded.MessageOption is not null, $"{who}: expected a message");
                    var m = decoded.MessageOption!;
                    Assert.Equal(SerializedPath(recipient), m.Recipient.Path.ToSerializationFormat());

                    if (sender is null)
                        Assert.True(m.SenderOptional is null, $"{who}: expected no sender");
                    else
                    {
                        Assert.True(m.SenderOptional is not null, $"{who}: expected sender '{sender}'");
                        Assert.Equal(SerializedPath(sender), m.SenderOptional!.Path.ToSerializationFormat());
                    }

                    Assert.Equal(seq, m.Seq);
                    Assert.Equal(payload!.SerializerId, m.SerializedMessage.SerializerId);
                    Assert.Equal(payload.MessageManifest, m.SerializedMessage.MessageManifest);
                    Assert.Equal(payload.Message, m.SerializedMessage.Message);
                }

                if (ack is null)
                {
                    Assert.True(decoded.AckOption is null, $"{who}: expected no ack");
                }
                else
                {
                    Assert.True(decoded.AckOption is not null, $"{who}: expected an ack");
                    Assert.Equal(ack.CumulativeAck, decoded.AckOption!.CumulativeAck);
                    Assert.Equal(ack.Nacks.ToArray(), decoded.AckOption.Nacks.ToArray());
                }
            }
        }

        private static void AssertSameDecode(string label, AckAndMessage oracle, AckAndMessage fast)
        {
            if (oracle.AckOption is null)
                Assert.True(fast.AckOption is null, $"{label}: expected null ack");
            else
            {
                Assert.True(fast.AckOption is not null, $"{label}: expected non-null ack");
                Assert.Equal(oracle.AckOption.CumulativeAck, fast.AckOption!.CumulativeAck);
                Assert.Equal(oracle.AckOption.Nacks.ToArray(), fast.AckOption.Nacks.ToArray());
            }

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
            Assert.Equal(o.SenderOptional?.Path, f.SenderOptional?.Path);
            Assert.Equal(o.SerializedMessage.SerializerId, f.SerializedMessage.SerializerId);
            Assert.Equal(o.SerializedMessage.MessageManifest, f.SerializedMessage.MessageManifest);
            Assert.Equal(o.SerializedMessage.Message, f.SerializedMessage.Message);
        }

        // ===================== builders / helpers =====================

        // Builds an AckAndEnvelopeContainer with fields written in REVERSE wire order, by hand, because the
        // codec only ever emits ascending order. protobuf field numbers (WireFormats.proto):
        //   RemoteEnvelope:            recipient=1, message=2, sender=4, seq=5 (fixed64)
        //   AckAndEnvelopeContainer:   ack=1, envelope=2
        private byte[] ReversedOrderEnvelope(string recipientName, string? senderName, SerializedMessage payload, SeqNo? seq, Ack? ack)
        {
            using var envStream = new MemoryStream();
            var env = new CodedOutputStream(envStream);
            // reversed envelope order: seq(5) -> sender(4) -> message(2) -> recipient(1)
            if (seq is { } s)
            {
                env.WriteTag(5, WireFormat.WireType.Fixed64);
                env.WriteFixed64((ulong)s.RawValue);
            }
            if (senderName is not null)
            {
                env.WriteTag(4, WireFormat.WireType.LengthDelimited);
                env.WriteMessage(new ActorRefData { Path = SerializedPath(senderName) });
            }
            env.WriteTag(2, WireFormat.WireType.LengthDelimited);
            env.WriteMessage(payload);
            env.WriteTag(1, WireFormat.WireType.LengthDelimited);
            env.WriteMessage(new ActorRefData { Path = SerializedPath(recipientName) });
            env.Flush();
            var envelopeBytes = envStream.ToArray();

            using var outStream = new MemoryStream();
            var os = new CodedOutputStream(outStream);
            // reversed container order: envelope(2) before ack(1). WriteBytes embeds the manually-ordered
            // envelope verbatim (length-prefixed), so its reversed field order is preserved on the wire.
            os.WriteTag(2, WireFormat.WireType.LengthDelimited);
            os.WriteBytes(ByteString.CopyFrom(envelopeBytes));
            if (ack is not null)
            {
                var info = new AcknowledgementInfo { CumulativeAck = (ulong)ack.CumulativeAck.RawValue };
                info.Nacks.AddRange(ack.Nacks.Select(n => (ulong)n.RawValue));
                os.WriteTag(1, WireFormat.WireType.LengthDelimited);
                os.WriteMessage(info);
            }
            os.Flush();
            return outStream.ToArray();
        }

        private IRemoteActorRefProvider RemoteProvider =>
            Sys.AsInstanceOf<ExtendedActorSystem>().Provider.AsInstanceOf<IRemoteActorRefProvider>();

        private IActorRef ActorRef(string name) =>
            new FixedActorRef(new RootActorPath(LocalAddress) / "user" / name, Sys.AsInstanceOf<ExtendedActorSystem>().Provider);

        private string SerializedPath(string name) =>
            (new RootActorPath(LocalAddress) / "user" / name).ToSerializationFormat();

        private static SerializedMessage Payload(int serializerId, string manifest, params byte[] message) => new()
        {
            SerializerId = serializerId,
            MessageManifest = ByteString.CopyFromUtf8(manifest),
            Message = ByteString.CopyFrom(message)
        };

        private static Ack Ack(long cumulative, params long[] nacks) =>
            new(new SeqNo(cumulative), nacks.Select(n => new SeqNo(n)).ToArray());

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
