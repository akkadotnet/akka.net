//-----------------------------------------------------------------------
// <copyright file="ClusterClientReconnectTimeoutSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.ClusterClient
{
    /// <summary>
    /// Regression tests for https://github.com/akkadotnet/akka.net/issues/8508.
    ///
    /// <see cref="Akka.Cluster.Tools.Client.ClusterClient"/> used to arm a one-shot
    /// <c>ReconnectTimeout</c> timer on every message it handled while establishing, and only ever
    /// cancelled the last one. Leftover timers that fired while the client was active were merely
    /// noise, but one that fired after the client went back to establishing stopped a healthy
    /// client mid-reconnect - long before its own reconnect deadline was due.
    /// </summary>
    public class ClusterClientReconnectTimeoutSpec : AkkaSpec
    {
        /// <summary>
        /// The reconnect deadline. Long enough that the two phases of the kill test are far apart.
        /// </summary>
        private static readonly TimeSpan ReconnectTimeout = 10.Seconds();

        public ClusterClientReconnectTimeoutSpec(ITestOutputHelper output) : base(GetConfig(), output)
        {
        }

        private static Config GetConfig()
        {
            // Heartbeats and contact refreshes are pinned out of the way so the only timer that can
            // move during these tests is the reconnect deadline under test. Without that, an
            // acceptable-heartbeat-pause expiry would race the deadline and blur which one fired.
            return ConfigurationFactory.ParseString($@"
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.dot-netty.tcp.hostname = 127.0.0.1
                akka.loglevel = INFO
                akka.cluster.client {{
                  heartbeat-interval = 1d
                  acceptable-heartbeat-pause = 1d
                  refresh-contacts-interval = 1d
                  reconnect-timeout = {ReconnectTimeout.TotalSeconds}s
                }}
            ").WithFallback(ClusterClientReceptionist.DefaultConfig());
        }

        /// <summary>
        /// Brings up a single-node cluster with a receptionist and one registered service, then
        /// returns a client that has completed its first establishing phase.
        /// </summary>
        private async Task<(IActorRef client, IActorRef receptionist)> StartEstablishedClientAsync()
        {
            var cluster = Akka.Cluster.Cluster.Get(Sys);
            cluster.Join(cluster.SelfAddress);
            await AwaitAssertAsync(
                () => cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(1),
                10.Seconds(),
                200.Milliseconds());

            var receptionist = ClusterClientReceptionist.Get(Sys);
            receptionist.RegisterService(Sys.ActorOf(EchoActor.Props(this, true), "testService"));

            var contacts = ImmutableHashSet.Create(receptionist.Underlying.Path);
            var client = Sys.ActorOf(
                Akka.Cluster.Tools.Client.ClusterClient.Props(
                    ClusterClientSettings.Create(Sys).WithInitialContacts(contacts)),
                "client");

            // The echo round trip proves the client reached Active. It also makes the client
            // heartbeat the receptionist, which is what registers it for the shutdown notification
            // the kill test relies on.
            client.Tell(new Akka.Cluster.Tools.Client.ClusterClient.Send("/user/testService", "hello", localAffinity: true));
            await ExpectMsgAsync("hello", 10.Seconds());

            return (client, receptionist.Underlying);
        }

        [Fact(DisplayName =
            "ClusterClient should not be stopped by a reconnect deadline armed during an earlier establishing phase")]
        public async Task Should_Survive_Stale_Reconnect_Deadline_When_Reestablishing()
        {
            var (client, receptionist) = await StartEstablishedClientAsync();

            var deathProbe = CreateTestProbe();
            deathProbe.Watch(client);

            // Phase 1 armed its deadline when the client was constructed, so it comes due at
            // roughly T0 + 10s. Sit in Active for 7s of that, which is also an assertion: an active
            // client must not be stopped by anything.
            var activeWindow = 7.Seconds();
            await deathProbe.ExpectNoMsgAsync(activeWindow);

            // Stopping the receptionist makes it tell its registered clients it is going away, which
            // sends the client back to establishing. Nothing replaces the receptionist, so the
            // client stays there and cannot mask a premature stop by reconnecting.
            Sys.Stop(receptionist);

            // Phase 2 begins here, so a correct client lives until roughly now + 10s.
            //
            // On the unfixed code a leftover phase-1 deadline fires at T0 + 10s, which is only
            // 10 - 7 = 3s into this window, and stops the client. So a 5s window fails there with
            // ~2s to spare and passes on fixed code with ~5s to spare: the deadline that legitimately
            // applies is still 5s away when the window closes.
            var survivalWindow = 5.Seconds();
            await deathProbe.ExpectNoMsgAsync(survivalWindow);

            // Still alive and still doing its job: it must be trying to reconnect, not sitting dead.
            client.Tell(GetContactPoints.Instance, deathProbe.Ref);
            (await deathProbe.ExpectMsgAsync<ContactPoints>(3.Seconds()))
                .ContactPointsList.Should().NotBeEmpty();
        }

        [Fact(DisplayName =
            "ClusterClient should leave no stray ReconnectTimeout behind across establish, active and re-establish")]
        public async Task Should_Not_Leak_ReconnectTimeout_Timers()
        {
            // The leak is directly observable: every abandoned timer eventually fires into an actor
            // that is no longer establishing, where ReconnectTimeout is unhandled and lands on the
            // event stream. A correct client arms exactly one deadline per establishing phase and
            // cancels it on the way into Active, so this collector must stay empty.
            var strayProbe = CreateTestProbe();
            var collector = Sys.ActorOf(Props.Create(() => new ReconnectTimeoutCollector(strayProbe.Ref)));
            Sys.EventStream.Subscribe(collector, typeof(UnhandledMessage));
            Sys.EventStream.Subscribe(collector, typeof(DeadLetter));

            var (client, receptionist) = await StartEstablishedClientAsync();

            // Stay ACTIVE across a whole reconnect-timeout. This is where a leak becomes visible:
            // any deadline abandoned by the first establishing phase comes due here, and
            // ReconnectTimeout is unhandled once the client is active, so each one is published.
            // Waiting inside establishing would hide the leak instead, because there the message is
            // handled - by stopping the client.
            await strayProbe.ExpectNoMsgAsync(ReconnectTimeout + 2.Seconds());

            // Now cover the re-establish leg of the cycle. The client is still well inside its
            // phase-2 deadline when this window closes, so a correct client publishes nothing here
            // either. The deadline it eventually hits is handled while establishing, not stray.
            Sys.Stop(receptionist);
            await strayProbe.ExpectNoMsgAsync(5.Seconds());
        }

        /// <summary>
        /// Forwards only stray <c>ReconnectTimeout</c> messages, so unrelated event-stream traffic
        /// cannot be misread as a leak.
        /// </summary>
        private sealed class ReconnectTimeoutCollector : ReceiveActor
        {
            public ReconnectTimeoutCollector(IActorRef target)
            {
                Receive<UnhandledMessage>(m =>
                {
                    if (m.Message is Akka.Cluster.Tools.Client.ClusterClient.ReconnectTimeout)
                        target.Tell(m.Message);
                });

                Receive<DeadLetter>(m =>
                {
                    if (m.Message is Akka.Cluster.Tools.Client.ClusterClient.ReconnectTimeout)
                        target.Tell(m.Message);
                });
            }
        }
    }
}
