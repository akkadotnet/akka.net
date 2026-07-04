//-----------------------------------------------------------------------
// <copyright file="ArteryFramingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Linq;
using Akka.Remote.Artery;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Unit tests for the G1 "TCP Framing Foundation" primitives: the Artery connection
    /// preamble codec (<see cref="ArteryConnectionHeader"/>) and the incremental frame
    /// parser (<see cref="ArteryFrameParser"/>). No <c>ActorSystem</c> is needed — these are
    /// pure, allocation-conscious binary codecs. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire layout" /
    /// Decision 3) for the wire format under test.
    /// </summary>
    public class ArteryFramingSpec
    {
        #region ArteryConnectionHeader

        // NOTE: the theory parameter is the raw stream-id byte (not ArteryStreamId) because
        // ArteryStreamId is `internal` - a public [Theory] method cannot declare a parameter
        // of a less-accessible type (CS0051). The enum is reconstructed inside the method.
        [Theory(DisplayName = "ArteryConnectionHeader should round-trip write and parse for every stream id")]
        [InlineData((byte)1)]
        [InlineData((byte)2)]
        [InlineData((byte)3)]
        public void ArteryConnectionHeader_should_round_trip_every_stream_id(byte rawStreamId)
        {
            var streamId = (ArteryStreamId)rawStreamId;
            Span<byte> buffer = stackalloc byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(buffer, streamId);

            var sequence = new ReadOnlySequence<byte>(buffer.ToArray());
            var result = ArteryConnectionHeader.TryParse(sequence, out var parsedStreamId, out var bytesConsumed);

            result.Should().Be(ArteryConnectionHeaderParseResult.Success);
            parsedStreamId.Should().Be(streamId);
            bytesConsumed.Should().Be(ArteryConnectionHeader.Length);
        }

        [Theory(DisplayName = "ArteryConnectionHeader should report NeedMoreData for fewer than 5 bytes")]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void ArteryConnectionHeader_should_need_more_data_for_partial_input(int availableBytes)
        {
            var full = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(full, ArteryStreamId.Ordinary);
            var partial = new ReadOnlySequence<byte>(full.AsMemory(0, availableBytes));

            var result = ArteryConnectionHeader.TryParse(partial, out var streamId, out var bytesConsumed);

            result.Should().Be(ArteryConnectionHeaderParseResult.NeedMoreData);
            bytesConsumed.Should().Be(0);
        }

        [Fact(DisplayName = "ArteryConnectionHeader should throw ArteryFramingException on bad magic bytes")]
        public void ArteryConnectionHeader_should_throw_on_bad_magic()
        {
            var bytes = new byte[] { (byte)'N', (byte)'O', (byte)'P', (byte)'E', (byte)ArteryStreamId.Control };
            var sequence = new ReadOnlySequence<byte>(bytes);

            Assert.Throws<ArteryFramingException>(() =>
                ArteryConnectionHeader.TryParse(sequence, out _, out _));
        }

        [Theory(DisplayName = "ArteryConnectionHeader should throw ArteryFramingException on an unknown stream id byte")]
        [InlineData((byte)0)]
        [InlineData((byte)4)]
        [InlineData((byte)255)]
        public void ArteryConnectionHeader_should_throw_on_unknown_stream_id(byte unknownStreamId)
        {
            var bytes = new byte[] { (byte)'A', (byte)'K', (byte)'K', (byte)'A', unknownStreamId };
            var sequence = new ReadOnlySequence<byte>(bytes);

            Assert.Throws<ArteryFramingException>(() =>
                ArteryConnectionHeader.TryParse(sequence, out _, out _));
        }

        #endregion

        #region ArteryFrameParser - construction

        [Theory(DisplayName = "ArteryFrameParser constructor should reject a non-positive maxFrameLength")]
        [InlineData(0)]
        [InlineData(-1)]
        public void ArteryFrameParser_ctor_should_reject_non_positive_max_frame_length(int maxFrameLength)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ArteryFrameParser(maxFrameLength));
        }

        [Fact(DisplayName = "ArteryFrameParser constructor should reject a maxFrameLength above 0x00FFFFFF")]
        public void ArteryFrameParser_ctor_should_reject_max_frame_length_above_hard_cap()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ArteryFrameParser(ArteryFrameParser.MaxAllowedFrameLength + 1));
        }

        [Fact(DisplayName = "ArteryFrameParser constructor should accept the hard-cap maxFrameLength")]
        public void ArteryFrameParser_ctor_should_accept_hard_cap_max_frame_length()
        {
            var parser = new ArteryFrameParser(ArteryFrameParser.MaxAllowedFrameLength);
            parser.MaxFrameLength.Should().Be(ArteryFrameParser.MaxAllowedFrameLength);
        }

        #endregion

        #region ArteryFrameParser - basic framing

        [Fact(DisplayName = "ArteryFrameParser should parse a single complete frame delivered in one chunk")]
        public void ArteryFrameParser_should_parse_single_complete_frame_in_one_chunk()
        {
            var parser = new ArteryFrameParser(1024);
            var body = Bytes("hello, artery");
            var encoded = Encode(body);

            parser.Append(encoded);

            parser.TryReadFrame(out var frame).Should().BeTrue();
            frame.ToArray().Should().Equal(body);
            frame.IsSingleSegment.Should().BeTrue();

            // No more frames buffered.
            parser.TryReadFrame(out _).Should().BeFalse();
        }

        [Fact(DisplayName = "ArteryFrameParser should parse a frame delivered one byte at a time")]
        public void ArteryFrameParser_should_parse_frame_delivered_byte_by_byte()
        {
            var parser = new ArteryFrameParser(1024);
            var body = Bytes("worst-case fragmentation");
            var encoded = Encode(body);

            for (var i = 0; i < encoded.Length - 1; i++)
            {
                parser.Append(new[] { encoded[i] });
                parser.TryReadFrame(out _).Should().BeFalse("the frame is not yet fully buffered");
            }

            // Append the final byte - the frame should now be complete.
            parser.Append(new[] { encoded[^1] });

            parser.TryReadFrame(out var frame).Should().BeTrue();
            frame.ToArray().Should().Equal(body);
        }

        [Fact(DisplayName = "ArteryFrameParser should parse multiple frames delivered in a single chunk")]
        public void ArteryFrameParser_should_parse_multiple_frames_in_one_chunk()
        {
            var parser = new ArteryFrameParser(1024);
            var body1 = Bytes("first frame");
            var body2 = Bytes("second frame, a bit longer");
            var body3 = Bytes("");

            var combined = Encode(body1).Concat(Encode(body2)).Concat(Encode(body3)).ToArray();
            parser.Append(combined);

            parser.TryReadFrame(out var frame1).Should().BeTrue();
            frame1.ToArray().Should().Equal(body1);

            parser.TryReadFrame(out var frame2).Should().BeTrue();
            frame2.ToArray().Should().Equal(body2);

            parser.TryReadFrame(out var frame3).Should().BeTrue();
            frame3.ToArray().Should().Equal(body3);
            frame3.Length.Should().Be(0);

            parser.TryReadFrame(out _).Should().BeFalse();
        }

        [Fact(DisplayName = "ArteryFrameParser should parse a frame spanning three or more appended chunks")]
        public void ArteryFrameParser_should_parse_frame_spanning_multiple_chunks()
        {
            var parser = new ArteryFrameParser(1024);
            var body = Bytes("a frame body long enough to split across several chunks of input");
            var encoded = Encode(body);

            // Split into 5 chunks of varying (small) size so the frame body definitely spans
            // more than the length-field chunk.
            var chunkSize = Math.Max(1, encoded.Length / 5);
            var offset = 0;
            var chunkCount = 0;
            while (offset < encoded.Length)
            {
                var take = Math.Min(chunkSize, encoded.Length - offset);
                parser.Append(encoded.AsMemory(offset, take));
                offset += take;
                chunkCount++;
            }

            chunkCount.Should().BeGreaterOrEqualTo(3, "the test setup should actually exercise multi-chunk spanning");

            parser.TryReadFrame(out var frame).Should().BeTrue();
            frame.ToArray().Should().Equal(body);
            frame.IsSingleSegment.Should().BeFalse("the body was delivered across multiple appended chunks");
        }

        [Fact(DisplayName = "ArteryFrameParser should handle interleaved partial-then-complete sequences")]
        public void ArteryFrameParser_should_handle_interleaved_partial_then_complete_sequences()
        {
            var parser = new ArteryFrameParser(1024);
            var body1 = Bytes("alpha");
            var body2 = Bytes("beta, a somewhat longer message body");
            var encoded1 = Encode(body1);
            var encoded2 = Encode(body2);

            // Deliver frame 1 in two partial pieces.
            var splitPoint = encoded1.Length / 2;
            parser.Append(encoded1.AsMemory(0, splitPoint));
            parser.TryReadFrame(out _).Should().BeFalse();

            parser.Append(encoded1.AsMemory(splitPoint));
            parser.TryReadFrame(out var frame1).Should().BeTrue();
            frame1.ToArray().Should().Equal(body1);

            // Deliver frame 2's length field and part of its body, then the rest.
            var lengthPlusPartialBody = ArteryFrameParser.LengthFieldSize + (body2.Length / 3);
            parser.Append(encoded2.AsMemory(0, lengthPlusPartialBody));
            parser.TryReadFrame(out _).Should().BeFalse();

            parser.Append(encoded2.AsMemory(lengthPlusPartialBody));
            parser.TryReadFrame(out var frame2).Should().BeTrue();
            frame2.ToArray().Should().Equal(body2);

            parser.TryReadFrame(out _).Should().BeFalse();
        }

        #endregion

        #region ArteryFrameParser - length boundaries

        [Fact(DisplayName = "ArteryFrameParser should parse a legal empty-body frame")]
        public void ArteryFrameParser_should_parse_empty_body_frame()
        {
            var parser = new ArteryFrameParser(1024);
            parser.Append(Encode(Array.Empty<byte>()));

            parser.TryReadFrame(out var frame).Should().BeTrue();
            frame.Length.Should().Be(0);
        }

        [Fact(DisplayName = "ArteryFrameParser should accept a body of exactly maxFrameLength")]
        public void ArteryFrameParser_should_accept_body_of_exactly_max_frame_length()
        {
            const int maxFrameLength = 64;
            var parser = new ArteryFrameParser(maxFrameLength);
            var body = new byte[maxFrameLength];
            new Random(42).NextBytes(body);

            parser.Append(Encode(body));

            parser.TryReadFrame(out var frame).Should().BeTrue();
            frame.ToArray().Should().Equal(body);
        }

        [Fact(DisplayName = "ArteryFrameParser should throw ArteryFramingException for a body of maxFrameLength + 1")]
        public void ArteryFrameParser_should_throw_for_body_exceeding_max_frame_length()
        {
            const int maxFrameLength = 64;
            var parser = new ArteryFrameParser(maxFrameLength);
            var body = new byte[maxFrameLength + 1];

            parser.Append(Encode(body));

            Assert.Throws<ArteryFramingException>(() => parser.TryReadFrame(out _));
        }

        #endregion

        #region ArteryFrameParser - reuse

        [Fact(DisplayName = "ArteryFrameParser should support continued append/read cycles after yielding frames")]
        public void ArteryFrameParser_should_support_reuse_after_yielding_frames()
        {
            var parser = new ArteryFrameParser(1024);

            for (var i = 0; i < 5; i++)
            {
                var body = Bytes($"message #{i}");
                parser.Append(Encode(body));

                parser.TryReadFrame(out var frame).Should().BeTrue();
                frame.ToArray().Should().Equal(body);
                parser.TryReadFrame(out _).Should().BeFalse();
            }
        }

        #endregion

        #region Test helpers

        private static byte[] Bytes(string s) => System.Text.Encoding.UTF8.GetBytes(s);

        /// <summary>Encodes a frame as <c>[4B LE length][body]</c>, matching <see cref="ArteryFrameParser"/>'s wire format.</summary>
        private static byte[] Encode(byte[] body)
        {
            var frame = new byte[ArteryFrameParser.LengthFieldSize + body.Length];
            ArteryFrameParser.WriteFrameLength(frame, body.Length);
            body.CopyTo(frame, ArteryFrameParser.LengthFieldSize);
            return frame;
        }

        #endregion
    }
}
