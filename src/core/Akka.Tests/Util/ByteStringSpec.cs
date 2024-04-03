//-----------------------------------------------------------------------
// <copyright file="ByteStringSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.IO;
using FluentAssertions;
using FsCheck;
using Xunit;

namespace Akka.Tests.Util
{
    public class ByteStringSpec
    {
        class Generators
        {
            // TODO: Align with JVM Akka Generator
            public static Arbitrary<ByteString> ByteStrings()
            {
                return Arb.From(Arb.Generate<byte[]>().Select(ByteString.CopyFrom));
            }
        }

        public ByteStringSpec()
        {
            Arb.Register<Generators>();
        }

        [Fact]
        public void A_ByteString_must_have_correct_size_when_concatenating()
        {
            Prop.ForAll((ByteString a, ByteString b) => (a + b).Count == a.Count + b.Count)
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_have_correct_size_when_slicing_from_index()
        {
            var a = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var b = ByteString.FromBytes(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 });

            (a + b).Slice(b.Count).Count.Should().Be(a.Count);
        }

        [Fact]
        public void A_ByteString_must_be_sequential_when_slicing_from_start()
        {
            Prop.ForAll((ByteString a, ByteString b) =>
                    (a + b).Slice(0, a.Count).Memory.Span.SequenceEqual(a.Memory.Span))
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_be_sequential_when_slicing_from_index()
        {
            var a = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var b = ByteString.FromBytes(new byte[] { 10, 11, 12, 13, 14, 15, 16, 17, 18 });

            (a + b).Slice(a.Count).Should().BeEquivalentTo(b);
        }

        [Fact]
        public void A_ByteString_must_be_equal_to_the_original_when_recombining()
        {
            var xs = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var tmp1 = xs.Slice(0, xs.Count / 2);
            var tmp2 = xs.Slice(xs.Count / 2);
            var tmp11 = tmp1.Slice(0, tmp1.Count / 2);
            var tmp12 = tmp1.Slice(tmp1.Count / 2);
            (tmp11 + tmp12 + tmp2).Should().BeEquivalentTo(xs);
        }

        [Fact]
        public void A_ByteString_must_behave_as_expected_when_created_from_and_decoding_to_String()
        {
            Prop.ForAll((string s) =>
                    ByteString.FromString(s, Encoding.UTF8).ToString(Encoding.UTF8) ==
                    (s ?? "")) // TODO: What should we do with null string?
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_behave_as_expected_when_created_from_and_decoding_to_unicode_String()
        {
            Prop.ForAll(
                    (string s) =>
                        ByteString.FromString(s, Encoding.Unicode).ToString(Encoding.Unicode) ==
                        (s ?? "")) // TODO: What should we do with null string?
                .QuickCheckThrowOnFailure();
        }

        [Fact(DisplayName =
            @"A concatenated byte string should return the index of a byte in one the two byte strings.")]
        public void A_concatenated_bytestring_must_return_correct_index_of_elements_in_string()
        {
            var b = ByteString.FromBytes(new byte[] { 1 }) + ByteString.FromBytes(new byte[] { 2 });
            int offset = b.IndexOf(2);

            Assert.Equal(1, offset);
        }

        [Fact(DisplayName =
            @"A concatenated byte string should return -1 when it was not found in the concatenated byte strings")]
        public void A_concatenated_bytestring_must_return_negative_one_when_an_element_was_not_found()
        {
            var b = ByteString.FromBytes(new byte[] { 1 }) + ByteString.FromBytes(new byte[] { 2 });
            int offset = b.IndexOf(3);

            Assert.Equal(-1, offset);
        }

        [Fact(DisplayName =
            "A concatenated byte string composed of partial characters must return the correct string for ToString(Unicode)")]
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

        [Fact(DisplayName =
            "A concatenated byte string composed of partial characters must return the correct string for ToString(UTF8)")]
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

            var actual = data.ToString(encoding);
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

        // generate a test case for the ByteString.HasSubstring method, when one big ByteString contains another ByteString
        [Fact]
        public void A_ByteString_must_return_true_when_containing_another_ByteString()
        {
            Prop.ForAll((ByteString a, ByteString b) =>
                {
                    var big = a + b + a;
                    return ByteStringHasSubstringZeroIndex().Label($"big: {big}, b: {b}")
                        .And(ByteStringHasSubstringNonZeroIndex().Label($"big: {big}, b: {b}"));

                    bool ByteStringHasSubstringZeroIndex()
                    {
                        return big.HasSubstring(b, 0);
                    }

                    bool ByteStringHasSubstringNonZeroIndex()
                    {
                        return big.HasSubstring(b, a.Count);
                    }
                })
                .QuickCheckThrowOnFailure();
        }

        // generate a test case for ByteString.IndexOf of another ByteString and ensure that the index is correct (should match the beginning of the other ByteString)
        [Fact]
        public void A_ByteString_must_return_correct_index_when_containing_another_ByteString()
        {
            Prop.ForAll((ByteString a, ByteString b) =>
                {
                    return Prop.When(b.Count > 0, () =>
                    {
                        var big = a + b + a;
                        var i = ByteStringIndexOfZeroIndex();
                        var g = ByteStringIndexOfNonZeroIndex();

                        return (i == a.Count).Label($"big: {big}, b: {b}, expected start: {a.Count}, actual start: {i}")
                            .And((g == a.Count).Label($"big: {big}, b: {b}, expected start: {a.Count}, actual start: {g}"));

                        int ByteStringIndexOfZeroIndex()
                        {
                            return big.IndexOf(b, 0);
                        }

                        int ByteStringIndexOfNonZeroIndex()
                        {
                            return big.IndexOf(b, a.Count);
                        }
                    });
                })
                .QuickCheckThrowOnFailure();
        }
        
        // write a unit test for the IndexOf method when we're looking for a single byte - do it without using FsCheck
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void A_ByteString_must_return_correct_index_when_containing_a_single_byte(int startingIndex)
        {
            var a = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var b = ByteString.FromBytes(new byte[] { 5 });

            var i = a.IndexOf(b, startingIndex);
            Assert.Equal(4, i);
            
            // also do the comparison with the byte value
            var j = a.IndexOf(5, startingIndex);
            Assert.Equal(4, j);
        }
        
        [Fact]
        public void A_ByteString_must_return_correct_index_when_containing_a_single_byte_front()
        {
            var a = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var b = ByteString.FromBytes(new byte[] { 1 });

            var i = a.IndexOf(b);
            Assert.Equal(0, i);
            
            // also do the comparison with the byte value
            var j = a.IndexOf(1);
            Assert.Equal(0, j);
        }
        
        [Fact]
        public void A_ByteString_must_return_correct_index_when_containing_a_single_byte_back()
        {
            var a = ByteString.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var b = ByteString.FromBytes(new byte[] { 9 });

            var i = a.IndexOf(b);
            Assert.Equal(8, i);
            
            // also do the comparison with the byte value
            var j = a.IndexOf(9);
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
    }
}