// -----------------------------------------------------------------------
//  <copyright file="UtilSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using FluentAssertions;
using Xunit;
using static FluentAssertions.FluentActions;

namespace Akka.Hosting.Tests;

public class UtilSpec
{
    [Fact(DisplayName = "Config.MoveTo() should move config to a new path properly")]
    public void MoveToSpec()
    {
        var hocon = (Config)"a: 1, b: { c: 2 }";
        var moved = hocon.MoveTo("x.y.z");
        moved.GetInt("x.y.z.a").Should().Be(1);
        moved.GetInt("x.y.z.b.c").Should().Be(2);
    }

    [Fact(DisplayName = "HoconExtensions TimeSpan.ToHocon should work properly")]
    public void TimeSpanToHoconSpec()
    {
        TimeSpan? nullTs = null;
        Invoking(() => nullTs.ToHocon()).Should()
            .Throw<ConfigurationException>()
            .WithMessage("Value can not be null", "Null value is not allowed");

        var ts = TimeSpan.Zero;
        ts.ToHocon().Should().Be("0");
        ts.ToHocon(true, false).Should().Be("0");
        ts.ToHocon(true, true).Should().Be("infinite");
        ts.ToHocon(false, false).Should().Be("0");
        Invoking(() => ts.ToHocon(false, true)).Should()
            .Throw<ConfigurationException>("Infinite value is not allowed", "Infinite value is not allowed, zero is considered as infinite");

        ts = TimeSpan.FromTicks(-1);
        ts.ToHocon(true, false).Should().Be("infinite");
        ts.ToHocon(true, true).Should().Be("infinite");
        Invoking(() => ts.ToHocon(false, true)).Should()
            .Throw<ConfigurationException>("Infinite value is not allowed", "Infinite value is not allowed");
        Invoking(() => ts.ToHocon(false, false)).Should()
            .Throw<ConfigurationException>("Infinite value is not allowed", "Infinite value is not allowed");
        
    }

    [InlineData("$")]
    [InlineData("\"")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData(":")]
    [InlineData("=")]
    [InlineData(",")]
    [InlineData("#")]
    [InlineData("`")]
    [InlineData("^")]
    [InlineData("?")]
    [InlineData("!")]
    [InlineData("@")]
    [InlineData("*")]
    [InlineData("&")]
    [InlineData("\\")]
    [Theory(DisplayName = "HoconExtensions String.ToHocon should put illegal characters in quotes")]
    public void StringToHoconTest(string input)
    {
        var result = input.ToHocon();

        switch (input)
        {
            case "\"":
                // special case for quote
                result.Length.Should().Be(4);
                break;
            case "\\":
                // special case for backslash
                result.Length.Should().Be(4);
                break;
            default:
                result.Length.Should().Be(3);
                break;
        }
        
        result.Should().StartWith("\"").And.EndWith("\"");
    }
}