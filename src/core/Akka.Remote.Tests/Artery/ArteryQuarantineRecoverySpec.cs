//-----------------------------------------------------------------------
// <copyright file="ArteryQuarantineRecoverySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    public sealed class ArteryQuarantineRecoverySpec : AkkaSpec
    {
        public ArteryQuarantineRecoverySpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ConfigFor(int port = 0) => ConfigurationFactory.ParseString($$"""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = {{port}}
            akka.remote.artery.advanced.outbound-restart-backoff = 200ms
            akka.remote.artery.advanced.control-heartbeat-interval = 30s
            akka.remote.artery.advanced.handshake-retry-interval = 200ms
            akka.remote.artery.advanced.handshake-timeout = 30s
            """);

        private static ArteryRemoting TransportFor(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;
        private static Address AddressFor(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;
        private static string EchoPath(ActorSystem system) => $"{AddressFor(system)}/user/echo";

        private static readonly MethodInfo EnqueueControlMethod = typeof(ArteryRemoting)
            .GetMethod("EnqueueControl", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("ArteryRemoting.EnqueueControl was not found.");

        private static void SendHeartbeat(ArteryRemoting transport, Address address) =>
            EnqueueControlMethod.Invoke(transport, new object[] { address, new ArteryHeartbeat() });

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(message => Sender.Tell(message));
        }

        private sealed class HeartbeatSubscriber(IActorRef probe) : IControlMessageSubscriber
        {
            public void ControlMessageReceived(long originUid, object message)
            {
                if (message is ArteryHeartbeat) probe.Tell(message);
            }
        }

        // The existing encode-pool seam lets this test stop one control encode before
        // queued messages are read. Releasing it throws once, causing a real stream
        // failure and exercising the transport's own cleanup and reconnection paths.
        private sealed class FailOncePool : ArrayPool<byte>, IDisposable
        {
            private readonly ArrayPool<byte> _inner = Create();
            private readonly ManualResetEventSlim _release = new(false);
            private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private int _armed;

            public Task Entered => _entered.Task;
            public void Arm() => Interlocked.Exchange(ref _armed, 1);
            public void FailBlockedEncode() => _release.Set();

            public override byte[] Rent(int minimumLength)
            {
                if (Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    _entered.TrySetResult(true);
                    if (!_release.Wait(30.Seconds()))
                        throw new TimeoutException("The test did not release its blocked control encode.");
                    throw new IOException("Injected transient control-encode failure.");
                }
                return _inner.Rent(minimumLength);
            }

            public override void Return(byte[] array, bool clearArray = false) => _inner.Return(array, clearArray);
            public void Dispose() => _release.Dispose();
        }

        [Theory(DisplayName = "Accepted quarantine notifications survive a transient control failure but never quarantine a replacement peer UID")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_Retain_Or_Retire_Notification_For_Its_Target_Incarnation(bool replacePeer)
        {
            var cancellation = TestContext.Current.CancellationToken;
            using var pool = new FailOncePool();
            var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ConfigFor()), new ArteryTransportSetup(pool));
            var sender = ActorSystem.Create("QuarantineRecoverySender", setup);
            ActorSystem? peer = ActorSystem.Create("QuarantineRecoveryPeer", ConfigFor());
            ActorSystem? replacement = null;
            try
            {
                sender.ActorOf(Props.Create(() => new Echo()), "echo");
                peer.ActorOf(Props.Create(() => new Echo()), "echo");
                var probe = CreateTestProbe(sender);
                var remoteEcho = await sender.ActorSelection(EchoPath(peer)).ResolveOne(10.Seconds(), cancellation);
                remoteEcho.Tell("warmup", probe.Ref);
                await probe.ExpectMsgAsync("warmup", 10.Seconds(), cancellationToken: cancellation);

                var transport = TransportFor(sender);
                var peerAddress = AddressFor(peer);
                var peerUid = AddressUidExtension.Uid(peer);
                var association = transport.Registry.AssociationFor(peerAddress);
                await AwaitAssertAsync(() => association.ControlQueueCount.Should().Be(0),
                    10.Seconds(), cancellationToken: cancellation);
                var events = CreateTestProbe(peer);
                peer.EventStream.Subscribe(events.Ref, typeof(ThisActorSystemQuarantinedEvent));

                // The real warmup reply proves ordinary encoding finished. Until this
                // barrier opens, only the explicitly injected control heartbeat is sent.
                pool.Arm();
                SendHeartbeat(transport, peerAddress);
                await pool.Entered.WaitAsync(10.Seconds(), cancellation);
                transport.Quarantine(peerAddress, peerUid);
                association.ControlReader.TryPeek(out var queued).Should().BeTrue();
                queued!.Message.Should().BeOfType<ArteryQuarantined>()
                    .Which.QuarantinedUid.Should().Be(peerUid);
                association.ControlQueueCount.Should().BeGreaterOrEqualTo(1,
                    "the accepted notification is queued behind the blocked encode");

                Task<IActorRef>? incomingConnection = null;
                if (replacePeer)
                {
                    await peer.Terminate().AwaitWithTimeout(10.Seconds());
                    peer = null;
                    replacement = ActorSystem.Create("QuarantineRecoveryPeer", ConfigFor(peerAddress.Port!.Value));
                    replacement.ActorOf(Props.Create(() => new Echo()), "echo");
                    var replacementUid = AddressUidExtension.Uid(replacement);
                    replacementUid.Should().NotBe(peerUid);
                    events = CreateTestProbe(replacement);
                    replacement.EventStream.Subscribe(events.Ref, typeof(ThisActorSystemQuarantinedEvent));

                    // The new peer's inbound HandshakeReq updates the sender's known UID
                    // before its reply can pass the still-blocked outbound control graph.
                    incomingConnection = replacement.ActorSelection(EchoPath(sender)).ResolveOne(15.Seconds(), cancellation);
                    await AwaitAssertAsync(() => association.CurrentState.UniqueRemoteAddress!.Value.Uid.Should().Be(replacementUid),
                        10.Seconds(), cancellationToken: cancellation);
                }

                pool.FailBlockedEncode();
                if (!replacePeer)
                {
                    var notification = await events.ExpectMsgAsync<ThisActorSystemQuarantinedEvent>(15.Seconds(), cancellationToken: cancellation);
                    notification.RemoteAddress.Should().Be(AddressFor(sender));
                }
                else
                {
                    var replacementProbe = CreateTestProbe(replacement!);
                    var senderEcho = await incomingConnection!;
                    senderEcho.Tell("replacement-to-sender", replacementProbe.Ref);
                    await replacementProbe.ExpectMsgAsync("replacement-to-sender", 10.Seconds(), cancellationToken: cancellation);
                    var replacementEcho = await sender.ActorSelection(EchoPath(replacement!)).ResolveOne(10.Seconds(), cancellation);
                    replacementEcho.Tell("sender-to-replacement", probe.Ref);
                    await probe.ExpectMsgAsync("sender-to-replacement", 10.Seconds(), cancellationToken: cancellation);

                    // A fresh control heartbeat proves recovery and orders observation
                    // after the old queued notice: this negative assertion cannot pass
                    // merely because the transport never reconnected.
                    var heartbeat = CreateTestProbe(replacement!);
                    TransportFor(replacement!).SubscribeControl(new HeartbeatSubscriber(heartbeat.Ref));
                    SendHeartbeat(transport, peerAddress);
                    await heartbeat.ExpectMsgAsync<ArteryHeartbeat>(10.Seconds(), cancellationToken: cancellation);
                    events.Ref.Tell("control-drained");
                    await events.ExpectMsgAsync("control-drained", 3.Seconds(), cancellationToken: cancellation);
                    await AwaitAssertAsync(() => association.ControlQueueCount.Should().Be(0),
                        10.Seconds(), cancellationToken: cancellation);
                    association.IsQuarantined(AddressUidExtension.Uid(replacement!)).Should().BeFalse();
                }
            }
            finally
            {
                pool.FailBlockedEncode();
                await sender.Terminate().AwaitWithTimeout(10.Seconds());
                if (peer is not null) await peer.Terminate().AwaitWithTimeout(10.Seconds());
                if (replacement is not null) await replacement.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
