//-----------------------------------------------------------------------
// <copyright file="SpanHacksSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    public class SpanHacksSpec
    {
        public static IEnumerable<object[]> BoundaryValues()
        {
            var values = new SortedSet<long> { 0L, 9L, 10L, int.MaxValue, int.MaxValue + 1L, long.MaxValue,
                10_000_000_000_000_000L - 40, 1_000_000_000_000_000_000L - 4000 };
            long power = 1;
            for (var k = 0; k <= 18; k++)
            {
                values.Add(power);
                values.Add(power - 1);
                values.Add(power + 1);
                if (k < 18)
                    power *= 10;
            }

            foreach (var v in values)
                yield return new object[] { v };
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
    }
}
