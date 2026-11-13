// -----------------------------------------------------------------------
// <copyright file="SynchronousSenderScopeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Xunit;

namespace Akka.Hosting.TestKit.Tests;

public sealed class SynchronousSenderScopeSpec : IAsyncLifetime
{
    private ImplicitSenderScope? _scope;

    public Task InitializeAsync()
    {
        _scope = ImplicitSenderScope.Enter(() => ActorRefs.Nobody);
        return Task.CompletedTask;
    }

    [Fact(DisplayName = "Should_resolve_sender_in_xUnit2_body_after_synchronously_completed_initialization")]
    public void Should_ResolveSender_AfterSynchronousInitialization()
    {
        Assert.Same(ActorRefs.Nobody, ActorCell.GetCurrentSelfOrNoSender());
        Assert.Null(InternalCurrentActorCellKeeper.Current);
    }

    public Task DisposeAsync()
    {
        _scope?.Dispose();
        return Task.CompletedTask;
    }
}
