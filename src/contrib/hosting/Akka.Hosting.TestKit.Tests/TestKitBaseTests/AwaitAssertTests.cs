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

    [Fact(DisplayName = "AwaitAssertAsync must not throw when the assertion is valid")]
    public async Task AwaitAssert_must_not_throw_any_exception_when_assertion_is_valid()
    {
        await AwaitAssertAsync(() => Assert.Equal("foo", "foo"));
    }

    [Fact(DisplayName = "AwaitAssertAsync must throw when the assertion never becomes valid")]
    public async Task AwaitAssert_must_throw_exception_when_assertion_is_invalid()
    {
        // AwaitAssertAsync polls for 500ms (dilated to 1s by timefactor=2) before it rethrows,
        // so it can't return sooner than the 300ms lower bound. The upper bound only guards
        // against a hang; it is generous because a cold or starved CI host can stretch a
        // nominal ~1s run well past 2s.
        await WithinAsync(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(10), async () =>
        {
            await Assert.ThrowsAsync<EqualException>(async () =>
                await AwaitAssertAsync(() => Assert.Equal("foo", "bar"), TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(300)));
        });
    }
}