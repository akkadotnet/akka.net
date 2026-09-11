//-----------------------------------------------------------------------
// <copyright file="ArteryInboundContextPublishSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

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
    /// <summary>
    /// P5 regression guard: <c>ArteryRemoting.Start</c> must publish <c>_localUniqueAddress</c> and
    /// <c>_inboundContext</c> before anything else that runs once the bound port is known.
    /// <c>HandleIncomingConnection</c> wires the inbound context into every accepted connection's
    /// <c>InboundHandshakeStage</c>/<c>InboundQuarantineCheckStage</c>/<c>SystemMessageAckerStage</c>
    /// the moment it is dispatched, and the underlying <c>ConnectionSourceStage</c> already asked
    /// the TCP manager to resume accepting as part of the SAME "Bound" message turn that completes
    /// the bind's own promise -- so a peer already dialing this (possibly pinned) port can have a
    /// connection accepted and dispatched on a different thread concurrently with this one resuming
    /// past the blocking bind-completion wait. Before the fix, several unrelated statements
    /// (<c>_defaultAddress</c>/<c>_addresses</c>, <c>SubscribeControl</c>, a log line) ran between
    /// "the port is known" and "the inbound context is published", widening that window; a
    /// concurrent accept in it dereferences a null inbound context (design.md group 9's Linux
    /// residue: <c>DistributedPubSubRestartSpec</c>'s "Bind commander died, stopping listener").
    ///
    /// <para>
    /// The actual OS-level race is not reproducible deterministically in a unit test (that is
    /// exactly what makes it a bug), so this spec observes the ORDERING directly via
    /// <see cref="ArteryTransportSetup.OnBoundPortKnown"/>, a test-observability hook invoked in
    /// <see cref="ArteryRemoting.Start"/> AFTER <c>_defaultAddress</c>/<c>_addresses</c> are
    /// published (see that method's remarks), passed <c>_inboundContext is not null</c> read at
    /// that point. That makes the hook's argument order-dependent rather than tautological: it
    /// reads <see langword="true"/> only because <c>_localUniqueAddress</c>/<c>_inboundContext</c>
    /// were assigned earlier in the method, and would read <see langword="false"/> here if those
    /// two assignments were ever moved back down after <c>_defaultAddress</c>/<c>_addresses</c> --
    /// exactly the same technique <c>ArteryInboundLanesSpec</c>/<c>ArteryInboundLanesQuarantineSpec</c>
    /// use for the lane-count hook.
    /// </para>
    /// </summary>
    public class ArteryInboundContextPublishSpec : AkkaSpec
    {
        public ArteryInboundContextPublishSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig() => ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            """);

        [Fact(DisplayName = "P5: the inbound context must already be published by the time Start() learns the bound port, before any other startup work runs")]
        public async Task InboundContext_should_be_published_before_bound_port_is_reported()
        {
            bool? publishedAlready = null;
            var setup = BootstrapSetup.Create().WithConfig(ArteryConfig())
                .And(new ArteryTransportSetup(onBoundPortKnown: p => publishedAlready = p));

            var system = ActorSystem.Create("ArteryInboundContextPublishSpecSystem", setup);
            try
            {
                publishedAlready.Should().NotBeNull("ArteryTransportSetup.OnBoundPortKnown must fire once during Start()");
                publishedAlready!.Value.Should().BeTrue(
                    "_localUniqueAddress/_inboundContext must already be assigned by the time the bound port is " +
                    "known -- an accepted connection's HandleIncomingConnection dereferences them the moment it " +
                    "is dispatched, and the listener can already be accepting connections by this point");
            }
            finally
            {
                await system.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
