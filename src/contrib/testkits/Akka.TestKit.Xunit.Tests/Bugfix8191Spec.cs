// -----------------------------------------------------------------------
//  <copyright file="Bugfix8191Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Xunit;

namespace Akka.TestKit.Xunit.Tests;

/// <summary>
/// Regression tests for https://github.com/akkadotnet/akka.net/issues/8191
///
/// xUnit v3 tears a test class instance down via <see cref="IAsyncDisposable.DisposeAsync"/>
/// in preference to <see cref="IDisposable.Dispose"/> whenever the type implements both.
/// <see cref="TestKit"/> must therefore drive its dispose chain — and shut the
/// <c>ActorSystem</c> down — from <c>DisposeAsync</c>, and a derived <c>DisposeAsync</c>
/// override must be able to chain to <c>base.DisposeAsync()</c>. Before the fix the
/// <c>ActorSystem</c> was silently leaked once a derived spec implemented
/// <c>IAsyncLifetime</c>.
/// </summary>
public class Bugfix8191Spec
{
    private sealed class TrackingTestKit : TestKit
    {
        public bool AfterAllRan { get; private set; }

        protected override void AfterAll()
        {
            AfterAllRan = true;
            base.AfterAll();
        }
    }

    private sealed class AsyncTeardownTestKit : TestKit
    {
        public bool DisposeAsyncOverrideRan { get; private set; }

        public override async ValueTask DisposeAsync()
        {
            DisposeAsyncOverrideRan = true;
            await base.DisposeAsync();
        }
    }

    [Fact(DisplayName = "TestKit.DisposeAsync should run the dispose chain and shut down the ActorSystem")]
    public async Task Should_run_dispose_chain_and_shut_down_system_When_disposed_via_DisposeAsync()
    {
        var testKit = new TrackingTestKit();
        var system = testKit.Sys;

        // Exercise the exact path xUnit v3 uses to tear down a test class instance.
        await ((IAsyncDisposable)testKit).DisposeAsync();

        Assert.True(testKit.AfterAllRan, "AfterAll() should run as part of the DisposeAsync chain");
        Assert.True(system.WhenTerminated.IsCompleted, "the ActorSystem should be shut down");
    }

    [Fact(DisplayName = "A derived DisposeAsync override chaining to base should shut down the ActorSystem")]
    public async Task Should_shut_down_system_When_derived_DisposeAsync_chains_to_base()
    {
        var testKit = new AsyncTeardownTestKit();
        var system = testKit.Sys;

        await ((IAsyncDisposable)testKit).DisposeAsync();

        Assert.True(testKit.DisposeAsyncOverrideRan, "the derived DisposeAsync override should run");
        Assert.True(system.WhenTerminated.IsCompleted, "base.DisposeAsync() should shut the ActorSystem down");
    }
}
