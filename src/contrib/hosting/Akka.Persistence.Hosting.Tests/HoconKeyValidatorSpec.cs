// -----------------------------------------------------------------------
//  <copyright file="HoconKeyValidatorSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Hosting.Tests;

public class HoconKeyValidatorSpec
{
    [MemberData(nameof(StringFactory))]
    [Theory(DisplayName = "HOCON key validator should detect illegal characters")]
    public void ValidatorTest(string input, string[] illegals)
    {
        var illegalChars = input.IsIllegalHoconKey();
        illegalChars.Length.Should().Be(illegals.Length);
        illegalChars.Should().BeEquivalentTo(illegals);
    }

    public static IEnumerable<object[]> StringFactory()
    {
        yield return new object[] { "a.:\u0020", new []{".", ":", "\\u0020"} };
        yield return new object[] { "a.b.c:d:", new []{".", ":"} };
        yield return new object[] { "a..c::", new []{".", ":"} };
        yield return new object[] { "\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006", new []{"\\u00A0", "\\u1680", "\\u2000", "\\u2001", "\\u2002", "\\u2003", "\\u2004", "\\u2005", "\\u2006"} };
        yield return new object[] { "=,#`^", new []{"=", ",", "#", "`", "^"} };
        yield return new object[] { "[x]y{z}", new []{"[", "]", "{", "}"} };
        yield return new object[] { "-_%()'~|+01234567890abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ<>", Array.Empty<string>() };
    }
}