// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

public sealed class SystemMessageMixedBindingSpec : AkkaSpec
{
    public SystemMessageMixedBindingSpec(ITestOutputHelper output) : base(output) { }

    [Theory(DisplayName = "Should_DeliverWatchAndTerminationBothWays_When_SystemMessageWritersDiffer")]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Should_DeliverWatchAndTerminationBothWays_When_SystemMessageWritersDiffer(bool artery, bool modern)
    {
        await using var pair = CreatePair(artery, modern);
        Assert.Equal(modern ? 62 : 22, pair.V2Node.Serialization.FindSerializerFor(new Suspend()).Identifier);
        Assert.Equal(22, pair.LegacyNode.Serialization.FindSerializerFor(new Suspend()).Identifier);
        await VerifyDeathWatch(pair.V2Node, pair.LegacyNode);
        await VerifyDeathWatch(pair.LegacyNode, pair.V2Node);
    }

    [Theory(DisplayName = "Should_RemoveRemoteSelfWatchBothWays_When_SystemMessageWritersDiffer")]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task Should_RemoveRemoteSelfWatchBothWays_When_SystemMessageWritersDiffer(bool artery, bool modern)
    {
        await using var pair = CreatePair(artery, modern);
        Assert.Equal(modern ? 62 : 22, pair.V2Node.Serialization.FindSerializerFor(new Suspend()).Identifier);
        Assert.Equal(22, pair.LegacyNode.Serialization.FindSerializerFor(new Suspend()).Identifier);
        await VerifyUnwatch(pair.V2Node, pair.LegacyNode);
        await VerifyUnwatch(pair.LegacyNode, pair.V2Node);
    }

    private static MixedBindingPair CreatePair(bool artery, bool modern)
    {
        var transport = ConfigurationFactory.ParseString(artery ? """
            akka.actor.provider = remote
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """ : """
            akka.actor.provider = remote
            akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
            akka.remote.dot-netty.tcp.port = 0
            """);
        var enabled = ConfigurationFactory.ParseString($"akka.actor.serialization-v2 = {(modern ? "on" : "off")}").WithFallback(transport);
        var pinned = MixedBindingPair.BindTo("akka-system-msg", typeof(SystemMessage)).WithFallback(enabled);
        return MixedBindingPair.Create(artery ? "system-artery" : "system-classic", enabled, pinned);
    }

    private async Task VerifyDeathWatch(ActorSystem watchingSystem, ActorSystem targetSystem)
    {
        var observer = CreateTestProbe(watchingSystem);
        var inspector = CreateTestProbe(targetSystem);
        var address = RARP.For(targetSystem).Provider.DefaultAddress;
        var watched = targetSystem.ActorOf(Props.Create(() => new WatchStateTarget()));
        var watchedRemote = await watchingSystem.ActorSelection(watched.Path.ToSerializationFormatWithAddress(address))
            .ResolveOne(TimeSpan.FromSeconds(10));
        await observer.WatchAsync(watchedRemote);
        await AssertRemoteWatchCount(watched, inspector, 1);
        targetSystem.Stop(watched);
        var terminated = await observer.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(10));
        Assert.Equal(watchedRemote, terminated.ActorRef);
        Assert.True(terminated.ExistenceConfirmed);
        Assert.False(terminated.AddressTerminated);
        await observer.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

        var missing = ((ExtendedActorSystem)watchingSystem).Provider.ResolveActorRef(
            new RootActorPath(address) / "user" / "missing-target");
        await observer.WatchAsync(missing);
        var absent = await observer.ExpectMsgAsync<Terminated>(TimeSpan.FromSeconds(10));
        Assert.Equal(missing, absent.ActorRef);
        Assert.False(absent.ExistenceConfirmed);
        Assert.False(absent.AddressTerminated);
    }

    private async Task VerifyUnwatch(ActorSystem watchingSystem, ActorSystem targetSystem)
    {
        var observer = CreateTestProbe(watchingSystem);
        var inspector = CreateTestProbe(targetSystem);
        var target = targetSystem.ActorOf(Props.Create(() => new WatchStateTarget()));
        var address = RARP.For(targetSystem).Provider.DefaultAddress;
        var remote = await watchingSystem.ActorSelection(target.Path.ToSerializationFormatWithAddress(address))
            .ResolveOne(TimeSpan.FromSeconds(10));

        await observer.WatchAsync(remote);
        await AssertRemoteWatchCount(target, inspector, 1);
        await observer.UnwatchAsync(remote);
        await AwaitAssertAsync(async () =>
        {
            RARP.For(watchingSystem).Provider.RemoteWatcher.Tell(RemoteWatcher.Stats.Empty, observer.Ref);
            Assert.Equal(RemoteWatcher.Stats.Empty, await observer.ExpectMsgAsync<RemoteWatcher.Stats>(TimeSpan.FromSeconds(1)));
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
        await AssertRemoteWatchCount(target, inspector, 0);
        targetSystem.Stop(target);
        await observer.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

    }

    private async Task AssertRemoteWatchCount(IActorRef target, TestProbe inspector, int expected)
    {
        // Read on the target's actor thread, so stop/unwatch assertions wait for actual remote
        // system-message delivery rather than a fixed sleep or an unrelated user-message echo.
        await AwaitAssertAsync(async () =>
        {
            target.Tell("remote-watch-count", inspector.Ref);
            var watchers = await inspector.ExpectMsgAsync<string[]>(TimeSpan.FromSeconds(1));
            Assert.True(watchers.Length == expected, $"Expected {expected} remote watchers; observed: {string.Join(", ", watchers)}");
        }, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50));
    }

    private sealed class WatchStateTarget : ReceiveActor
    {
        // This bounded JIT test probe reads private cell state only to establish a delivery
        // barrier. Product serialization/native consumers never use reflection on actor cells.
        private static readonly FieldInfo State = typeof(ActorCell).GetField("_state", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Actor cell state field is missing.");

        public WatchStateTarget()
        {
            Receive<string>(message =>
            {
                if (message != "remote-watch-count")
                    throw new ArgumentException($"Unexpected local inspection message [{message}].");
                var state = (IActorState)(State.GetValue(Context) ?? throw new InvalidOperationException("Actor cell state is missing."));
                Sender.Tell(state.GetWatchedBy().Where(actor => actor is IInternalActorRef { IsLocal: false })
                    .Select(actor => actor.Path.ToSerializationFormat()).ToArray());
            });
        }
    }
}
