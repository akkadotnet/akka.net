//-----------------------------------------------------------------------
// <copyright file="SpanHacksSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    public class SpanHacksSpec
    {
        public static TheoryData<long> BoundaryValues
        {
            get
            {
                var data = new TheoryData<long> { 0L, 9L, 10L, int.MaxValue, int.MaxValue + 1L, long.MaxValue };
                long power = 1;
                for (var k = 0; k <= 18; k++)
                {
                    data.Add(power);
                    if (power > 1)
                        data.Add(power - 1);
                    if (power > 10)
                        data.Add(power + 1);
                    if (k < 18)
                        power *= 10;
                }
                return data;
            }
        }

        [Theory(DisplayName = "Should_ReportExactDigitCount_When_SizingInt64")]
        [MemberData(nameof(BoundaryValues))]
        public void Should_ReportExactDigitCount_When_SizingInt64(long value)
        {
            SpanHacks.Int64SizeInCharacters(value).Should().Be(value.ToString(CultureInfo.InvariantCulture).Length);
        }

        [Theory(DisplayName = "Should_FormatExactly_When_FormattingInt64")]
        [MemberData(nameof(BoundaryValues))]
        public void Should_FormatExactly_When_FormattingInt64(long value)
        {
            Span<char> buffer = new char[32];
            var written = SpanHacks.TryFormat(value, 0, ref buffer);
            var text = buffer.Slice(0, written).ToString();

            text.Should().Be(value.ToString(CultureInfo.InvariantCulture));
            long.Parse(text, CultureInfo.InvariantCulture).Should().Be(value);
        }

        [Fact(DisplayName = "Should_StillReject_When_ParsingInt32OutOfRange")]
        public void Should_StillReject_When_ParsingInt32OutOfRange()
        {
            Action parse = () => SpanHacks.Parse("2147483648".AsSpan());
            parse.Should().Throw<FormatException>();
        }
    }
}
