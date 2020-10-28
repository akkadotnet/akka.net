//-----------------------------------------------------------------------
// <copyright file="TypeExtensionsTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Util
{
    public class AkkaVersionSpec
    {
        [Fact]
        public void Version_should_compare_3_digit_version()
        {
            AkkaVersion.Create("1.2.3").Should().Be(AkkaVersion.Create("1.2.3"));
            AkkaVersion.Create("1.2.3").Should().NotBe(AkkaVersion.Create("1.2.4"));
            AkkaVersion.Create("1.2.4").Should().BeGreaterThan(AkkaVersion.Create("1.2.3"));
            AkkaVersion.Create("3.2.1").Should().BeGreaterThan(AkkaVersion.Create("1.2.3"));
            AkkaVersion.Create("3.2.1").Should().BeLessThan(AkkaVersion.Create("3.3.1"));
            AkkaVersion.Create("3.2.0").Should().BeLessThan(AkkaVersion.Create("3.2.1"));
        }

        [Fact]
        public void Version_should_not_support_more_than_3_digits_version()
        {
            XAssert.Throws<ArgumentOutOfRangeException>(() => AkkaVersion.Create("1.2.3.1"));
        }

        [Fact]
        public void Version_should_compare_2_digit_version()
        {
            AkkaVersion.Create("1.2").Should().Be(AkkaVersion.Create("1.2"));
            AkkaVersion.Create("1.2").Should().Be(AkkaVersion.Create("1.2.0"));
            AkkaVersion.Create("1.2").Should().NotBe(AkkaVersion.Create("1.3"));
            AkkaVersion.Create("1.2.1").Should().BeGreaterThan(AkkaVersion.Create("1.2"));
            AkkaVersion.Create("2.4").Should().BeGreaterThan(AkkaVersion.Create("2.3"));
            AkkaVersion.Create("3.2").Should().BeLessThan(AkkaVersion.Create("3.2.7"));
        }

        [Fact]
        public void Version_should_compare_single_digit_version()
        {
            AkkaVersion.Create("1").Should().Be(AkkaVersion.Create("1"));
            AkkaVersion.Create("1").Should().Be(AkkaVersion.Create("1.0"));
            AkkaVersion.Create("1").Should().Be(AkkaVersion.Create("1.0.0"));
            AkkaVersion.Create("1").Should().NotBe(AkkaVersion.Create("2"));
            AkkaVersion.Create("3").Should().BeGreaterThan(AkkaVersion.Create("2"));

            AkkaVersion.Create("2b").Should().BeGreaterThan(AkkaVersion.Create("2a"));
            AkkaVersion.Create("2020-09-07").Should().BeGreaterThan(AkkaVersion.Create("2020-08-30"));
        }

        [Fact]
        public void Version_should_compare_extra()
        {
            AkkaVersion.Create("1.2.3-M1").Should().Be(AkkaVersion.Create("1.2.3-M1"));
            AkkaVersion.Create("1.2-M1").Should().Be(AkkaVersion.Create("1.2-M1"));
            AkkaVersion.Create("1.2.0-M1").Should().Be(AkkaVersion.Create("1.2-M1"));
            AkkaVersion.Create("1.2.3-M1").Should().NotBe(AkkaVersion.Create("1.2.3-M2"));
            AkkaVersion.Create("1.2-M1").Should().BeLessThan(AkkaVersion.Create("1.2.0"));
            AkkaVersion.Create("1.2.0-M1").Should().BeLessThan(AkkaVersion.Create("1.2.0"));
            AkkaVersion.Create("1.2.3-M2").Should().BeGreaterThan(AkkaVersion.Create("1.2.3-M1"));
        }

        [Fact]
        public void Version_should_require_digits()
        {
            XAssert.Throws<FormatException>(() => AkkaVersion.Create("1.x.3"));
            XAssert.Throws<FormatException>(() => AkkaVersion.Create("1.2x.3"));
            XAssert.Throws<FormatException>(() => AkkaVersion.Create("1.2.x"));
            XAssert.Throws<FormatException>(() => AkkaVersion.Create("1.2.3x"));

            XAssert.Throws<FormatException>(() => AkkaVersion.Create("x.3"));
            XAssert.Throws<FormatException>(() => AkkaVersion.Create("1.x"));
            XAssert.Throws<FormatException>(() => AkkaVersion.Create("1.2x"));
        }

        [Fact]
        public void Version_should_compare_dynver_format()
        {
            // dynver format
            AkkaVersion.Create("1.0.10+3-1234abcd").Should().BeLessThan(AkkaVersion.Create("1.0.11"));
            AkkaVersion.Create("1.0.10+3-1234abcd").Should().BeLessThan(AkkaVersion.Create("1.0.10+10-1234abcd"));
            AkkaVersion.Create("1.2+3-1234abcd").Should().BeLessThan(AkkaVersion.Create("1.2+10-1234abcd"));
            AkkaVersion.Create("1.0.0+3-1234abcd+20140707-1030").Should().BeLessThan(AkkaVersion.Create("1.0.0+3-1234abcd+20140707-1130"));
            AkkaVersion.Create("0.0.0+3-2234abcd").Should().BeLessThan(AkkaVersion.Create("0.0.0+4-1234abcd"));
            AkkaVersion.Create("HEAD+20140707-1030").Should().BeLessThan(AkkaVersion.Create("HEAD+20140707-1130"));

            AkkaVersion.Create("1.0.10-3-1234abcd").Should().BeLessThan(AkkaVersion.Create("1.0.10-10-1234abcd"));
            AkkaVersion.Create("1.0.0-3-1234abcd+20140707-1030").Should().BeLessThan(AkkaVersion.Create("1.0.0-3-1234abcd+20140707-1130"));

            // not real dynver, but should still work
            AkkaVersion.Create("1.0.10+3a-1234abcd").Should().BeLessThan(AkkaVersion.Create("1.0.10+3b-1234abcd"));
        }

        [Fact]
        public void Version_should_compare_extra_without_digits()
        {
            AkkaVersion.Create("foo").Should().Be(AkkaVersion.Create("foo"));
            AkkaVersion.Create("foo").Should().NotBe(AkkaVersion.Create("bar"));
            AkkaVersion.Create("foo").Should().BeLessThan(AkkaVersion.Create("1.2.3"));
            AkkaVersion.Create("foo").Should().BeGreaterThan(AkkaVersion.Create("bar"));
            AkkaVersion.Create("1-foo").Should().NotBe(AkkaVersion.Create("01-foo"));
            AkkaVersion.Create("1-foo").Should().BeGreaterThan(AkkaVersion.Create("02-foo"));
        }
    }
}

