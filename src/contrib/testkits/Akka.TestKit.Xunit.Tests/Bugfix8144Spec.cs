// -----------------------------------------------------------------------
//  <copyright file="Bugfix8144Spec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit.TestActors;
using Xunit;

namespace Akka.TestKit.Tests;

/// <summary>
/// Verifies that implicit sender (TestActor) is preserved across async boundaries.
/// Regression test for https://github.com/akkadotnet/akka.net/issues/8144
/// </summary>
public class Bugfix8144Spec: Xunit.TestKit, IAsyncLifetime
{
    public ValueTask DisposeAsync()
    {
        return new ValueTask(Task.CompletedTask);
    }

    public async ValueTask InitializeAsync()
    {
        // Any await here can cause a thread switch, which previously
        // lost the [ThreadStatic] InternalCurrentActorCellKeeper.Current
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Should_use_implicit_TestActor_sender_after_async_initialization()
    {
        // SimpleEchoActor echoes back to Sender - tests that implicit sender works
        var actor = Sys.ActorOf(SimpleEchoActor.Props());

        // This uses ActorRefImplicitSenderExtensions.Tell which resolves
        // the sender via ActorCell.GetCurrentSelfOrNoSender()
        actor.Tell("hello");

        // Should receive the echo back at TestActor
        ExpectMsg("hello");
    }
}
