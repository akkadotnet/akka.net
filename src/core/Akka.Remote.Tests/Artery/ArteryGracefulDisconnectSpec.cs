//-----------------------------------------------------------------------
// <copyright file="ArteryGracefulDisconnectSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryGracefulDisconnectSpec : AkkaSpec
    {
        private static readonly Config SpecConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-restart-backoff = 200ms
            akka.remote.artery.advanced.control-heartbeat-interval = 200ms
            akka.remote.watch-failure-detector.heartbeat-interval = 100ms
            akka.remote.watch-failure-detector.acceptable-heartbeat-pause = 1s
            akka.remote.watch-failure-detector.unreachable-nodes-reaper-interval = 100ms
            akka.remote.watch-failure-detector.expected-response-after = 200ms
            """);

        public ArteryGracefulDisconnectSpec(ITestOutputHelper output) : base(SpecConfig, output)
        {
        }

        private sealed class ChatRoom : ReceiveActor
        {
            public ChatRoom()
            {
                Receive<string>(message =>
                {
                    switch (message)
                    {
                        case "join":
                            Context.Watch(Sender);
                            Sender.Tell("joined");
                            break;
                        case "leave":
                            Context.Unwatch(Sender);
                            Sender.Tell("left");
                            break;
                        case "ping":
                            Sender.Tell("pong");
                            break;
                    }
                });
                Receive<Terminated>(_ => { });
            }
        }

        [Fact(DisplayName = "An acknowledged graceful peer disconnect leaves idle Artery streams dormant instead of reconnecting forever")]
        public async Task Should_Stop_Idle_Reconnects_When_Unwatched_Peer_Shuts_Down_After_Leave_Acknowledgement()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var peerConfig = ConfigurationFactory.ParseString("akka.actor.debug.lifecycle = on")
                .WithFallback(SpecConfig);
            var peer = ActorSystem.Create("GracefullyLeavingPeer", peerConfig);
            try
            {
                var room = Sys.ActorOf(Props.Create(() => new ChatRoom()), "chat");
                var peerProbe = CreateTestProbe(peer);
                var lifecycleProbe = CreateTestProbe(peer);
                peer.EventStream.Subscribe(lifecycleProbe.Ref, typeof(Debug));
                var serverAddress = RARP.For(Sys).Provider.DefaultAddress;
                var remoteRoom = await peer.ActorSelection($"{serverAddress}/user/chat")
                    .ResolveOne(10.Seconds(), cancellation);
                var remoteWatcher = RARP.For(Sys).Provider.RemoteWatcher;
                var statsProbe = CreateTestProbe();

                remoteRoom.Tell("join", peerProbe.Ref);
                await peerProbe.ExpectMsgAsync("joined", 10.Seconds(), cancellationToken: cancellation);
                await AwaitAssertAsync(async () =>
                {
                    remoteWatcher.Tell(new RemoteWatcher.Stats(0, 0), statsProbe.Ref);
                    var stats = await statsProbe.ExpectMsgAsync<RemoteWatcher.Stats>(3.Seconds(), cancellationToken: cancellation);
                    stats.WatchingRefs.Count.Should().Be(1);
                }, 10.Seconds(), cancellationToken: cancellation);

                // Match the chat application: explicitly unwatch, receive the successful
                // application acknowledgement, and only then terminate the client system.
                remoteRoom.Tell("leave", peerProbe.Ref);
                await peerProbe.ExpectMsgAsync("left", 10.Seconds(), cancellationToken: cancellation);
                await AwaitAssertAsync(async () =>
                {
                    remoteWatcher.Tell(new RemoteWatcher.Stats(0, 0), statsProbe.Ref);
                    var stats = await statsProbe.ExpectMsgAsync<RemoteWatcher.Stats>(3.Seconds(), cancellationToken: cancellation);
                    stats.WatchingRefs.Count.Should().Be(0);
                }, 10.Seconds(), cancellationToken: cancellation);

                // The peer actor's lifecycle event proves this exact Unwatch was processed.
                // An Ack sequence threshold could also be satisfied by RemoteWatcher's ReWatch.
                await lifecycleProbe.FishForMessageAsync(message =>
                    message is Debug lifecycle && lifecycle.LogSource == peerProbe.Ref.Path.ToString()
                    && lifecycle.Message is string text && text.StartsWith("no longer watched by ", StringComparison.Ordinal),
                    10.Seconds(), cancellationToken: cancellation);
                var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
                var association = transport.Registry.AssociationFor(RARP.For(peer).Provider.DefaultAddress);
                await AwaitAssertAsync(() =>
                {
                    association.SystemMessageDeliveryState.Buffer.Count.Should().Be(0);
                    association.ControlQueueCount.Should().Be(0);
                }, 10.Seconds(), cancellationToken: cancellation);

                await peer.Terminate().AwaitWithTimeout(10.Seconds());

                await AwaitAssertAsync(() =>
                {
                    association.SystemMessageDeliveryState.Buffer.Count.Should().Be(0);
                    association.ControlQueueCount.Should().Be(0);
                    association.IsControlOutboundMaterialized.Should().BeFalse("there is no remaining control work for the disconnected peer");
                    association.IsOutboundMaterialized.Should().BeFalse("the disconnected peer has no remaining ordinary traffic");
                }, 10.Seconds(), cancellationToken: cancellation);

                // Observe several restart-backoff windows. A momentarily released gate is
                // insufficient: housekeeping must not start another idle reconnect loop.
                for (var sample = 0; sample < 12; sample++)
                {
                    await Task.Delay(200.Milliseconds(), cancellation);
                    association.IsControlOutboundMaterialized.Should().BeFalse();
                    association.IsOutboundMaterialized.Should().BeFalse();
                }

                Sys.WhenTerminated.IsCompleted.Should().BeFalse();
                room.Tell("ping", TestActor);
                await ExpectMsgAsync("pong", 3.Seconds(), cancellationToken: cancellation);
            }
            finally
            {
                await peer.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
