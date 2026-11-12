// -----------------------------------------------------------------------
// <copyright file="MultiNodeTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.TestKit.Tests;

public sealed class MultiNodeTransportSpec
{
    private static Func<string, string?> EnvironmentFor(string? mode, string? streams = null) =>
        name => name == MultiNodeTransport.ModeVariable ? mode : name == MultiNodeTransport.StreamsVariable ? streams : null;

    [Theory(DisplayName = "Multi-node transport selection preserves defaults and recognizes explicit modes")]
    [InlineData(null, "classic")]
    [InlineData("", "classic")]
    [InlineData("classic", "classic")]
    [InlineData("ARTERY", "artery")]
    [InlineData(" artery-quic ", "artery-quic")]
    public void Should_SelectMode_When_ExplicitOrDefault(string? input, string expected) =>
        MultiNodeTransport.ParseMode(input).Should().Be(expected);

    [Theory(DisplayName = "Multi-node transport selection rejects unknown modes instead of falling back")]
    [InlineData("quic")]
    [InlineData("artery-typo")]
    public void Should_RejectMode_When_Unknown(string mode) =>
        Assert.Throws<ConfigurationException>(() => MultiNodeTransport.Defaults("127.0.0.1", 0, EnvironmentFor(mode)));

    [Theory(DisplayName = "QUIC multi-node defaults use independent streams and bounded matching admission")]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(64)]
    public void Should_ConfigureStreams_When_QuicSelected(int streams)
    {
        var config = MultiNodeTransport.Defaults("127.0.0.1", 1234, EnvironmentFor("artery-quic", streams.ToString()));
        MultiNodeTransport.EffectiveMode(config).Should().Be("artery-quic");
        config.GetString("akka.remote.artery.canonical.hostname").Should().Be("127.0.0.1");
        config.GetInt("akka.remote.artery.canonical.port").Should().Be(1234);
        config.GetInt("akka.remote.artery.advanced.outbound-lanes").Should().Be(streams);
        config.GetInt("akka.remote.artery.advanced.inbound-lanes").Should().Be(1);
        config.GetInt("akka.remote.artery.advanced.quic.ordinary-streams").Should().Be(streams);
        config.GetInt("akka.remote.artery.advanced.quic.maximum-inbound-ordinary-streams").Should().Be(streams);
    }

    [Theory(DisplayName = "QUIC multi-node configuration rejects invalid native stream budgets")]
    [InlineData("0")]
    [InlineData("65")]
    [InlineData("not-a-number")]
    public void Should_RejectStreams_When_OutsideBounds(string streams) =>
        Assert.Throws<ConfigurationException>(() => MultiNodeTransport.Defaults("127.0.0.1", 0, EnvironmentFor("artery-quic", streams)));

    [Theory(DisplayName = "Multi-node transport fallback respects explicit per-spec classic and TCP choices")]
    [InlineData("akka.remote.artery.enabled = off", "classic")]
    [InlineData("akka.remote.artery.transport = tcp", "artery")]
    public void Should_RespectOverride_When_SpecPinsTransport(string spec, string expected)
    {
        var fallback = MultiNodeTransport.Defaults("127.0.0.1", 0, EnvironmentFor("artery-quic"));
        var effective = ConfigurationFactory.ParseString(spec).WithFallback(fallback);
        MultiNodeTransport.EffectiveMode(effective).Should().Be(expected);
    }

    [Theory(DisplayName = "Default and TCP multi-node modes do not change their lane budgets")]
    [InlineData(null)]
    [InlineData("classic")]
    [InlineData("artery")]
    public void Should_LeaveLaneDefaults_When_QuicNotRequested(string? mode)
    {
        var config = MultiNodeTransport.Defaults("127.0.0.1", 0, EnvironmentFor(mode, "invalid-unused-quic-value"));
        config.HasPath("akka.remote.artery.advanced").Should().BeFalse();
        MultiNodeTransport.EffectiveMode(config).Should().Be(mode == "artery" ? "artery" : "classic");
    }

    [Theory(DisplayName = "QUIC multi-node startup rejects a local provider before native initialization")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_RejectStartup_When_ProviderIsLocal(bool typedProvider)
    {
        var environment = EnvironmentFor("artery-quic");
        var config = ConfigurationFactory.ParseString("akka.actor.provider = local")
            .WithFallback(MultiNodeTransport.Defaults("127.0.0.1", 0, environment));
        var bootstrap = BootstrapSetup.Create().WithConfig(config);
        if (typedProvider)
            bootstrap = bootstrap.WithActorRefProvider(ProviderSelection.Local.Instance);
        var exception = Assert.Throws<ConfigurationException>(() => MultiNodeTransport.CreateSystem(
            "LocalQuic", ActorSystemSetup.Create(bootstrap), environment));
        exception.Message.Should().Contain("remote or cluster actor provider");
    }

    [Theory(DisplayName = "QUIC multi-node startup rejects missing authentication configuration before creating a system")]
    [InlineData(false)]
    [InlineData(true)]
    public void Should_RejectStartup_When_CredentialsMissing(bool includeCertificate)
    {
        var env = new Dictionary<string, string?> { [MultiNodeTransport.ModeVariable] = "artery-quic" };
        if (includeCertificate)
            env[MultiNodeTransport.CertificateVariable] = "unused.pfx";
        string? Lookup(string name) => env.GetValueOrDefault(name);
        var config = ConfigurationFactory.ParseString("akka.actor.provider = remote")
            .WithFallback(MultiNodeTransport.Defaults("127.0.0.1", 0, Lookup));
        var exception = Assert.Throws<ConfigurationException>(() => MultiNodeTransport.CreateSystem("MissingCredentials", config, Lookup));
        exception.Message.Should().Contain(includeCertificate ? MultiNodeTransport.PinsVariable : MultiNodeTransport.CertificateVariable);
    }
}
