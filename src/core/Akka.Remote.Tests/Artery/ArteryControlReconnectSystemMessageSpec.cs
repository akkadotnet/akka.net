//-----------------------------------------------------------------------
// <copyright file="ArteryControlReconnectSystemMessageSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Concurrent;
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
// Akka.Remote.SysAck/SysNack (classic) would shadow the Artery types -- see SystemMessageAckerStageSpec.
using SysAck = Akka.Remote.Artery.Ack;
using SysNack = Akka.Remote.Artery.Nack;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Two live systems (same uids throughout) whose control connection is aborted and re-established:
    /// the sender's system-message sequence continues, so the receiver's expected sequence must too.
    /// </summary>
    public class ArteryControlReconnectSystemMessageSpec : AkkaSpec
    {
        public ArteryControlReconnectSystemMessageSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.outbound-restart-backoff = 300ms
            akka.remote.artery.advanced.control-heartbeat-interval = 500ms
            """);

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(m => Sender.Tell(m));
        }

        /// <summary>One system: captures its control-connection abort switch and counts the Acks/Nacks it sends.</summary>
        private sealed class Node
        {
            private Action? _abort;
            private int _connections;
            private long _nacks;
            private long _acks;

            public volatile bool DropAcks;
            public ActorSystem System { get; private set; } = null!;
            public int Connections => Volatile.Read(ref _connections);
            public long Nacks => Interlocked.Read(ref _nacks);
            public long Acks => Interlocked.Read(ref _acks);
            public Address Address => RARP.For(System).Provider.DefaultAddress;
            public Association AssociationTo(Node other) => ((ArteryRemoting)RARP.For(System).Provider.Transport).Registry.AssociationFor(other.Address);

            public static Node Create(string name)
            {
                var node = new Node();
                var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(ArteryConfig()))
                    .WithSetup(new ArteryTransportSetup(
                        dropOutboundControlMessage: node.OnControl,
                        onOutboundControlStarted: (_, abort) =>
                        {
                            Volatile.Write(ref node._abort, abort);
                            Interlocked.Increment(ref node._connections);
                        }));
                node.System = ActorSystem.Create(name, setup);
                return node;
            }

            private bool OnControl(object message)
            {
                switch (message)
                {
                    case SysAck:
                        Interlocked.Increment(ref _acks);
                        return DropAcks;
                    case SysNack:
                        Interlocked.Increment(ref _nacks);
                        break;
                }

                return false;
            }

            public void AbortControlConnection() => Volatile.Read(ref _abort)!.Invoke();
        }

        /// <summary>
        /// Aborts the control connection and waits for a new connection attempt. The hook fires when the attempt is
        /// materialized, not when it is established, so a following drop may hit an attempt that is still connecting.
        /// </summary>
        private async Task DropControlConnectionAsync(Node node)
        {
            var before = node.Connections;
            node.AbortControlConnection();
            await AwaitAssertAsync(() => node.Connections.Should().BeGreaterThan(before, "the control stream must reconnect"), 10.Seconds());
        }

        private async Task<(IActorRef Target, TestProbe Probe)> WatchAsync(Node watcher, Node host, string name)
        {
            var target = host.System.ActorOf(Props.Create(() => new Echo()), name);
            // The Identify can be lost on an ordinary stream that is reconnecting right after a drop; retry it.
            IActorRef remote = null!;
            await AwaitAssertAsync(async () =>
                remote = await watcher.System.ActorSelection($"{host.Address}/user/{name}").ResolveOne(2.Seconds()),
                20.Seconds(), 250.Milliseconds());
            var probe = CreateTestProbe(watcher.System);
            await probe.WatchAsync(remote);
            return (target, probe);
        }

        private async Task ExpectRealTerminatedAsync(Node watcher, Node host, string name)
        {
            var (target, probe) = await WatchAsync(watcher, host, name);
            target.Tell(PoisonPill.Instance);
            var terminated = await probe.ExpectMsgAsync<Terminated>(5.Seconds());
            terminated.AddressTerminated.Should().BeFalse("the Watch must be delivered, not synthesised by quarantine");
        }

        private async Task AwaitDrainedAsync(Node sender, Node receiver) =>
            await AwaitAssertAsync(() => sender.AssociationTo(receiver).SystemMessageDeliveryState.Buffer.Count.Should().Be(0), 10.Seconds());

        [Theory(DisplayName = "Should_DeliverTerminated_When_ControlConnectionIsDropped")]
        [InlineData(1)]
        [InlineData(2)]
        public async Task Should_DeliverTerminated_When_ControlConnectionIsDropped(int drops)
        {
            var (a, b) = (Node.Create("ReconnectDropA"), Node.Create("ReconnectDropB"));
            try
            {
                // Both senders have used sequence numbers before the drop.
                await ExpectRealTerminatedAsync(a, b, "warm-ab");
                await ExpectRealTerminatedAsync(b, a, "warm-ba");
                await AwaitDrainedAsync(a, b);
                await AwaitDrainedAsync(b, a);

                for (var i = 0; i < drops; i++)
                {
                    await DropControlConnectionAsync(a);
                    await DropControlConnectionAsync(b);
                }

                await ExpectRealTerminatedAsync(a, b, "after-ab");
                await ExpectRealTerminatedAsync(b, a, "after-ba");
                (a.Nacks + b.Nacks).Should().Be(0);
            }
            finally
            {
                await a.System.Terminate().AwaitWithTimeout(10.Seconds());
                await b.System.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Should_DrainWindowWithoutNacks_When_SystemMessageIsUnackedAtControlDrop")]
        public async Task Should_DrainWindowWithoutNacks_When_SystemMessageIsUnackedAtControlDrop()
        {
            var (a, b) = (Node.Create("ReconnectUnackedA"), Node.Create("ReconnectUnackedB"));
            try
            {
                await ExpectRealTerminatedAsync(a, b, "warm");
                await AwaitDrainedAsync(a, b);

                // B receives the Watch but its Ack is lost, so the Watch is still in A's window at the drop.
                b.DropAcks = true;
                var acksBefore = b.Acks;
                var (target, probe) = await WatchAsync(a, b, "unacked");
                await AwaitAssertAsync(() => b.Acks.Should().BeGreaterThan(acksBefore), 5.Seconds());
                a.AssociationTo(b).SystemMessageDeliveryState.Buffer.Count.Should().BeGreaterThan(0);

                await DropControlConnectionAsync(a);
                b.DropAcks = false;

                // The replayed window is recognised as already delivered: acknowledged, never Nacked.
                await AwaitDrainedAsync(a, b);
                b.Nacks.Should().Be(0);

                target.Tell(PoisonPill.Instance);
                (await probe.ExpectMsgAsync<Terminated>(5.Seconds())).AddressTerminated.Should().BeFalse();
            }
            finally
            {
                await a.System.Terminate().AwaitWithTimeout(10.Seconds());
                await b.System.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
