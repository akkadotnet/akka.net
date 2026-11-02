//-----------------------------------------------------------------------
// <copyright file="SemanticFormatterNullAndEdgeCaseSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Event;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Loggers;

/// <summary>
/// Pure unit tests for <see cref="SemanticLogMessageFormatter"/> edge cases.
/// No actor system required - tests the formatter directly.
/// </summary>
public class SemanticFormatterNullAndEdgeCaseSpecs
{
    private static readonly SemanticLogMessageFormatter Formatter = SemanticLogMessageFormatter.Instance;

    #region Decimal Edge Cases

    [Fact(DisplayName = "Should preserve trailing zeros in decimal: 1.00m → '1.00'")]
    public void Decimal_trailing_zeros_preserved()
    {
        var result = Formatter.Format("{Amount}", 1.00m);
        result.Should().Be("1.00");
    }

    [Fact(DisplayName = "Should preserve single trailing zero in decimal: 100.0m → '100.0'")]
    public void Decimal_single_trailing_zero_preserved()
    {
        var result = Formatter.Format("{Amount}", 100.0m);
        result.Should().Be("100.0");
    }

    [Fact(DisplayName = "Should not add trailing zero for whole decimal: 100m → '100'")]
    public void Decimal_whole_number_no_trailing_zero()
    {
        var result = Formatter.Format("{Amount}", 100m);
        result.Should().Be("100");
    }

    [Fact(DisplayName = "Should render nullable decimal null as 'null'")]
    public void Nullable_decimal_null_renders_as_null()
    {
        decimal? value = null;
        var result = Formatter.Format("{Amount}", new object[] { value });
        result.Should().Be("null");
    }

    [Fact(DisplayName = "Should render nullable decimal with value same as non-nullable")]
    public void Nullable_decimal_with_value_same_as_non_nullable()
    {
        decimal? value = 100.0m;
        var result = Formatter.Format("{Amount}", new object[] { value });
        result.Should().Be("100.0");
    }

    [Fact(DisplayName = "Should apply N2 format specifier to decimal")]
    public void Decimal_format_specifier_N2()
    {
        var result = Formatter.Format("{Amount:N2}", 1234.5m);
        result.Should().Be(1234.5m.ToString("N2"));
    }

    [Fact(DisplayName = "Should apply C format specifier to decimal")]
    public void Decimal_format_specifier_C()
    {
        var result = Formatter.Format("{Amount:C}", 99.99m);
        result.Should().Be(99.99m.ToString("C"));
    }

    #endregion

    #region Numeric Types

    [Fact(DisplayName = "Should format int correctly")]
    public void Int_basic_formatting()
    {
        var result = Formatter.Format("{Value}", 42);
        result.Should().Be("42");
    }

    [Fact(DisplayName = "Should format long correctly")]
    public void Long_basic_formatting()
    {
        var result = Formatter.Format("{Value}", 9876543210L);
        result.Should().Be("9876543210");
    }

    [Fact(DisplayName = "Should format double correctly")]
    public void Double_basic_formatting()
    {
        var result = Formatter.Format("{Value}", 3.14d);
        result.Should().Be(3.14d.ToString());
    }

    [Fact(DisplayName = "Should format float correctly")]
    public void Float_basic_formatting()
    {
        var result = Formatter.Format("{Value}", 2.5f);
        result.Should().Be(2.5f.ToString());
    }

    [Fact(DisplayName = "Should render nullable int null as 'null'")]
    public void Nullable_int_null_renders_as_null()
    {
        int? value = null;
        var result = Formatter.Format("{Value}", new object[] { value });
        result.Should().Be("null");
    }

    [Fact(DisplayName = "Should render nullable double null as 'null'")]
    public void Nullable_double_null_renders_as_null()
    {
        double? value = null;
        var result = Formatter.Format("{Value}", new object[] { value });
        result.Should().Be("null");
    }

    [Fact(DisplayName = "Should render nullable int with value same as non-nullable")]
    public void Nullable_int_with_value_same_as_non_nullable()
    {
        int? value = 42;
        var result = Formatter.Format("{Value}", new object[] { value });
        result.Should().Be("42");
    }

    #endregion

    #region Common Types

    [Fact(DisplayName = "Should format DateTime using ToString")]
    public void DateTime_formatting()
    {
        var dt = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var result = Formatter.Format("{Timestamp}", dt);
        result.Should().Be(dt.ToString());
    }

