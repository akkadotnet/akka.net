//-----------------------------------------------------------------------
// <copyright file="ArteryBlackholeEndToEndSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// End-to-end (two real transports over loopback TCP, no TestConductor) coverage for artery
    /// <c>advanced.test-mode</c> failure injection: proves the test stages are actually WOVEN into
    /// the materialized pipelines (not just unit-correct in isolation) by driving
    /// <c>ManagementCommand(SetThrottle(...))</c> exactly the way the multi-node TestConductor
    /// Player does, and observing message flow stop and resume. Scenario 2 issues the command on
    /// the REMOTE system, exercising the inbound-side drop path. Assertions are progress-based
    /// (a round-trip completes / a later round-trip completes) -- never wall-clock timings.
    /// </summary>
    public class ArteryBlackholeEndToEndSpec : AkkaSpec
    {
        public ArteryBlackholeEndToEndSpec(ITestOutputHelper output) : base(ArteryTestModeConfig, output)
        {
        }

        /// <summary>
        /// Same shape as <c>ArteryReconnectSpec.ArteryConfig</c> (short restart backoff and control
        /// heartbeat so the deterministic reconnect machinery runs fast in a test environment; no
        /// assertion depends on these timings) plus <c>advanced.test-mode = on</c>.
        /// </summary>
        private static readonly Config ArteryTestModeConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.test-mode = on
            akka.remote.artery.advanced.outbound-restart-backoff = 300ms
            akka.remote.artery.advanced.control-heartbeat-interval = 500ms
            """);

        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }

        private static ArteryRemoting TransportOf(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        /// <summary>
        /// Sends pings until one echoes back -- used to (a) establish the association before any
        /// blackhole is applied (a pre-handshake blackhole would drop the handshake itself, by
        /// design) and (b) prove recovery after PassThrough (ordinary delivery is at-most-once, so
        /// pings swallowed by the blackhole are simply gone; only a FRESH ping's echo proves the
        /// link healed). Uses a FRESH probe and a unique per-attempt marker, fishing for exactly
        /// the current attempt's echo -- a slow earlier attempt's late echo is swallowed by the
        /// fish rather than tripping a later assertion (each test phase also uses its own probe so
        /// a stray late echo from THIS phase can never leak into a subsequent ExpectNoMsg).
        /// </summary>
        private async Task AwaitRoundTripAsync(ActorSelection selection, string marker)
        {
            var probe = CreateTestProbe();
            var attempt = 0;
            await AwaitAssertAsync(async () =>
            {
                var attemptMarker = $"{marker}-{++attempt}";
                selection.Tell(attemptMarker, probe.Ref);
                await probe.FishForMessageAsync(msg => Equals(msg, attemptMarker), TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
        }

        [Fact(DisplayName = "blackhole(Both) applied on the LOCAL system severs the link; PassThrough heals it and traffic resumes")]
        public async Task Should_Blackhole_And_Heal_From_Local_Side()
        {
            var remoteSys = ActorSystem.Create("blackhole-remote-1", ArteryTestModeConfig);
            try
            {
                remoteSys.ActorOf(Props.Create(() => new Echo()), "echo");
                var remoteAddress = AddressOf(remoteSys);
                var selection = Sys.ActorSelection($"akka://{remoteSys.Name}@127.0.0.1:{remoteAddress.Port}/user/echo");

                // 1. Association established + healthy round-trip BEFORE injecting the failure.
                await AwaitRoundTripAsync(selection, "before-blackhole");

                // 2. Blackhole, issued on the LOCAL transport exactly as the TestConductor Player
                //    would (SetThrottle rate 0 => Blackhole).
                var transport = TransportOf(Sys);
                (await transport.ManagementCommand(new SetThrottle(remoteAddress, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                    .Should().BeTrue();

                // 3. Pings are now swallowed (dropped at this node's outbound test stage). Fresh
                //    probe: only THIS phase's echo could ever reach it.
                var duringProbe = CreateTestProbe();
                selection.Tell("during-blackhole", duringProbe.Ref);
                await duringProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

                // 4. Heal, then prove a FRESH round-trip completes.
                (await transport.ManagementCommand(new SetThrottle(remoteAddress, ThrottleTransportAdapter.Direction.Both, Unthrottled.Instance)))
                    .Should().BeTrue();
                await AwaitRoundTripAsync(selection, "after-heal");
            }
            finally
            {
                await remoteSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
            }
        }

        [Fact(DisplayName = "blackhole(Send) applied on the REMOTE system drops traffic at ITS stages (inbound drop of our pings); PassThrough heals")]
        public async Task Should_Blackhole_And_Heal_From_Remote_Side()
        {
            var remoteSys = ActorSystem.Create("blackhole-remote-2", ArteryTestModeConfig);
            try
            {
                remoteSys.ActorOf(Props.Create(() => new Echo()), "echo");
                var remoteAddress = AddressOf(remoteSys);
                var localAddress = AddressOf(Sys);
                var selection = Sys.ActorSelection($"akka://{remoteSys.Name}@127.0.0.1:{remoteAddress.Port}/user/echo");

                await AwaitRoundTripAsync(selection, "before-blackhole");

                // Send-direction blackhole at the REMOTE node targeting US: per the verbatim Pekko
                // key-order semantics this is a full cut AT THAT node -- it drops both its outbound
                // replies to us AND its inbound from us (our pings die at ITS InboundTestStage).
                // This mirrors how a single TestConductor blackhole command routed to ONE node
                // still produces a partition observable from the other side.
                var remoteTransport = TransportOf(remoteSys);
                (await remoteTransport.ManagementCommand(new SetThrottle(localAddress, ThrottleTransportAdapter.Direction.Send, Blackhole.Instance)))
                    .Should().BeTrue();

                var duringProbe = CreateTestProbe();
                selection.Tell("during-blackhole", duringProbe.Ref);
                await duringProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

                (await remoteTransport.ManagementCommand(new SetThrottle(localAddress, ThrottleTransportAdapter.Direction.Send, Unthrottled.Instance)))
                    .Should().BeTrue();
                await AwaitRoundTripAsync(selection, "after-heal");
            }
            finally
            {
                await remoteSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(10));
            }
        }
    }
}
