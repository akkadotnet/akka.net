//-----------------------------------------------------------------------
// <copyright file="AwaitAssertTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Xunit;
using Xunit.Sdk;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class AwaitAssertTests : Akka.TestKit.Xunit.TestKit
    {
        public AwaitAssertTests() : base("akka.test.timefactor=2")
        {
        }

        [Fact]
        public void AwaitAssert_must_not_throw_any_exception_when_assertion_is_valid()
        {
            AwaitAssert(() => Assert.Equal("foo", "foo"));
        }

        [Fact]
        public void AwaitAssert_must_throw_exception_when_assertion_is_invalid()
        {
            Within(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), () =>
            {
                Assert.Throws<EqualException>(() =>
                    AwaitAssert(() => Assert.Equal("foo", "bar"), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300)));
            });
        }
    }
}
