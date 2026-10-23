//-----------------------------------------------------------------------
// <copyright file="ByteStringSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Akka.IO;
using Akka.TestKit;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using Xunit;

namespace Akka.Tests.Util
{

    /// <summary>
    /// TODO: Should we use the FsCheck.XUnit integration when they upgrade to xUnit 2
    /// </summary>
    public class ByteStringSpec
    {
        // TODO: Align with JVM Akka Generator
        private static readonly Arbitrary<ByteString> ByteStringArb =
            Arb.From(ArbMap.Default.GeneratorFor<byte[]>().Select(ByteString.CopyFrom));

        [Fact]
        public void A_ByteString_must_have_correct_size_when_concatenating()
        {
            Prop.ForAll(ByteStringArb, ByteStringArb, (ByteString a, ByteString b) => (a + b).Count == a.Count + b.Count)
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_ToReadOnlySpan_must_have_correct_size()
        {
            Prop.ForAll(ByteStringArb, ByteStringArb, (ByteString a, ByteString b) =>
            {
                a.ToReadOnlySpan().Length.Should().Be(a.Count);
                b.ToReadOnlySpan().Length.Should().Be(b.Count);
                var concat = a + b;
                var spanConcat = concat.ToReadOnlySpan();
                return spanConcat.Length == concat.Count;
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_Compact_ToReadOnlySpan_must_have_identical_memory_ref()
        {
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var a = ByteString.FromBytes(bytes);
            var aSpan = a.ToReadOnlySpan();
            
            var memory = new ReadOnlyMemory<byte>(bytes);
            var spanMemory = memory.Span;

            // span has memory reference to underlying byte array 
            (aSpan == spanMemory).Should().BeTrue();
            
            // Do another ToReadOnlySpan
            var span2 = a.ToReadOnlySpan();
            (aSpan == span2).Should().BeTrue();
        }
        
        [Fact]
        public void A_ByteString_ToReadOnlySpan_compacted_must_have_identical_memory_ref()
        {
            var bytesA = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
            var bytesB = new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 };
            var a = ByteString.FromBytes(bytesA);
            var b = ByteString.FromBytes(bytesB);
            
            var concat = a + b;
            
            var concatSpan = concat.ToReadOnlySpan();
            var concatSpan2 = concat.ToReadOnlySpan();
            // not compact, returns a new span
            (concatSpan == concatSpan2).Should().BeFalse();

            // compact, will return same span
            var compacted = concat.Compact();
            var concatSpan3 = compacted.ToReadOnlySpan();
            var concatSpan4 = compacted.ToReadOnlySpan();
            (concatSpan3 == concatSpan4).Should().BeTrue();
        }

        [Fact]
        public void A_ByteString_must_have_correct_size_when_slicing_from_index()
        {
            var a = ByteString.FromBytes(new byte[]{ 1, 2, 3, 4, 5, 6, 7, 8, 9} );
            var b = ByteString.FromBytes(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 });

            (a + b).Slice(b.Count).Count.Should().Be(a.Count);
        }

        [Fact]
        public void A_ByteString_must_be_sequential_when_slicing_from_start()
        {
            Prop.ForAll(ByteStringArb, ByteStringArb, (ByteString a, ByteString b) => (a + b).Slice(0, a.Count).SequenceEqual(a))
                .QuickCheckThrowOnFailure();
        }
        [Fact]
        public void A_ByteString_must_be_sequential_when_slicing_from_index()
        {
            var a = ByteString.FromBytes(new byte[]{ 1, 2, 3, 4, 5, 6, 7, 8, 9} );
            var b = ByteString.FromBytes(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 });

            (a + b).Slice(a.Count).Should().BeEquivalentTo(b);
        }

        [Fact]
        public void A_ByteString_must_be_equal_to_the_original_when_compacting()
        {
            Prop.ForAll(ByteStringArb, (ByteString xs) =>
            {
                var ys = xs.Compact();
                return xs.SequenceEqual(ys) && ys.IsCompact;
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_be_equal_to_the_original_when_recombining()
        {
            var xs = ByteString.FromBytes(new byte[]{ 1, 2, 3, 4, 5, 6, 7, 8, 9} );
            var tmp1 = xs.Slice(0, xs.Count / 2);
            var tmp2 = xs.Slice(xs.Count / 2);
            var tmp11 = tmp1.Slice(0, tmp1.Count / 2);
            var tmp12 = tmp1.Slice(tmp1.Count / 2);
            (tmp11 + tmp12 + tmp2).Should().BeEquivalentTo(xs);
        }

        [Fact]
        public void A_ByteString_must_behave_as_expected_when_created_from_and_decoding_to_String()
        {
            Prop.ForAll((string s) => ByteString.FromString(s, Encoding.UTF8).ToString(Encoding.UTF8) == (s ?? "")) // TODO: What should we do with null string?
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_behave_as_expected_when_created_from_and_decoding_to_unicode_String()
        {
            Prop.ForAll((string s) => ByteString.FromString(s, Encoding.Unicode).ToString(Encoding.Unicode) == (s ?? "")) // TODO: What should we do with null string?
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_behave_as_expected_when_compacting()
        {
            Prop.ForAll(ByteStringArb, (ByteString a) =>
            {
                var wasCompact = a.IsCompact;
                var b = a.Compact();
                return ((!wasCompact) || (b == a)) &&
                       b.SequenceEqual(a) &&
                       b.IsCompact &&
                       b.Compact() == b;
            }).QuickCheckThrowOnFailure();
        }

        [Fact(DisplayName = @"A concatenated byte string should return the index of a byte in one the two byte strings.")]
        public void A_concatenated_bytestring_must_return_correct_index_of_elements_in_string()
        {
            var b = ByteString.FromBytes(new byte[] { 1 }) + ByteString.FromBytes(new byte[] { 2 });
            int offset = b.IndexOf(2);

            Assert.Equal(1, offset);
        }

        [Fact(DisplayName = @"A concatenated byte string should return -1 when it was not found in the concatenated byte strings")]
        public void A_concatenated_bytestring_must_return_negative_one_when_an_element_was_not_found()
        {
            var b = ByteString.FromBytes(new byte[] { 1 }) + ByteString.FromBytes(new byte[] { 2 });
            int offset = b.IndexOf(3);

            Assert.Equal(-1, offset);
        }

        [Fact(DisplayName = "A concatenated byte string composed of partial characters must return the correct string for ToString(Unicode)")]
        public void A_concatenated_ByteString_with_partial_characters_must_return_correct_string_for_ToString_Unicode()
        {
            // In Unicode encoding, characters present in the ASCII character set are 2 bytes long.

            const string expected = "ǢBC";
            Encoding encoding = Encoding.Unicode;

            byte[] rawData = encoding.GetBytes(expected);

            ByteString data = ByteString.Empty;
            data += ByteString.CopyFrom(rawData, 0, 3); // One and a half characters
            data += ByteString.CopyFrom(rawData, 3, 3); // One and a half characters
            Assert.Equal(rawData.Length, data.Count);

            string actual = data.ToString(encoding);
            Assert.Equal(expected, actual);
        }

        [Fact(DisplayName = "A concatenated byte string composed of partial characters must return the correct string for ToString(UTF8)")]
        public void A_concatenated_ByteString_with_partial_characters_must_return_correct_string_for_ToString_UTF8()
        {
            // In UTF-8 encoding, characters present in the ASCII character set are only 1 byte long.

            const string expected = "ǢBC";
            Encoding encoding = Encoding.UTF8;

            byte[] rawData = encoding.GetBytes(expected);

            ByteString data = ByteString.Empty;
            data += ByteString.CopyFrom(rawData, 0, 1); // Half a character
            data += ByteString.CopyFrom(rawData, 1, 3); // One and a half characters
            Assert.Equal(rawData.Length, data.Count);

            string actual = data.ToString(encoding);
            Assert.Equal(expected, actual);
        }

        [Fact(DisplayName = "A sliced byte string must return the correct string for ToString")]
        public void A_sliced_ByteString_must_return_correct_string_for_ToString()
        {
            const string expected = "ABCDEF";
            Encoding encoding = Encoding.ASCII;

            int halfExpected = expected.Length / 2;

            string expectedLeft = expected.Substring(startIndex: 0, length: halfExpected);
            string expectedRight = expected.Substring(startIndex: halfExpected, length: halfExpected);

            ByteString data = ByteString.FromString(expected, encoding);

            string actualLeft = data.Slice(index: 0, count: halfExpected).ToString(encoding);
            string actualRight = data.Slice(index: halfExpected, count: halfExpected).ToString(encoding);

            Assert.Equal(expectedLeft, actualLeft);
            Assert.Equal(expectedRight, actualRight);
        }

#if !NETFRAMEWORK
        [Fact(DisplayName = "A sliced byte string using Range must return the correct string for ToString")]
        public void A_sliced_ByteString_using_Range_must_return_correct_string_for_ToString()
        {
            const string expected = "ABCDEF";
            Encoding encoding = Encoding.ASCII;

            int halfExpected = expected.Length / 2;

            string expectedLeft = expected.Substring(startIndex: 0, length: halfExpected);
            string expectedRight = expected.Substring(startIndex: halfExpected, length: halfExpected);

            ByteString data = ByteString.FromString(expected, encoding);

            string actualLeft = data[..halfExpected].ToString(encoding);
            string actualRight = data[halfExpected..].ToString(encoding);

            Assert.Equal(expectedLeft, actualLeft);
            Assert.Equal(expectedRight, actualRight);
        }
#endif

        [Fact(DisplayName = "A ByteString must copy into a Span sized exactly to count")]
        public void A_ByteString_must_copy_into_Span_sized_exactly_to_count()
        {
            var byteString = ByteString.FromBytes(SequentialBytes(2048));
            Span<byte> destination = new byte[1024];

            var copied = byteString.CopyTo(ref destination, 0, 1024);

            copied.Should().Be(1024);
            destination.ToArray().Should().Equal(Segment(SequentialBytes(2048), 0, 1024));
        }

        [Fact(DisplayName = "A ByteString must copy into a Memory sized exactly to count")]
        public void A_ByteString_must_copy_into_Memory_sized_exactly_to_count()
        {
            var byteString = ByteString.FromBytes(SequentialBytes(2048));
            Memory<byte> destination = new byte[1024];

            var copied = byteString.CopyTo(ref destination, 0, 1024);

            copied.Should().Be(1024);
            destination.ToArray().Should().Equal(Segment(SequentialBytes(2048), 0, 1024));
        }

        [Fact(DisplayName = "A ByteString must truncate a segment longer than the remaining Span space")]
        public void A_ByteString_must_truncate_segment_longer_than_remaining_Span_space()
        {
            // concat keeps two segments; the copy has to stop mid-way through the second one
            var byteString = ByteString.FromBytes(SequentialBytes(64)) + ByteString.FromBytes(SequentialBytes(64, 64));

            Span<byte> destination = new byte[96];
            destination.Fill(0xFF);

            var copied = byteString.CopyTo(ref destination, 0, 96);

            copied.Should().Be(96);
            destination.ToArray().Should().Equal(Segment(SequentialBytes(128), 0, 96));
        }

        [Fact(DisplayName = "A ByteString must honor a non-zero destination index when copying to a Span")]
        public void A_ByteString_must_honor_non_zero_destination_index_when_copying_to_Span()
        {
            var byteString = ByteString.FromBytes(SequentialBytes(32));

            Span<byte> destination = new byte[64];
            destination.Fill(0xFF);

            // index 16, count 32 -> destination is exactly large enough to hold the copy
            var copied = byteString.CopyTo(ref destination, 16, 32);

            copied.Should().Be(32);
            destination.Slice(0, 16).ToArray().Should().OnlyContain(b => b == 0xFF);
            destination.Slice(16, 32).ToArray().Should().Equal(SequentialBytes(32));
            destination.Slice(48, 16).ToArray().Should().OnlyContain(b => b == 0xFF);
        }

        [Fact(DisplayName = "A sliced ByteString with a buffer offset must copy the right bytes to a Span")]
        public void A_sliced_ByteString_with_buffer_offset_must_copy_correct_bytes_to_Span()
        {
            // Slice reuses the backing array, so the segment carries a non-zero Offset
            var sliced = ByteString.FromBytes(SequentialBytes(128)).Slice(32, 64);

            Span<byte> destination = new byte[64];

            var copied = sliced.CopyTo(ref destination, 0, 64);

            copied.Should().Be(64);
            destination.ToArray().Should().Equal(Segment(SequentialBytes(128), 32, 64));
        }

        [Fact(DisplayName = "A ByteString CopyTo must not write past count into a Span or Memory")]
        public void A_ByteString_CopyTo_must_not_write_past_count()
        {
            var byteString = ByteString.FromBytes(SequentialBytes(512));

            Span<byte> spanDestination = new byte[256];
            spanDestination.Fill(0xFF);
            byteString.CopyTo(ref spanDestination);
            spanDestination.ToArray().Should().Equal(Segment(SequentialBytes(512), 0, 256));

            Memory<byte> memoryDestination = new byte[256];
            memoryDestination.Span.Fill(0xFF);
            byteString.CopyTo(ref memoryDestination);
            memoryDestination.ToArray().Should().Equal(Segment(SequentialBytes(512), 0, 256));
        }

        [Fact(DisplayName = "A ByteString CopyTo to any Span at least as large as count must never throw")]
        public void A_ByteString_CopyTo_to_Span_at_least_count_must_never_throw()
        {
            Prop.ForAll(ByteStringArb, Arb.From(Gen.Choose(1, 4096)), (byteString, size) =>
            {
                var expected = byteString.ToArray();
                var count = Math.Min(size, expected.Length);

                var buffer = new byte[Math.Max(size, expected.Length)];
                Span<byte> destination = buffer;
                var copied = byteString.CopyTo(ref destination, 0, size);

                // never copies more than the ByteString holds
                copied.Should().Be(count);
                destination.Slice(0, count).ToArray().Should().Equal(Segment(expected, 0, count));
                return true;
            }).QuickCheckThrowOnFailure();
        }

        private static byte[] SequentialBytes(int length, byte start = 0)
        {
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
                bytes[i] = (byte)(start + i % byte.MaxValue);
            return bytes;
        }

        // net48 has no Range/Index support, so slices are taken explicitly instead of with [..] syntax
        private static byte[] Segment(byte[] source, int start, int length)
        {
            var destination = new byte[length];
            Array.Copy(source, start, destination, 0, length);
            return destination;
        }
    }
}
