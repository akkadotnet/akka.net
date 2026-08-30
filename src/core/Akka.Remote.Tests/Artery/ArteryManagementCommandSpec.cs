//-----------------------------------------------------------------------
// <copyright file="ArteryManagementCommandSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers <c>ArteryRemoting.ManagementCommand</c> under <c>advanced.test-mode</c> (Pekko
    /// <c>ArteryTransport.managementCommand</c> parity): <see cref="SetThrottle"/> with
    /// <see cref="Blackhole"/>/<see cref="Unthrottled"/> mutates the shared test state and reports
    /// <see langword="true"/>; EVERYTHING else -- rate throttles, <see cref="ForceDisassociate"/>,
    /// unknown commands -- reports <see langword="false"/>; and with test-mode OFF (the default)
    /// every command short-circuits to <see langword="false"/> with no state to mutate (the
    /// <see cref="SharedTestState"/> is never even created -- the off-mode byte-identity guard).
    /// </summary>
    public class ArteryManagementCommandSpec : AkkaSpec
    {
        private static readonly Config TestModeConfig = ConfigurationFactory.ParseString("""
            akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
            akka.remote.artery.enabled = on
            akka.remote.artery.canonical.hostname = "127.0.0.1"
            akka.remote.artery.canonical.port = 0
            akka.remote.artery.advanced.test-mode = on
            """);

        public ArteryManagementCommandSpec(ITestOutputHelper output) : base(TestModeConfig, output)
        {
        }

        private ArteryRemoting Transport => (ArteryRemoting)RARP.For(Sys).Provider.Transport;

        private static readonly Address Target = new("akka", "peer-sys", "10.0.0.2", 2552);

        [Fact(DisplayName = "test-mode on: SetThrottle(Blackhole) reports true and registers the directed blackhole pairs")]
        public async Task Should_Apply_Blackhole()
        {
            var transport = Transport;
            var localAddress = RARP.For(Sys).Provider.DefaultAddress;

            (await transport.ManagementCommand(new SetThrottle(Target, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                .Should().BeTrue();

            transport.TestState.Should().NotBeNull();
            transport.TestState!.IsBlackhole(localAddress, Target).Should().BeTrue();
            transport.TestState!.IsBlackhole(Target, localAddress).Should().BeTrue();
        }

        [Fact(DisplayName = "test-mode on: SetThrottle(Unthrottled) reports true and heals the pair")]
        public async Task Should_Apply_PassThrough()
        {
            var transport = Transport;
            var localAddress = RARP.For(Sys).Provider.DefaultAddress;

            (await transport.ManagementCommand(new SetThrottle(Target, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                .Should().BeTrue();
            (await transport.ManagementCommand(new SetThrottle(Target, ThrottleTransportAdapter.Direction.Both, Unthrottled.Instance)))
                .Should().BeTrue();

            transport.TestState!.IsBlackhole(localAddress, Target).Should().BeFalse();
            transport.TestState!.IsBlackhole(Target, localAddress).Should().BeFalse();
        }

        [Fact(DisplayName = "test-mode on: a rate throttle (TokenBucket) is UNSUPPORTED on artery and reports false")]
        public async Task Should_Reject_Rate_Throttle()
        {
            var rate = new SetThrottle(Target, ThrottleTransportAdapter.Direction.Both,
                new TokenBucket(1000, 0.01f * 125000, 0, 0));

            (await Transport.ManagementCommand(rate)).Should().BeFalse(
                "artery test-mode supports blackhole/passThrough only (Pekko parity); a rate throttle must fail " +
                "loudly at the TestConductor Player instead of silently no-oping");
        }

        [Fact(DisplayName = "test-mode on: ForceDisassociate and unknown commands report false")]
        public async Task Should_Reject_Unsupported_Commands()
        {
            (await Transport.ManagementCommand(new ForceDisassociate(Target))).Should().BeFalse();
            (await Transport.ManagementCommand("bogus")).Should().BeFalse();
        }

        [Fact(DisplayName = "test-mode on: the CancellationToken overload delegates to the same handling")]
        public async Task Should_Handle_CancellationToken_Overload()
        {
            var transport = Transport;
            var localAddress = RARP.For(Sys).Provider.DefaultAddress;

            (await transport.ManagementCommand(
                    new SetThrottle(Target, ThrottleTransportAdapter.Direction.Send, Blackhole.Instance),
                    cancellationToken: default))
                .Should().BeTrue();
            transport.TestState!.IsBlackhole(localAddress, Target).Should().BeTrue();
        }

        [Fact(DisplayName = "test-mode OFF (default): every command reports false and no test state exists at all")]
        public async Task Should_ShortCircuit_When_TestMode_Off()
        {
            var offConfig = ConfigurationFactory.ParseString("""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                """);

            var offSys = ActorSystem.Create("management-off-sys", offConfig);
            try
            {
                var transport = (ArteryRemoting)RARP.For(offSys).Provider.Transport;

                transport.TestModeEnabled.Should().BeFalse();
                transport.TestState.Should().BeNull("with test-mode off the SharedTestState must never be created -- " +
                    "stage insertion is gated on its existence, so null IS the byte-identical-pipeline guarantee");

                (await transport.ManagementCommand(new SetThrottle(Target, ThrottleTransportAdapter.Direction.Both, Blackhole.Instance)))
                    .Should().BeFalse();
                (await transport.ManagementCommand(new SetThrottle(Target, ThrottleTransportAdapter.Direction.Both, Unthrottled.Instance)))
                    .Should().BeFalse();
            }
            finally
            {
                await offSys.Terminate();
            }
        }
    }
}
