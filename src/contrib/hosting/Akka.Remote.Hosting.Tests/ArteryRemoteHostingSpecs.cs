// -----------------------------------------------------------------------
// <copyright file="ArteryRemoteHostingSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Remote.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Remote.Hosting.Tests
{
    public class ArteryRemoteHostingSpecs
    {
        [Fact(DisplayName = "WithArteryRemoting enables Artery and maps explicitly supplied address values")]
        public void Should_EnableArteryAndMapExplicitHostAndPort()
        {
            var builder = CreateBuilder();

            builder.WithArteryRemoting(options =>
            {
                options.HostName = "localhost";
                options.Port = 25520;
            });

            var config = builder.Configuration.GetOrElse(Config.Empty);
            config.GetBoolean("akka.remote.artery.enabled").Should().BeTrue();
            config.GetString("akka.remote.artery.canonical.hostname").Should().Be("localhost");
            config.GetInt("akka.remote.artery.canonical.port").Should().Be(25520);
            builder.ActorRefProvider.Value.Should().Be(ProviderSelection.Remote.Instance);
        }

        [Fact(DisplayName = "WithArteryRemoting preserves configured address values when options omit them")]
        public void Should_PreserveConfiguredAddressWhenOptionsOmitValues()
        {
            var builder = CreateBuilder();
            builder.AddHocon(ConfigurationFactory.ParseString("""
                akka.remote.artery.canonical.hostname = "configured-host"
                akka.remote.artery.canonical.port = 12345
                """), HoconAddMode.Append);

            builder.WithArteryRemoting(_ => { });

            var config = builder.Configuration.GetOrElse(Config.Empty);
            config.GetBoolean("akka.remote.artery.enabled").Should().BeTrue();
            config.GetString("akka.remote.artery.canonical.hostname").Should().Be("configured-host");
            config.GetInt("akka.remote.artery.canonical.port").Should().Be(12345);
        }

        [Fact(DisplayName = "WithArteryRemoting keeps an existing Cluster provider")]
        public void Should_PreserveClusterProvider()
        {
            var builder = CreateBuilder();
            builder.AddSetup(BootstrapSetup.Create().WithActorRefProvider(ProviderSelection.Cluster.Instance));

            builder.WithArteryRemoting(_ => { });

            builder.ActorRefProvider.Value.Should().Be(ProviderSelection.Cluster.Instance);
        }

        [Theory(DisplayName = "WithArteryRemoting preserves a Cluster or Remote provider configured through HOCON")]
        [InlineData("cluster")]
        [InlineData("Akka.Cluster.ClusterActorRefProvider, Akka.Cluster")]
        [InlineData("remote")]
        [InlineData("Akka.Remote.RemoteActorRefProvider, Akka.Remote")]
        [InlineData("Custom.ActorRefProvider, Custom.Assembly")]
        public void Should_PreserveHoconConfiguredProvider(string provider)
        {
            var builder = CreateBuilder();
            builder.AddHocon(ConfigurationFactory.ParseString($"akka.actor.provider = \"{provider}\""),
                HoconAddMode.Append);

            builder.WithArteryRemoting(_ => { });

            builder.ActorRefProvider.HasValue.Should().BeFalse("HOCON remains the provider source when no typed provider was selected");
            builder.Configuration.GetOrElse(Config.Empty).GetString("akka.actor.provider").Should().Be(provider);
        }

        [Fact(DisplayName = "WithArteryRemoting replaces a HOCON local provider with the Remote provider")]
        public void Should_ReplaceHoconLocalProvider()
        {
            var builder = CreateBuilder();
            builder.AddHocon(ConfigurationFactory.ParseString("akka.actor.provider = local"), HoconAddMode.Append);

            builder.WithArteryRemoting(_ => { });

            builder.ActorRefProvider.Value.Should().Be(ProviderSelection.Remote.Instance);
        }

        [Fact(DisplayName = "WithArteryRemoting leaves a directly supplied TLS setup when TLS is omitted")]
        public void Should_KeepDirectTlsSetupWhenHostingTlsIsNull()
        {
            using var certificate = CreateCertificate();
            var directSetup = new ArteryTlsSetup(ArteryTlsSettings.ServerOnly(certificate, AcceptAllPolicy()));
            var builder = CreateBuilder();
            builder.AddSetup(directSetup);

            builder.WithArteryRemoting(_ => { });

            builder.Setups.Should().ContainSingle(setup => ReferenceEquals(setup, directSetup));
        }

        [Fact(DisplayName = "WithArteryRemoting replaces a direct TLS setup when a Hosting profile is supplied")]
        public void Should_ReplaceDirectTlsSetupWhenHostingTlsIsSet()
        {
            using var directCertificate = CreateCertificate();
            using var hostingCertificate = CreateCertificate();
            var directSetup = new ArteryTlsSetup(ArteryTlsSettings.ServerOnly(directCertificate, AcceptAllPolicy()));
            var hostedProfile = ArteryTlsSettings.ServerOnly(hostingCertificate, AcceptAllPolicy());
            var builder = CreateBuilder();
            builder.AddSetup(directSetup);

            builder.WithArteryRemoting(options => options.Tls = hostedProfile);

            var setup = builder.Setups.Should().ContainSingle(item => item is ArteryTlsSetup)
                .Which.Should().BeOfType<ArteryTlsSetup>().Subject;
            setup.Settings.Should().BeSameAs(hostedProfile);
        }

        private static AkkaConfigurationBuilder CreateBuilder() =>
            new(new ServiceCollection(), "ArteryHostingTest");

        private static TlsPeerPolicy AcceptAllPolicy() =>
            TlsPeerPolicy.CustomTrust((_, _, _, _, _) => true);

        private static X509Certificate2 CreateCertificate()
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=artery-hosting-test", key, HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, critical: true));
            return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
        }
    }
}
