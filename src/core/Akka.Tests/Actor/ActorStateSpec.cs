// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor;

public sealed class ActorStateSpec : AkkaSpec
{
    public ActorStateSpec(ITestOutputHelper output) : base(output) { }

    private IActorRef Reference(string name, int uid) => new EmptyLocalActorRef(((ExtendedActorSystem)Sys).Provider,
        ActorPath.Parse($"akka://{Sys.Name}/user/{name}#{uid}"), Sys.EventStream);

    [Fact(DisplayName = "Should_RemoveTheLastWatcher_When_DefaultStateReceivesTheMatchingUnwatch")]
    public void Should_RemoveTheLastWatcher_When_DefaultStateReceivesTheMatchingUnwatch()
    {
        // Arrange
        var watcher = Reference("watcher", 111);
        Receive behavior = _ => true;
        var state = new DefaultActorState().Become(behavior).AddWatchedBy(watcher);

        // Act
        var updated = state.RemoveWatchedBy(watcher);

        // Assert
        Assert.Empty(updated.GetWatchedBy());
        Assert.Same(behavior, updated.GetCurrentBehavior());
    }

    [Fact(DisplayName = "Should_RemoveEquivalentWatcher_When_ItsPathAndUidMatch")]
    public void Should_RemoveEquivalentWatcher_When_ItsPathAndUidMatch()
    {
        // Arrange
        var watcher = Reference("watcher", 111);
        var equivalent = Reference("watcher", 111);
        var state = new DefaultActorState().AddWatchedBy(watcher);

        // Act
        var updated = state.RemoveWatchedBy(equivalent);

        // Assert
        Assert.NotSame(watcher, equivalent);
        Assert.Empty(updated.GetWatchedBy());
    }

    [Theory(DisplayName = "Should_KeepTheSoleWatcher_When_UnwatchNamesAnotherActorOrIncarnation")]
    [InlineData("other", 111)]
    [InlineData("watcher", 222)]
    public void Should_KeepTheSoleWatcher_When_UnwatchNamesAnotherActorOrIncarnation(string name, int uid)
    {
        // Arrange
        var watcher = Reference("watcher", 111);
        var other = Reference(name, uid);
        var state = new DefaultActorState().AddWatchedBy(watcher);

        // Act
        var updated = state.RemoveWatchedBy(other);

        // Assert
        Assert.Equal(watcher, Assert.Single(updated.GetWatchedBy()));
    }

    [Fact(DisplayName = "Should_KeepEmptyDefaultState_When_UnwatchHasNoRegisteredWatcher")]
    public void Should_KeepEmptyDefaultState_When_UnwatchHasNoRegisteredWatcher()
    {
        // Arrange
        var state = new DefaultActorState();
        var watcher = Reference("watcher", 111);

        // Act
        var updated = state.RemoveWatchedBy(watcher);

        // Assert
        Assert.Empty(updated.GetWatchedBy());
    }
}