    [Fact(DisplayName = "Should format DateTimeOffset using ToString")]
    public void DateTimeOffset_formatting()
    {
        var dto = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);
        var result = Formatter.Format("{Timestamp}", dto);
        result.Should().Be(dto.ToString());
    }

    [Fact(DisplayName = "Should format TimeSpan using ToString")]
    public void TimeSpan_formatting()
    {
        var ts = TimeSpan.FromMinutes(5.5);
        var result = Formatter.Format("{Duration}", ts);
        result.Should().Be(ts.ToString());
    }

    [Fact(DisplayName = "Should format Guid using ToString")]
    public void Guid_formatting()
    {
        var guid = Guid.NewGuid();
        var result = Formatter.Format("{Id}", guid);
        result.Should().Be(guid.ToString());
    }

    [Fact(DisplayName = "Should format bool as 'True'/'False'")]
    public void Bool_formatting()
    {
        Formatter.Format("{Flag}", true).Should().Be("True");
        Formatter.Format("{Flag}", false).Should().Be("False");
    }

    [Fact(DisplayName = "Should format enum as name, not numeric value")]
    public void Enum_formatting()
    {
        var result = Formatter.Format("{Level}", LogLevel.InfoLevel);
        result.Should().Be("InfoLevel");
    }

    #endregion

    #region Null and String Edge Cases

    [Fact(DisplayName = "Named template: null object renders as 'null'")]
    public void Named_template_null_renders_as_null_string()
    {
        var result = Formatter.Format("{Value}", new object[] { null });
        result.Should().Be("null");
    }

    [Fact(DisplayName = "Positional template: null renders as empty string via string.Format")]
    public void Positional_template_null_renders_as_empty_string()
    {
        // string.Format("{0}", null) treats null as empty string
        // This documents the asymmetry between named and positional templates
        var result = Formatter.Format("{0}", new object[] { null });
        result.Should().Be("");
    }

    [Fact(DisplayName = "Empty string stays empty, not rendered as 'null'")]
    public void Empty_string_stays_empty()
    {
        var result = Formatter.Format("{Value}", "");
        result.Should().Be("");
    }

    [Fact(DisplayName = "Regular string value rendered as-is")]
    public void String_value_rendered_as_is()
    {
        var result = Formatter.Format("{Name}", "Alice");
        result.Should().Be("Alice");
    }

    #endregion

    #region LogValues Boxing

    [Fact(DisplayName = "LogValues<decimal?> with null value boxes to null")]
    public void LogValues_nullable_decimal_null_boxes_to_null()
    {
        decimal? value = null;
        var logValues = new LogValues<decimal?>(value);
        logValues[0].Should().BeNull();
    }

    [Fact(DisplayName = "LogValues<int?> with null value boxes to null")]
    public void LogValues_nullable_int_null_boxes_to_null()
    {
        int? value = null;
        var logValues = new LogValues<int?>(value);
        logValues[0].Should().BeNull();
    }

    [Fact(DisplayName = "LogValues<decimal?> with value boxes correctly")]
    public void LogValues_nullable_decimal_with_value_boxes_correctly()
    {
        decimal? value = 100.0m;
        var logValues = new LogValues<decimal?>(value);
        logValues[0].Should().Be(100.0m);
    }

    [Fact(DisplayName = "LogValues<decimal> preserves trailing zeros through boxing")]
    public void LogValues_decimal_preserves_trailing_zeros()
    {
        var logValues = new LogValues<decimal>(100.0m);
        var formatted = Formatter.Format("{Amount}", logValues);
        formatted.Should().Be("100.0");
    }

    [Fact(DisplayName = "LogMessage<LogValues<T>> formats via generic path")]
    public void LogMessage_generic_path_formats_correctly()
    {
        var logValues = new LogValues<int>(42);
        var logMessage = new LogMessage<LogValues<int>>(
            Formatter, "User {UserId} logged in", logValues);

        logMessage.ToString().Should().Be("User 42 logged in");
    }

    [Fact(DisplayName = "LogMessage<LogValues<T>> with nullable null formats correctly")]
    public void LogMessage_generic_path_nullable_null_formats_correctly()
    {
        decimal? value = null;
        var logValues = new LogValues<decimal?>(value);
        var logMessage = new LogMessage<LogValues<decimal?>>(
            Formatter, "Amount: {Amount}", logValues);

        logMessage.ToString().Should().Be("Amount: null");
    }

    #endregion

    #region Multi-Arg Templates

    [Fact(DisplayName = "Two-arg template formats both values")]
    public void Two_arg_template()
    {
        var result = Formatter.Format("{Name} is {Age} years old", "Alice", 30);
        result.Should().Be("Alice is 30 years old");
    }

    [Fact(DisplayName = "Six-arg template with mixed types")]
    public void Six_arg_template_mixed_types()
    {
        var result = Formatter.Format(
            "{BetType} Bet on {Selection} at {Odds} for {Stake} returns {TotalReturns} (bonus: {BonusReturns})",
            "Win", "Horse A", 5.0m, 10.0m, 50.0m, (decimal?)null);
        result.Should().Be("Win Bet on Horse A at 5.0 for 10.0 returns 50.0 (bonus: null)");
    }

    #endregion
}
