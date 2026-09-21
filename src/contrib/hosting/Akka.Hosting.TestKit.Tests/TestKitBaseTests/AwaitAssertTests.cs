//-----------------------------------------------------------------------
// <copyright file="AwaitAssertTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Xunit;
using Xunit.Sdk;

namespace Akka.Hosting.TestKit.Tests.TestKitBaseTests;

public class AwaitAssertTests : TestKit
{
    protected override Config Config { get; } = "akka.test.timefactor=2";

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
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