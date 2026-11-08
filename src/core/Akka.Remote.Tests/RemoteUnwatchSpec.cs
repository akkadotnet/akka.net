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
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Serialization;

public sealed class RemoteUnwatchSpec : AkkaSpec
{
    public RemoteUnwatchSpec(ITestOutputHelper output) : base(output) { }

    [Theory(DisplayName = "Should_RemoveRemoteSelfWatch_When_TheLastLocalWatcherUnwatches")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RemoveRemoteSelfWatch_When_TheLastLocalWatcherUnwatches(bool artery)
    {
        // Arrange
        var config = ConfigurationFactory.ParseString($$"""
            akka.actor.provider = remote
            akka.actor.serialization-v2 = off
            akka.remote.artery.enabled = {{(artery ? "on" : "off")}}
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
            akka.remote.dot-netty.tcp.port = 0
            """).WithFallback(AkkaSpecConfig);
        var watchingSystem = ActorSystem.Create("watching", config);
        ActorSystem? targetSystem = null;
        try
        {
            targetSystem = ActorSystem.Create("target", config);
            var observer = CreateTestProbe(watchingSystem);
            var inspector = CreateTestProbe(targetSystem);
            var target = targetSystem.ActorOf(Props.Create(() => new WatchStateTarget()));
            var address = RARP.For(targetSystem).Provider.DefaultAddress;
            var remote = await watchingSystem.ActorSelection(target.Path.ToSerializationFormatWithAddress(address))
                .ResolveOne(TimeSpan.FromSeconds(10));
            await observer.WatchAsync(remote);
            await AssertRemoteWatchCount(target, inspector, 1);

            // Act
            await observer.UnwatchAsync(remote);

            // Assert
            await AssertRemoteWatchCount(target, inspector, 0);
        }
        finally
        {
            await ShutdownAsync(watchingSystem);
            if (targetSystem is not null)
                await ShutdownAsync(targetSystem);
        }
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
