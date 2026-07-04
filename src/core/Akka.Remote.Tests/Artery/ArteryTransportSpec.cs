//-----------------------------------------------------------------------
// <copyright file="ArteryTransportSpec.cs" company="Akka.NET Project">
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
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers Artery TCP remoting task group 5, "Plaintext TCP Transport"
    /// (<c>openspec/changes/artery-tcp-remoting/tasks.md</c>) -- the G2 gate: two ActorSystems
    /// exchange ordinary user messages over Artery TCP (single ordinary stream, single lane, no
    /// compression), with the handshake + UID established over that same connection. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>, "G2 staging" and "Connection
    /// cardinality".
    ///
    /// <para>
    /// Every test creates its OWN pair of ActorSystems (never reusing the outer <see cref="AkkaSpec.Sys"/>,
    /// which stays on classic remoting) and ALWAYS tears both down in a <c>finally</c> block --
    /// system termination itself is asserted (via <see cref="AwaitWithTimeoutExtensions.AwaitWithTimeout"/>)
    /// on every test, which is exactly the "clean shutdown" coverage bullet.
    /// </para>
    /// </summary>
    public class ArteryTransportSpec : AkkaSpec
    {
        public ArteryTransportSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """);

        private static int BoundPort(ActorSystem system) => RARP.For(system).Provider.DefaultAddress.Port!.Value;

        private static string EchoSelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target)
            {
                ReceiveAny(msg => target.Tell(msg, Sender));
            }
        }

        [Fact(DisplayName = "Artery TCP round-trip: A resolves B's echo actor via ActorSelection and exchanges a message both ways")]
        public async Task Should_RoundTrip_Message_Over_Artery_Tcp()
        {
            var systemA = ActorSystem.Create("ArteryRoundTripA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryRoundTripB", ArteryConfig());
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                // A -> B: ordinary send establishes A's outbound association (+ embedded handshake).
                // B -> A: the ActorSelection ResolveOne's ActorIdentity reply, and later the echo
                // reply, establish B's SEPARATE outbound association back to A (see the type-level
                // "Connection cardinality" remarks on ArteryRemoting).
                var echoSelection = systemA.ActorSelection(EchoSelectionPath(systemB, "echo"));
                var echoRef = await echoSelection.ResolveOne(TimeSpan.FromSeconds(10));

                var probe = CreateTestProbe(systemA);
                echoRef.Tell("ping", probe.Ref);

                await probe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "DefaultAddress reflects the bound ephemeral port and uses the akka scheme")]
        public async Task Should_Reflect_Bound_Ephemeral_Port_In_DefaultAddress()
        {
            var system = ActorSystem.Create("ArteryPortCheck", ArteryConfig());
            try
            {
                var defaultAddress = RARP.For(system).Provider.DefaultAddress;

                defaultAddress.Protocol.Should().Be("akka");
                defaultAddress.System.Should().Be("ArteryPortCheck");
                defaultAddress.Port.Should().NotBeNull();
                defaultAddress.Port!.Value.Should().BeGreaterThan(0, "canonical.port = 0 must resolve to the actual bound ephemeral port");
            }
            finally
            {
                await system.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Ordinary-stream messages dispatch to the correct actor when multiple actors are hosted on the receiving system")]
        public async Task Should_Dispatch_To_Correct_Actor_When_Multiple_Hosted()
        {
            var systemA = ActorSystem.Create("ArteryDispatchA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryDispatchB", ArteryConfig());
            try
            {
                var probe1 = CreateTestProbe(systemB, "probe1");
                var probe2 = CreateTestProbe(systemB, "probe2");

                systemB.ActorOf(Props.Create(() => new Forwarder(probe1.Ref)), "actor1");
                systemB.ActorOf(Props.Create(() => new Forwarder(probe2.Ref)), "actor2");

                var ref1 = await systemA.ActorSelection(EchoSelectionPath(systemB, "actor1")).ResolveOne(TimeSpan.FromSeconds(10));
                var ref2 = await systemA.ActorSelection(EchoSelectionPath(systemB, "actor2")).ResolveOne(TimeSpan.FromSeconds(10));

                ref1.Tell("to-actor-1");
                ref2.Tell("to-actor-2");

                await probe1.ExpectMsgAsync("to-actor-1", TimeSpan.FromSeconds(10));
                await probe2.ExpectMsgAsync("to-actor-2", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Sending to an unresolvable recipient path publishes a DeadLetter on the receiving system")]
        public async Task Should_DeadLetter_Unresolvable_Recipient()
        {
            var systemA = ActorSystem.Create("ArteryDeadLetterA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryDeadLetterB", ArteryConfig());
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                // Warm up the association first (a successful round-trip guarantees the handshake
                // has completed), so the dead-letter assertion below isn't confused by handshake
                // retry traffic.
                var echoRef = await systemA.ActorSelection(EchoSelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var warmupProbe = CreateTestProbe(systemA);
                echoRef.Tell("warmup", warmupProbe.Ref);
                await warmupProbe.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));

                // A direct dead-letter subscription (rather than EventFilter/Mute) -- EventFilter's
                // Mute/Unmute mechanism depends on Akka.TestKit.TestEventListener being wired up as
                // one of the target system's `akka.loggers`, which a bare `ActorSystem.Create` (no
                // AkkaSpec/TestKit bootstrap) does not do for systemB.
                var deadLetterProbe = CreateTestProbe(systemB);
                systemB.EventStream.Subscribe(deadLetterProbe.Ref, typeof(DeadLetter));
                try
                {
                    systemA.ActorSelection(EchoSelectionPath(systemB, "does-not-exist")).Tell("nobody-home");

                    var deadLetter = await deadLetterProbe.ExpectMsgAsync<DeadLetter>(TimeSpan.FromSeconds(10));
                    deadLetter.Message.Should().Be("nobody-home");
                }
                finally
                {
                    systemB.EventStream.Unsubscribe(deadLetterProbe.Ref, typeof(DeadLetter));
                }
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Both ActorSystems terminate cleanly within a timeout after exchanging Artery traffic")]
        public async Task Should_Terminate_Cleanly_After_Traffic()
        {
            var systemA = ActorSystem.Create("ArteryShutdownA", ArteryConfig());
            var systemB = ActorSystem.Create("ArteryShutdownB", ArteryConfig());
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");

                var echoRef = await systemA.ActorSelection(EchoSelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));

                var probe = CreateTestProbe(systemA);
                echoRef.Tell("ping", probe.Ref);
                await probe.ExpectMsgAsync("ping", TimeSpan.FromSeconds(10));
            }
            finally
            {
                // The assertion IS the clean-shutdown gate: AwaitWithTimeout throws (failing the
                // test) if either system fails to terminate within the timeout.
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
