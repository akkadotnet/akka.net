//-----------------------------------------------------------------------
// <copyright file="ArteryIdleAssociationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Reflection;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryIdleAssociationSpec : AkkaSpec
    {
        private static Config ConfigFor(int port = 0) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            akka.remote.artery.advanced.outbound-restart-backoff = 200ms
            akka.remote.artery.advanced.control-heartbeat-interval = 200ms
            akka.remote.artery.advanced.handshake-retry-interval = 100ms
            """);

        private static readonly MethodInfo ScheduleRestart = typeof(ArteryRemoting).GetMethod(
            "ScheduleOutboundRestart", BindingFlags.NonPublic | BindingFlags.Instance)!;

        public ArteryIdleAssociationSpec(ITestOutputHelper output) : base(ConfigFor(), output)
        {
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(message => Sender.Tell(message));
        }

        [Fact(DisplayName = "8857: a fresh ordinary handshake queued during control backoff eventually wakes the quiescent control stream")]
        public async Task Should_Complete_Handshake_When_Fresh_Request_Arrives_During_Control_Backoff()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var peer = ActorSystem.Create("IdleHandshakePeer", ConfigFor());
            try
            {
                peer.ActorOf(Props.Create(() => new Echo()), "echo");
                var address = RARP.For(peer).Provider.DefaultAddress;
                var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
                var association = transport.Registry.AssociationFor(address);
                // Hold the gate as a just-failed control materialization would. Its backoff
                // callback may discard the first request; the handshake retry must wake it again.
                association.EnsureControlOutboundMaterialized(_ => { });
                ScheduleRestart.Invoke(transport, new object[] { address, association, ArteryStreamId.Control });
                Sys.ActorSelection($"{address}/user/echo").Tell("fresh-demand", TestActor);
                await ExpectMsgAsync("fresh-demand", TimeSpan.FromSeconds(10), cancellationToken: cancellation);
                association.CurrentState.UniqueRemoteAddress.Should().NotBeNull();
            }
            finally
            {
                await peer.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "8857: a handshake-held message returned after idle backoff still wakes a consumer")]
        public async Task Should_Wake_On_A_Late_Returned_Handshake_Message()
        {
            var cancellation = TestContext.Current.CancellationToken;
            var peer = ActorSystem.Create("LateReturnedMessagePeer", ConfigFor());
            try
            {
                peer.ActorOf(Props.Create(() => new Echo()), "echo");
                var address = RARP.For(peer).Provider.DefaultAddress;
                var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
                var association = transport.Registry.AssociationFor(address);
                association.EnsureOutboundMaterialized(_ => { });
                ScheduleRestart.Invoke(transport, new object[] { address, association, ArteryStreamId.Ordinary });
                await AwaitAssertAsync(() => association.IsOutboundMaterialized.Should().BeFalse(),
                    TimeSpan.FromSeconds(5), cancellationToken: cancellation);

                // TerminationWatcher can complete before upstream PostStop returns its held
                // envelope. The backoff may have already made the association dormant by then.
                var returned = new OutboundEnvelope("late-return",
                    TestActor.Path.ToStringWithAddress(RARP.For(Sys).Provider.DefaultAddress), $"{address}/user/echo");
                var returnMethod = typeof(ArteryRemoting).GetMethod("ReturnUndeliveredOutboundElement",
                    BindingFlags.NonPublic | BindingFlags.Instance)!;
                returnMethod.Invoke(transport, new object[] { address, association, ArteryStreamId.Ordinary, returned, 0 });
                await ExpectMsgAsync("late-return", TimeSpan.FromSeconds(5), cancellationToken: cancellation);
            }
            finally
            {
                await peer.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "8857: new traffic wakes an idle association and reaches a replacement actor system with a fresh UID")]
        public async Task Should_Reconnect_On_Demand_After_Idle_Peer_Is_Replaced()
        {
            var cancellation = TestContext.Current.CancellationToken;
            const string peerName = "IdleReplacementPeer";
            var peer = ActorSystem.Create(peerName, ConfigFor());
            ActorSystem? replacement = null;
            try
            {
                peer.ActorOf(Props.Create(() => new Echo()), "echo");
                var address = RARP.For(peer).Provider.DefaultAddress;
                var oldUid = AddressUidExtension.Uid(peer);
                var selection = Sys.ActorSelection($"{address}/user/echo");
                selection.Tell("before-idle", TestActor);
                await ExpectMsgAsync("before-idle", TimeSpan.FromSeconds(10), cancellationToken: cancellation);
                var transport = (ArteryRemoting)RARP.For(Sys).Provider.Transport;
                var association = transport.Registry.AssociationFor(address);
                await peer.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
                await AwaitAssertAsync(() =>
                {
                    association.IsControlOutboundMaterialized.Should().BeFalse();
                    association.IsOutboundMaterialized.Should().BeFalse();
                }, TimeSpan.FromSeconds(10), cancellationToken: cancellation);

                // This test owns the port throughout; give the OS time to release the listener.
                for (var attempt = 0; replacement is null; attempt++)
                {
                    try
                    {
                        replacement = ActorSystem.Create(peerName, ConfigFor(address.Port!.Value));
                    }
                    catch (Exception) when (attempt < 20)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(100), cancellation);
                    }
                }
                replacement.ActorOf(Props.Create(() => new Echo()), "echo");
                var newUid = AddressUidExtension.Uid(replacement);
                newUid.Should().NotBe(oldUid);
                await AwaitAssertAsync(async () =>
                {
                    selection.Tell("after-idle", TestActor);
                    await ExpectMsgAsync("after-idle", TimeSpan.FromSeconds(2), cancellationToken: cancellation);
                }, TimeSpan.FromSeconds(10), cancellationToken: cancellation);
                association.CurrentState.UniqueRemoteAddress!.Value.Uid.Should().Be(newUid);
                association.IsControlShutDown.Should().BeFalse("quiescence must not permanently close the channel");
            }
            finally
            {
                await peer.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
                if (replacement is not null)
                    await replacement.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
            }
        }
    }
}
