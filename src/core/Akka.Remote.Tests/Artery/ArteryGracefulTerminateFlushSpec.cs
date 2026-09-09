//-----------------------------------------------------------------------
// <copyright file="ArteryGracefulTerminateFlushSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers the actual, end-user-visible shutdown path: <see cref="ActorSystem.Terminate"/>, NOT
    /// a direct call to the transport's own <c>Shutdown()</c> (that path is
    /// <see cref="ArteryShutdownFlushSpec"/>, and it already passes today -- it never exercises
    /// <c>/user</c> guardian teardown at all).
    ///
    /// <para>
    /// <b>Why the assertion is "no AbruptStageTerminationException", not message delivery.</b> A
    /// warm, already-handshake-complete association's outbound stream never completes on its own --
    /// the association-owned channel it reads from stays open indefinitely until
    /// <c>ArteryRemoting.Shutdown()</c> explicitly calls <c>CompleteOutbound()</c> -- so on a fast,
    /// local, healthy loopback connection a small burst of messages typically reaches the peer
    /// before ANY teardown-related race even matters, fix or no fix. That makes raw message
    /// delivery an unreliable discriminator here (verified empirically while writing this spec).
    /// What the placement bug actually determines is HOW the still-open stream ends: abruptly
    /// (an active <c>ActorGraphInterpreter</c> killed out from under a live stream, which
    /// unconditionally faults its <c>WatchTermination</c> monitor with
    /// <see cref="Akka.Streams.AbruptStageTerminationException"/>) or gracefully (the channel is
    /// completed first, and the stream drains and finishes on its own). That is exactly what the
    /// "Artery {stream} outbound connection to [...] failed; ... outbound stream has ended" WARNING
    /// reports, so its presence/absence is the deterministic signal.
    /// </para>
    ///
    /// <para>
    /// Before this fix, <c>ArteryRemoting.Start()</c> materialized every Artery stream on
    /// <c>ActorMaterializer.Create(System)</c>, whose <c>StreamSupervisor</c> is a <c>/user</c>
    /// actor. A graceful <see cref="ActorSystem.Terminate"/> stops the <c>/user</c> guardian FIRST --
    /// and a parent's own stop does not complete until every child (recursively) has stopped, so the
    /// <c>StreamSupervisor</c> and every graph interpreter under it are ALREADY DEAD by the time
    /// <c>RemotingTerminator</c> (a <c>/system</c> actor) observes that and finally calls
    /// <c>Transport.Shutdown()</c> -- the method that would otherwise complete the channel
    /// gracefully first. That is a strict ordering, not a race, which is why the WARNING below fires
    /// on every run pre-fix and never post-fix. Moving the materializer's <c>StreamSupervisor</c> to
    /// a <c>/system</c> actor (<c>ArteryRemoting.CreateSystemMaterializer</c>) is what changes the
    /// ordering: the outbound stream now survives the <c>/user</c> teardown, so
    /// <c>Shutdown()</c>'s own graceful <c>CompleteOutbound()</c> + kill-switch sequence gets to run
    /// while it is still alive.
    /// </para>
    /// </summary>
    public class ArteryGracefulTerminateFlushSpec : AkkaSpec
    {
        public ArteryGracefulTerminateFlushSpec(ITestOutputHelper output) : base(ArteryConfig, output)
        {
        }

        private static readonly Config ArteryConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.loggers = ["Akka.TestKit.TestEventListener, Akka.TestKit"]
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.flush-wait-on-shutdown = 5s
            akka.loglevel = INFO
            """);

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        /// <summary>Forwards everything it receives to <paramref name="target"/>.</summary>
        private sealed class Forwarder : ReceiveActor
        {
            public Forwarder(IActorRef target)
            {
                ReceiveAny(msg => target.Forward(msg));
            }
        }

        /// <summary>
        /// Sends one-way markers until one lands at this spec's test actor, establishing the
        /// association (connection plus handshake) before the assertion under test -- the same idea
        /// <c>ArteryShutdownFlushSpec</c> uses for the same purpose. A WARM association is
        /// deliberate: this spec is about how an already-open stream ends, not about connection
        /// establishment, and a cold first-contact handshake racing a peer's own reply connection
        /// introduces timing behavior this spec has nothing to do with.
        /// </summary>
        private async Task AwaitAssociationAsync(ActorSelection selection)
        {
            var attempt = 0;
            await AwaitAssertAsync(async () =>
            {
                var marker = $"warmup-{++attempt}";
                selection.Tell(marker, ActorRefs.NoSender);
                await FishForMessageAsync(msg => Equals(msg, marker), TimeSpan.FromSeconds(1));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(200));
        }

        [Fact(DisplayName = "Should_NotAbortTheOutboundStream_When_TheSenderGracefullyTerminates")]
        public async Task Should_NotAbortTheOutboundStream_When_TheSenderGracefullyTerminates()
        {
            // Sys is the RECEIVER here and outlives the sender's system.
            var senderSys = ActorSystem.Create("artery-graceful-terminate-sender", ArteryConfig);
            try
            {
                Sys.ActorOf(Props.Create(() => new Forwarder(TestActor)), "graceful-terminate-receiver");
                var selection = senderSys.ActorSelection(
                    $"akka://{Sys.Name}@127.0.0.1:{AddressOf(Sys).Port}/user/graceful-terminate-receiver");

                await AwaitAssociationAsync(selection);

                // The deterministic proof: pre-fix, /user teardown always kills the still-open
                // outbound stream out from under a live ActorGraphInterpreter, which ALWAYS faults
                // its WatchTermination monitor and logs this WARNING. Post-fix, Shutdown()'s own
                // graceful CompleteOutbound() + kill-switch sequence gets to run first, so the
                // stream finishes on its own and this WARNING never fires.
                await CreateEventFilter(senderSys)
                    .Warning(contains: "outbound stream has ended")
                    .ExpectAsync(0, async () =>
                    {
                        (await senderSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(30)))
                            .Should().BeTrue("graceful termination must actually complete");
                    });
            }
            finally
            {
                // senderSys is already terminated by the assertion above in the success case; this
                // is a no-op then and a safety net if an earlier assertion throws first.
                await senderSys.Terminate().AwaitWithTimeout(TimeSpan.FromSeconds(30));
            }
        }
    }
}
