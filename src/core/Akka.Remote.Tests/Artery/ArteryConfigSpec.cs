//-----------------------------------------------------------------------
// <copyright file="ArteryConfigSpec.cs" company="Akka.NET Project">
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
using Akka.Remote.Configuration;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Covers Artery TCP remoting task group 1, "Configuration and entry point"
    /// (<c>openspec/changes/artery-tcp-remoting/tasks.md</c>): the <c>akka.remote.artery</c>
    /// config section, <see cref="ArterySettings"/> parsing/validation, and the
    /// <see cref="RemoteActorRefProvider"/> classic-vs-Artery transport switch wired in
    /// <c>RemoteActorRefProvider.CreateInternals()</c>. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c>, "Handshake + association/UID
    /// (gate G2)" -&gt; "Provider integration".
    /// </summary>
    public class ArteryConfigSpec : AkkaSpec
    {
        private static readonly Config ClassicRemotingConfig = ConfigurationFactory.ParseString(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.dot-netty.tcp {
                hostname = 127.0.0.1
                port = 0
            }
        ");

        public ArteryConfigSpec(ITestOutputHelper output) : base(ClassicRemotingConfig, output)
        {
        }

        [Fact(DisplayName = "Should_HaveArteryDisabled_ByDefault_InReferenceConf")]
        public void Should_HaveArteryDisabled_ByDefault_InReferenceConf()
        {
            var config = RARP.For(Sys).Provider.RemoteSettings.Config;
            config.GetBoolean("akka.remote.artery.enabled").Should().BeFalse();
        }

        [Fact(DisplayName = "Should_SelectClassicRemoting_When_ArteryNotEnabled")]
        public void Should_SelectClassicRemoting_When_ArteryNotEnabled()
        {
            var provider = RARP.For(Sys).Provider;
            provider.Transport.Should().BeOfType<Remoting>();
        }

        [Fact(DisplayName = "Should_UseAkkaTcpScheme_When_ClassicRemotingIsActive")]
        public void Should_UseAkkaTcpScheme_When_ClassicRemotingIsActive()
        {
            var defaultAddress = RARP.For(Sys).Provider.DefaultAddress;
            defaultAddress.Protocol.Should().Be("akka.tcp");
        }

        [Fact(DisplayName = "Should_ParseArterySettingsDefaults_When_ConfigNotOverridden")]
        public void Should_ParseArterySettingsDefaults_When_ConfigNotOverridden()
        {
            var arteryConfig = RARP.For(Sys).Provider.RemoteSettings.Config.GetConfig("akka.remote.artery");
            var settings = new ArterySettings(arteryConfig);

            settings.Enabled.Should().BeFalse();
            settings.Transport.Should().Be("tcp");
            settings.CanonicalHostname.Should().Be("localhost");
            settings.CanonicalPort.Should().Be(25520);
            settings.MaximumFrameSize.Should().Be(256 * 1024);
            settings.InboundLanes.Should().Be(4);
            settings.OutboundLanes.Should().Be(1);
            settings.HandshakeTimeout.Should().Be(20.Seconds());
            settings.HandshakeRetryInterval.Should().Be(1.Seconds());
            settings.InjectHandshakeInterval.Should().Be(1.Seconds());
            settings.ControlHeartbeatInterval.Should().Be(5.Seconds());

            // Reliable system-message delivery (design.md gate G3).
            settings.SystemMessageBufferSize.Should().Be(20_000);
            settings.SystemMessageResendInterval.Should().Be(1.Seconds());
            settings.GiveUpSystemMessageAfter.Should().Be(6.Hours(),
                "Pekko's Artery default (NOT classic Akka.NET's much shorter ~3-minute analogue) -- see ArterySettings.GiveUpSystemMessageAfter's remarks");
        }

        [Theory(DisplayName = "Should_ThrowConfigurationException_When_ArterySettingIsInvalid")]
        [InlineData("akka.remote.artery.advanced.maximum-frame-size = 0b")]
        [InlineData("akka.remote.artery.advanced.maximum-frame-size = 20000000b")] // > 0x00FFFFFF (16 MiB - 1)
        [InlineData("akka.remote.artery.advanced.inbound-lanes = 0")]
        [InlineData("akka.remote.artery.advanced.outbound-lanes = 0")]
        [InlineData("akka.remote.artery.transport = aeron")]
        [InlineData("akka.remote.artery.advanced.system-message-buffer-size = 0")]
        [InlineData("akka.remote.artery.advanced.system-message-resend-interval = 0s")]
        [InlineData("akka.remote.artery.advanced.give-up-system-message-after = 0s")]
        public void Should_ThrowConfigurationException_When_ArterySettingIsInvalid(string overrideHocon)
        {
            var arteryConfig = ConfigurationFactory.ParseString(overrideHocon)
                .WithFallback(RemoteConfigFactory.Default())
                .GetConfig("akka.remote.artery");

            Assert.Throws<ConfigurationException>(() => new ArterySettings(arteryConfig));
        }

        [Fact(DisplayName = "Should_SelectArteryRemoting_When_ArteryEnabled_ViaConfig")]
        public async Task Should_SelectArteryRemoting_When_ArteryEnabled_ViaConfig()
        {
            // canonical.port = 0 would mean "bind ephemeral", which the G2 configuration/entry-point
            // skeleton does not implement yet (no socket is actually bound) -- use an explicit port.
            var config = ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = localhost
                akka.remote.artery.canonical.port = 25521
            ");

            var system = ActorSystem.Create("ArteryEnabledSystem", config);
            try
            {
                var provider = RARP.For(system).Provider;
                provider.Transport.Should().BeOfType<ArteryRemoting>();

                var expectedAddress = new Address("akka", system.Name, "localhost", 25521);
                provider.DefaultAddress.Should().Be(expectedAddress);
                provider.DefaultAddress.Protocol.Should().Be("akka");
                provider.DefaultAddress.ToString().Should().Be($"akka://{system.Name}@localhost:25521");
            }
            finally
            {
                await system.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
