//-----------------------------------------------------------------------
// <copyright file="ArteryNumericRefCodecSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Buffers;
using Akka.Remote.Artery;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Envelope codec round trip of the sender and recipient in each shape: absent, literal path, numeric record.
    /// </summary>
    public class ArteryNumericRefCodecSpec : AkkaSpec
    {
        private const int FrameLengthFieldLength = 4;
        private const int HeaderLength = 32;
        private const long Origin = 0x0123_4567_89AB_CDEFL;

        public ArteryNumericRefCodecSpec(ITestOutputHelper output) : base(output)
        {
        }

        private Akka.Serialization.PooledPayloadWriter Encode(
            string? senderPath, ArteryNumericRef? senderRef, string? recipientPath, ArteryNumericRef? recipientRef) =>
            ArteryEnvelopeCodec.Encode(Sys.Serialization, Origin, senderPath, senderRef, recipientPath, recipientRef, "payload");

        private static ReadOnlySequence<byte> Body(Akka.Serialization.PooledPayloadWriter writer) =>
            new(writer.WrittenMemory.Slice(FrameLengthFieldLength));

        [Fact(DisplayName = "Should_round_trip_absent_literal_and_numeric_shapes_in_either_position")]
        public void Should_round_trip_absent_literal_and_numeric_shapes_in_either_position()
        {
            var temporary = ArteryNumericRef.ForTemporary(Origin, 77);
            // A reply's recipient is owned by the asker, not by the frame's origin.
            var otherOwner = ArteryNumericRef.ForTemporary(0x0BAD_CAFEL, 78);

            using (var absent = Encode(null, null, null, null))
            {
                var decoded = ArteryEnvelopeCodec.Decode(Body(absent));
                Assert.Equal(ArteryTagKind.Absent, decoded.SenderKind);
                Assert.Equal(ArteryTagKind.Absent, decoded.RecipientKind);
            }

            using (var literalSenderNumericRecipient = Encode("akka://S@h:1/user/a", null, null, otherOwner))
            {
                var decoded = ArteryEnvelopeCodec.Decode(Body(literalSenderNumericRecipient));
                Assert.Equal(ArteryTagKind.Literal, decoded.SenderKind);
                Assert.True(decoded.TryGetSenderPath(out var sender));
                Assert.Equal("akka://S@h:1/user/a", sender);
                Assert.False(decoded.TryGetSenderNumeric(out _));

                Assert.Equal(ArteryTagKind.Numeric, decoded.RecipientKind);
                Assert.True(decoded.TryGetRecipientNumeric(out var recipient));
                Assert.Equal(otherOwner.OwnerUid, recipient.OwnerUid);
                Assert.Equal(otherOwner.Id, recipient.Id);
                Assert.False(decoded.TryGetRecipientPath(out var path));
                Assert.Null(path);
            }

            using (var numericSenderLiteralRecipient = Encode(null, temporary, "akka://S@h:1/user/b", null))
            {
                var decoded = ArteryEnvelopeCodec.Decode(Body(numericSenderLiteralRecipient));
                Assert.Equal(ArteryTagKind.Numeric, decoded.SenderKind);
                Assert.True(decoded.TryGetSenderNumeric(out var sender));
                Assert.Equal(temporary.OwnerUid, sender.OwnerUid);
                Assert.Equal(temporary.Id, sender.Id);
                Assert.Equal(ArteryTagKind.Literal, decoded.RecipientKind);
                Assert.True(decoded.TryGetRecipientPath(out var recipient));
                Assert.Equal("akka://S@h:1/user/b", recipient);
            }
        }
    }
}
