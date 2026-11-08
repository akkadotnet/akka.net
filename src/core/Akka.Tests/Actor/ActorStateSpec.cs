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
        var watcher = Reference("watcher", 111);
        Receive behavior = _ => true;
        var state = new DefaultActorState().Become(behavior).AddWatchedBy(watcher);
        Assert.IsType<DefaultActorState>(state);
        Assert.True(state.ContainsWatchedBy(watcher));
        Assert.Same(state, state.RemoveWatchedBy(watcher));
        Assert.IsType<DefaultActorState>(state);
        Assert.False(state.ContainsWatchedBy(watcher));
        Assert.Empty(state.GetWatchedBy());
        Assert.Same(behavior, state.GetCurrentBehavior());
        var replacement = Reference("replacement", 333);
        Assert.Same(state, state.AddWatchedBy(replacement));
        Assert.True(state.ContainsWatchedBy(replacement));
        Assert.False(state.ContainsWatchedBy(watcher));
    }

    [Fact(DisplayName = "Should_RemoveEquivalentWatcher_When_ItsPathAndUidMatch")]
    public void Should_RemoveEquivalentWatcher_When_ItsPathAndUidMatch()
    {
        var watcher = Reference("watcher", 111);
        var equivalent = Reference("watcher", 111);
        Assert.NotSame(watcher, equivalent);
        Assert.Equal(111, watcher.Path.Uid);
        Assert.Equal(watcher, equivalent);
        var state = new DefaultActorState().AddWatchedBy(watcher);
        Assert.Same(state, state.RemoveWatchedBy(equivalent));
        Assert.Empty(state.GetWatchedBy());
    }

    [Theory(DisplayName = "Should_KeepTheSoleWatcher_When_UnwatchNamesAnotherActorOrIncarnation")]
    [InlineData("other", 111)]
    [InlineData("watcher", 222)]
    public void Should_KeepTheSoleWatcher_When_UnwatchNamesAnotherActorOrIncarnation(string name, int uid)
    {
        var watcher = Reference("watcher", 111);
        var other = Reference(name, uid);
        Assert.NotEqual(watcher, other);
        var state = new DefaultActorState().AddWatchedBy(watcher);
        Assert.Same(state, state.RemoveWatchedBy(other));
        Assert.True(state.ContainsWatchedBy(watcher));
        Assert.False(state.ContainsWatchedBy(other));
        Assert.Equal(watcher, Assert.Single(state.GetWatchedBy()));
    }

    [Fact(DisplayName = "Should_KeepEmptyDefaultState_When_UnwatchHasNoRegisteredWatcher")]
    public void Should_KeepEmptyDefaultState_When_UnwatchHasNoRegisteredWatcher()
    {
        var state = new DefaultActorState();
        var watcher = Reference("watcher", 111);
        Assert.Same(state, state.RemoveWatchedBy(watcher));
        Assert.False(state.ContainsWatchedBy(watcher));
        Assert.Empty(state.GetWatchedBy());
    }
}
