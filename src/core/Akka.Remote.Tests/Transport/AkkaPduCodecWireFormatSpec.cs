//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodecWireFormatSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util.Internal;
using Google.Protobuf;
using Xunit;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Tests.Transport
{
    public class AkkaPduCodecWireFormatSpec : AkkaSpec
    {
        private const string AssociateHex =
            "1239080112350A2A0A1057697265436F6D70617452656D6F746512093132372E302E302E3218F813" +
            "2208616B6B612E746370111100000000000000";

        private const string HeartbeatHex = "12020803";
        private const string DisassociateUnknownHex = "12020802";
        private const string DisassociateShutdownHex = "12020804";
        private const string DisassociateQuarantinedHex = "12020805";
        private const string PayloadHex = "0A03AABBCC";

        private const string PayloadWrappingAckAndMessageHex =
            "0AAA010A1B090A0000000000000012100B000000000000000C00000000000000128A010A350A33616B6B" +
            "612E7463703A2F2F57697265436F6D706174403132372E302E302E313A323535312F757365722F726563" +
            "697069656E7412140A0401020304107B1A0A6D616E69666573742D6122320A30616B6B612E7463703A2F" +
            "2F57697265436F6D706174403132372E302E302E313A323535312F757365722F73656E646572292A0000" +
            "0000000000";

        private const string PureAckHex = "0A1B090A0000000000000012100B000000000000000C00000000000000";

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

        private static readonly Address LocalAddress = new("akka.tcp", "WireCompat", "127.0.0.1", 2551);
        private static readonly Address RemoteAddress = new("akka.tcp", "WireCompatRemote", "127.0.0.2", 2552);

        private readonly AkkaPduProtobuffCodec _codec;

        public AkkaPduCodecWireFormatSpec(ITestOutputHelper output)
            : base(ConfigurationFactory.ParseString("akka.actor.provider = remote"), output)
        {
            _codec = new AkkaPduProtobuffCodec(Sys);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should preserve control PDU wire format")]
        public void AkkaPduProtobuffCodec_should_preserve_control_pdu_wire_format()
        {
            AssertBytes(AssociateHex, _codec.ConstructAssociate(new HandshakeInfo(RemoteAddress, 17)));
            AssertBytes(HeartbeatHex, _codec.ConstructHeartbeat());
            AssertBytes(DisassociateUnknownHex, _codec.ConstructDisassociate(DisassociateInfo.Unknown));
            AssertBytes(DisassociateShutdownHex, _codec.ConstructDisassociate(DisassociateInfo.Shutdown));
            AssertBytes(DisassociateQuarantinedHex, _codec.ConstructDisassociate(DisassociateInfo.Quarantined));
            AssertWriterBytes(AssociateHex, writer => _codec.ConstructAssociate(new HandshakeInfo(RemoteAddress, 17), writer));
            AssertWriterBytes(HeartbeatHex, writer => _codec.ConstructHeartbeat(writer));
            AssertWriterBytes(DisassociateUnknownHex, writer => _codec.ConstructDisassociate(DisassociateInfo.Unknown, writer));
            AssertWriterBytes(DisassociateShutdownHex, writer => _codec.ConstructDisassociate(DisassociateInfo.Shutdown, writer));
            AssertWriterBytes(DisassociateQuarantinedHex, writer => _codec.ConstructDisassociate(DisassociateInfo.Quarantined, writer));

            var associate = Assert.IsType<Associate>(_codec.DecodePdu(FromHexSequence(AssociateHex)));
            Assert.Equal(RemoteAddress, associate.Info.Origin);
            Assert.Equal(17, associate.Info.Uid);

            Assert.IsType<Heartbeat>(_codec.DecodePdu(FromHex(HeartbeatHex)));

            AssertDisassociate(DisassociateUnknownHex, DisassociateInfo.Unknown);
            AssertDisassociate(DisassociateShutdownHex, DisassociateInfo.Shutdown);
            AssertDisassociate(DisassociateQuarantinedHex, DisassociateInfo.Quarantined);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should preserve payload PDU wire format")]
        public void AkkaPduProtobuffCodec_should_preserve_payload_pdu_wire_format()
        {
            var payloadBytes = ByteString.CopyFrom(0xAA, 0xBB, 0xCC);

            AssertBytes(PayloadHex, _codec.ConstructPayload(payloadBytes));
            AssertWriterBytes(PayloadHex, writer => _codec.ConstructPayload(payloadBytes, writer));

            var decodedPayload = Assert.IsType<Payload>(_codec.DecodePdu(FromHex(PayloadHex)));
            Assert.Equal(payloadBytes, decodedPayload.Bytes);

            var decodedSequencePayload = Assert.IsType<SequencePayload>(_codec.DecodePdu(FromHexSequence(PayloadHex)));
            AssertSequenceBytes(payloadBytes, decodedSequencePayload.Bytes);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should fast-path canonical sequence payload PDUs")]
        public void AkkaPduProtobuffCodec_should_fast_path_canonical_sequence_payload_pdus()
        {
            var decodedPayload = Assert.IsType<SequencePayload>(_codec.DecodePdu(SplitSequence(0x0A, 0x03, 0xAA, 0xBB, 0xCC)));

            AssertSequenceBytes(ByteString.CopyFrom(0xAA, 0xBB, 0xCC), decodedPayload.Bytes);
        }

        [Theory(DisplayName = "AkkaPduProtobuffCodec should fall back from sequence fast path when needed")]
        [InlineData(HeartbeatHex, typeof(Heartbeat))]
        [InlineData("18010A03AABBCC", typeof(Payload))]
        [InlineData("0A03AABBCC1801", typeof(Payload))]
        [InlineData("120208030A03AABBCC", typeof(Heartbeat))]
        [InlineData("0A03AABBCC12020803", typeof(Heartbeat))]
        public void AkkaPduProtobuffCodec_should_fall_back_from_sequence_fast_path_when_needed(string hex, Type expectedPduType)
        {
            var decoded = _codec.DecodePdu(FromHexSequence(hex));

            Assert.Equal(expectedPduType, decoded.GetType());
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should reject malformed sequence payload fast path candidates")]
        public void AkkaPduProtobuffCodec_should_reject_malformed_sequence_payload_fast_path_candidates()
        {
            Assert.Throws<PduCodecException>(() => _codec.DecodePdu(FromHexSequence("0A80")));
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should preserve payload-wrapped envelope wire format")]
        public void AkkaPduProtobuffCodec_should_preserve_payload_wrapped_envelope_wire_format()
        {
            var envelope = _codec.ConstructMessage(
                LocalAddress,
                CreateFixedActorRef("recipient"),
                SerializedPayload(),
                CreateFixedActorRef("sender"),
                new SeqNo(42),
                Ack());

            AssertBytes(PayloadWrappingAckAndMessageHex, _codec.ConstructPayload(envelope));
            AssertWriterBytes(PayloadWrappingAckAndMessageHex, writer => _codec.ConstructPayload(envelope, writer));

            var decodedPayload = Assert.IsType<Payload>(_codec.DecodePdu(FromHex(PayloadWrappingAckAndMessageHex)));
            Assert.Equal(FromHex(AckAndMessageHex), decodedPayload.Bytes);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should preserve pure ACK wire format")]
        public void AkkaPduProtobuffCodec_should_preserve_pure_ack_wire_format()
        {
            AssertBytes(PureAckHex, _codec.ConstructPureAck(Ack()));
            AssertWriterBytes(PureAckHex, writer => _codec.ConstructPureAck(Ack(), writer));

            var decoded = _codec.DecodeMessage(FromHexSequence(PureAckHex), RemoteProvider, LocalAddress);
            Assert.Null(decoded.MessageOption);
            AssertAck(decoded.AckOption);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should preserve reliable delivery envelope wire format")]
        public void AkkaPduProtobuffCodec_should_preserve_reliable_delivery_envelope_wire_format()
        {
            AssertBytes(AckAndMessageHex, _codec.ConstructMessage(
                LocalAddress,
                CreateFixedActorRef("recipient"),
                SerializedPayload(),
                CreateFixedActorRef("sender"),
                new SeqNo(42),
                Ack()));
            AssertWriterBytes(AckAndMessageHex, writer => _codec.ConstructMessage(
                LocalAddress,
                CreateFixedActorRef("recipient"),
                SerializedPayload(),
                writer,
                CreateFixedActorRef("sender"),
                new SeqNo(42),
                Ack()));

            var decoded = _codec.DecodeMessage(FromHexSequence(AckAndMessageHex), RemoteProvider, LocalAddress);
            AssertAck(decoded.AckOption);
            AssertMessage(decoded.MessageOption, reliableDeliveryEnabled: true);
            Assert.Equal(new SeqNo(42), decoded.MessageOption!.Seq);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should decode reliable delivery envelope from split sequence")]
        public void AkkaPduProtobuffCodec_should_decode_reliable_delivery_envelope_from_split_sequence()
        {
            var decoded = _codec.DecodeMessage(SplitSequence(Convert.FromHexString(AckAndMessageHex)), RemoteProvider, LocalAddress);

            AssertAck(decoded.AckOption);
            AssertMessage(decoded.MessageOption, reliableDeliveryEnabled: true);
            Assert.Equal(new SeqNo(42), decoded.MessageOption!.Seq);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should decode envelopes with unknown fields")]
        public void AkkaPduProtobuffCodec_should_decode_envelopes_with_unknown_fields()
        {
            var decoded = _codec.DecodeMessage(FromHexSequence(AckAndMessageHex + "1801"), RemoteProvider, LocalAddress);

            AssertAck(decoded.AckOption);
            AssertMessage(decoded.MessageOption, reliableDeliveryEnabled: true);
            Assert.Equal(new SeqNo(42), decoded.MessageOption!.Seq);
        }

        [Fact(DisplayName = "AkkaPduProtobuffCodec should preserve unsequenced envelope wire format")]
        public void AkkaPduProtobuffCodec_should_preserve_unsequenced_envelope_wire_format()
        {
            AssertBytes(UnsequencedMessageHex, _codec.ConstructMessage(
                LocalAddress,
                CreateFixedActorRef("recipient"),
                SerializedPayload(),
                CreateFixedActorRef("sender")));
            AssertWriterBytes(UnsequencedMessageHex, writer => _codec.ConstructMessage(
                LocalAddress,
                CreateFixedActorRef("recipient"),
                SerializedPayload(),
                writer,
                CreateFixedActorRef("sender")));

            var decoded = _codec.DecodeMessage(FromHexSequence(UnsequencedMessageHex), RemoteProvider, LocalAddress);
            Assert.Null(decoded.AckOption);
            AssertMessage(decoded.MessageOption, reliableDeliveryEnabled: false);
            Assert.Null(decoded.MessageOption!.Seq);
        }

        private IRemoteActorRefProvider RemoteProvider =>
            Sys.AsInstanceOf<ExtendedActorSystem>().Provider.AsInstanceOf<IRemoteActorRefProvider>();

        private IActorRef CreateFixedActorRef(string name)
        {
            return new FixedActorRef(
                new RootActorPath(LocalAddress) / "user" / name,
                Sys.AsInstanceOf<ExtendedActorSystem>().Provider);
        }

        private static SerializedMessage SerializedPayload()
        {
            return new SerializedMessage
            {
                SerializerId = 123,
                MessageManifest = ByteString.CopyFromUtf8("manifest-a"),
                Message = ByteString.CopyFrom(1, 2, 3, 4)
            };
        }

        private static Ack Ack()
        {
            return new Ack(new SeqNo(10), new[] { new SeqNo(11), new SeqNo(12) });
        }

        private void AssertDisassociate(string hex, DisassociateInfo reason)
        {
            var disassociate = Assert.IsType<Disassociate>(_codec.DecodePdu(FromHex(hex)));
            Assert.Equal(reason, disassociate.Reason);
        }

        private static void AssertAck(Ack? ack)
        {
            Assert.NotNull(ack);
            Assert.Equal(new SeqNo(10), ack.CumulativeAck);
            Assert.Equal(new[] { new SeqNo(11), new SeqNo(12) }, ack.Nacks.ToArray());
        }

        private static void AssertMessage(Message? message, bool reliableDeliveryEnabled)
        {
            Assert.NotNull(message);
            Assert.Equal(LocalAddress, message.RecipientAddress);
            Assert.Equal(reliableDeliveryEnabled, message.ReliableDeliveryEnabled);

            var serializedMessage = message.SerializedMessage;
            Assert.Equal(123, serializedMessage.SerializerId);
            Assert.Equal(ByteString.CopyFromUtf8("manifest-a"), serializedMessage.MessageManifest);
            Assert.Equal(ByteString.CopyFrom(1, 2, 3, 4), serializedMessage.Message);
        }

        private static ByteString FromHex(string hex)
        {
            return ByteString.CopyFrom(Convert.FromHexString(hex));
        }

        private static ReadOnlySequence<byte> FromHexSequence(string hex)
        {
            return new ReadOnlySequence<byte>(Convert.FromHexString(hex));
        }

        private static ReadOnlySequence<byte> SplitSequence(params byte[] bytes)
        {
            var first = new SequenceSegment(new[] { bytes[0] });
            var current = first;
            for (var i = 1; i < bytes.Length; i++)
            {
                current = current.Append(new[] { bytes[i] });
            }

            return new ReadOnlySequence<byte>(first, 0, current, current.Memory.Length);
        }

        private static void AssertSequenceBytes(ByteString expected, ReadOnlySequence<byte> actual)
        {
            Assert.Equal(expected, ByteString.CopyFrom(actual.ToArray()));
        }

        private static void AssertBytes(string expectedHex, ByteString actual)
        {
            Assert.Equal(expectedHex, Convert.ToHexString(actual.ToByteArray()));
        }

        private static void AssertWriterBytes(string expectedHex, Action<IBufferWriter<byte>> write)
        {
            var writer = new ArrayBufferWriter<byte>();
            write(writer);
            Assert.Equal(expectedHex, Convert.ToHexString(writer.WrittenSpan));
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

        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
        {
            public SequenceSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public SequenceSegment Append(ReadOnlyMemory<byte> memory)
            {
                var segment = new SequenceSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = segment;
                return segment;
            }
        }
    }
}
