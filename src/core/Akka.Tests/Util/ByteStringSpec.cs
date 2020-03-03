//-----------------------------------------------------------------------
// <copyright file="ByteStringSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Text;
using Akka.IO;
using FsCheck;
using Xunit;

namespace Akka.Tests.Util
{

    /// <summary>
    /// TODO: Should we use the FsCheck.XUnit integration when they upgrade to xUnit 2
    /// </summary>
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
            Prop.ForAll((ByteString a, ByteString b) => (a + b).Slice(b.Count).Count == a.Count)
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_be_sequential_when_slicing_from_start()
        {
            Prop.ForAll((ByteString a, ByteString b) => (a + b).Slice(0, a.Count).SequenceEqual(a))
                .QuickCheckThrowOnFailure();
        }
        [Fact]
        public void A_ByteString_must_be_sequential_when_slicing_from_index()
        {
            Prop.ForAll((ByteString a, ByteString b) => (a + b).Slice(a.Count).SequenceEqual(b))
                .QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_be_equal_to_the_original_when_compacting()
        {
            Prop.ForAll((ByteString xs) =>
            {
                var ys = xs.Compact();
                return xs.SequenceEqual(ys) && ys.IsCompact;
            }).QuickCheckThrowOnFailure();
        }

        [Fact]
        public void A_ByteString_must_be_equal_to_the_original_when_recombining()
        {
            Prop.ForAll((ByteString xs, int until) =>
            {
                var tmp1 = xs.Slice(0, until);
                var tmp2 = xs.Slice(until);
                var tmp11 = tmp1.Slice(0, until);
                var tmp12 = tmp1.Slice(until);
                return (tmp11 + tmp12 + tmp2).SequenceEqual(xs);
            }).QuickCheckThrowOnFailure();
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
            Prop.ForAll((ByteString a) =>
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
    }
}
