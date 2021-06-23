//-----------------------------------------------------------------------
// <copyright file="AkkaVersionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
            AppVersion.Create("1.2.3").Should().Be(AppVersion.Create("1.2.3"));
            AppVersion.Create("1.2.3").Should().NotBe(AppVersion.Create("1.2.4"));
            AppVersion.Create("1.2.4").Should().BeGreaterThan(AppVersion.Create("1.2.3"));
            AppVersion.Create("3.2.1").Should().BeGreaterThan(AppVersion.Create("1.2.3"));
            AppVersion.Create("3.2.1").Should().BeLessThan(AppVersion.Create("3.3.1"));
            AppVersion.Create("3.2.0").Should().BeLessThan(AppVersion.Create("3.2.1"));
        }

        [Fact]
        public void Version_should_not_support_more_than_3_digits_version()
        {
            XAssert.Throws<ArgumentOutOfRangeException>(() => AppVersion.Create("1.2.3.1"));
        }

        [Fact]
        public void Version_should_compare_2_digit_version()
        {
            AppVersion.Create("1.2").Should().Be(AppVersion.Create("1.2"));
            AppVersion.Create("1.2").Should().Be(AppVersion.Create("1.2.0"));
            AppVersion.Create("1.2").Should().NotBe(AppVersion.Create("1.3"));
            AppVersion.Create("1.2.1").Should().BeGreaterThan(AppVersion.Create("1.2"));
            AppVersion.Create("2.4").Should().BeGreaterThan(AppVersion.Create("2.3"));
            AppVersion.Create("3.2").Should().BeLessThan(AppVersion.Create("3.2.7"));
        }

        [Fact]
        public void Version_should_compare_single_digit_version()
        {
            AppVersion.Create("1").Should().Be(AppVersion.Create("1"));
            AppVersion.Create("1").Should().Be(AppVersion.Create("1.0"));
            AppVersion.Create("1").Should().Be(AppVersion.Create("1.0.0"));
            AppVersion.Create("1").Should().NotBe(AppVersion.Create("2"));
            AppVersion.Create("3").Should().BeGreaterThan(AppVersion.Create("2"));

            AppVersion.Create("2b").Should().BeGreaterThan(AppVersion.Create("2a"));
            AppVersion.Create("2020-09-07").Should().BeGreaterThan(AppVersion.Create("2020-08-30"));
        }

        [Fact]
        public void Version_should_compare_extra()
        {
            AppVersion.Create("1.2.3-M1").Should().Be(AppVersion.Create("1.2.3-M1"));
            AppVersion.Create("1.2-M1").Should().Be(AppVersion.Create("1.2-M1"));
            AppVersion.Create("1.2.0-M1").Should().Be(AppVersion.Create("1.2-M1"));
            AppVersion.Create("1.2.3-M1").Should().NotBe(AppVersion.Create("1.2.3-M2"));
            AppVersion.Create("1.2-M1").Should().BeLessThan(AppVersion.Create("1.2.0"));
            AppVersion.Create("1.2.0-M1").Should().BeLessThan(AppVersion.Create("1.2.0"));
            AppVersion.Create("1.2.3-M2").Should().BeGreaterThan(AppVersion.Create("1.2.3-M1"));
        }

        [Fact]
        public void Version_should_require_digits()
        {
            XAssert.Throws<FormatException>(() => AppVersion.Create("1.x.3"));
            XAssert.Throws<FormatException>(() => AppVersion.Create("1.2x.3"));
            XAssert.Throws<FormatException>(() => AppVersion.Create("1.2.x"));
            XAssert.Throws<FormatException>(() => AppVersion.Create("1.2.3x"));

            XAssert.Throws<FormatException>(() => AppVersion.Create("x.3"));
            XAssert.Throws<FormatException>(() => AppVersion.Create("1.x"));
            XAssert.Throws<FormatException>(() => AppVersion.Create("1.2x"));
        }

        [Fact]
        public void Version_should_compare_dynver_format()
        {
            // dynver format
            AppVersion.Create("1.0.10+3-1234abcd").Should().BeLessThan(AppVersion.Create("1.0.11"));
            AppVersion.Create("1.0.10+3-1234abcd").Should().BeLessThan(AppVersion.Create("1.0.10+10-1234abcd"));
            AppVersion.Create("1.2+3-1234abcd").Should().BeLessThan(AppVersion.Create("1.2+10-1234abcd"));
            AppVersion.Create("1.0.0+3-1234abcd+20140707-1030").Should().BeLessThan(AppVersion.Create("1.0.0+3-1234abcd+20140707-1130"));
            AppVersion.Create("0.0.0+3-2234abcd").Should().BeLessThan(AppVersion.Create("0.0.0+4-1234abcd"));
            AppVersion.Create("HEAD+20140707-1030").Should().BeLessThan(AppVersion.Create("HEAD+20140707-1130"));

            AppVersion.Create("1.0.10-3-1234abcd").Should().BeLessThan(AppVersion.Create("1.0.10-10-1234abcd"));
            AppVersion.Create("1.0.0-3-1234abcd+20140707-1030").Should().BeLessThan(AppVersion.Create("1.0.0-3-1234abcd+20140707-1130"));

            // not real dynver, but should still work
            AppVersion.Create("1.0.10+3a-1234abcd").Should().BeLessThan(AppVersion.Create("1.0.10+3b-1234abcd"));
        }

        [Fact]
        public void Version_should_compare_extra_without_digits()
        {
            AppVersion.Create("foo").Should().Be(AppVersion.Create("foo"));
            AppVersion.Create("foo").Should().NotBe(AppVersion.Create("bar"));
            AppVersion.Create("foo").Should().BeLessThan(AppVersion.Create("1.2.3"));
            AppVersion.Create("foo").Should().BeGreaterThan(AppVersion.Create("bar"));
            AppVersion.Create("1-foo").Should().NotBe(AppVersion.Create("01-foo"));
            AppVersion.Create("1-foo").Should().BeGreaterThan(AppVersion.Create("02-foo"));
        }
    }
}

