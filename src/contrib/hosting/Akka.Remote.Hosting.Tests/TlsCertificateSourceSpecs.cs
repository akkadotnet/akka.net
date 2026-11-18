// -----------------------------------------------------------------------
//  <copyright file="TlsCertificateSourceSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.IO;
using Akka.Remote.Transport.DotNetty;
using Akka.Tests.IO;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Remote.Hosting.Tests;

public sealed class TlsCertificateSourceSpecs
{
    private static readonly CertificateValidationCallback AcceptAll = (_, _, _, _, _) => true;

    private static X509Certificate2 CreateCertificate(string commonName = "akka-test") =>
        TlsTestCertificates.Create(commonName, -1440, 43200, true, TlsTestCertificates.ServerAuth,
            TlsTestCertificates.ClientAuth);

    private static AkkaConfigurationBuilder Configure(RemoteOptions options)
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "test");
        builder.WithRemoting(options);
        return builder;
    }

    private static AkkaConfigurationBuilder Configure(SslOptions ssl, bool enableSsl = true) =>
        Configure(new RemoteOptions { EnableSsl = enableSsl, Ssl = ssl });

    private static void WithTempFolder(Action<string> body)
    {
        var folder = Directory.CreateTempSubdirectory("akka-tls-folder-");
        try
        {
            body(folder.FullName);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    private static DotNettySslSetup? FindSetup(AkkaConfigurationBuilder builder) =>
        builder.Setups.OfType<DotNettySslSetup>().SingleOrDefault();

    [Fact(DisplayName = "Should_AddSourceBackedSetupAndNoCertificateHocon_When_CertificateSourceIsSet")]
    public void Should_AddSourceBackedSetupAndNoCertificateHocon_When_CertificateSourceIsSet()
    {
        using var certificate = CreateCertificate();
        var builder = Configure(new SslOptions
        {
            SuppressValidation = true,
            RequireMutualAuthentication = false,
            ValidateCertificateHostname = true,
            CertificateSource = TlsCertificateSource.FromCallback(() => certificate),
            CustomValidator = AcceptAll,
            // Ignored, as it is when an X509Certificate is set.
            CertificateOptions = new SslCertificateOptions { CommonName = "ignored", Path = "ignored" }
        });

        var setup = FindSetup(builder);
        setup.Should().NotBeNull();
        setup!.Certificate.Thumbprint.Should().Be(certificate.Thumbprint);
        setup.SuppressValidation.Should().BeTrue();
        setup.RequireMutualAuthentication.Should().BeFalse();
        setup.ValidateCertificateHostname.Should().BeTrue();
        setup.CustomValidator.Should().BeSameAs(AcceptAll);

        builder.Configuration.Value.HasPath("akka.remote.dot-netty.tcp.ssl").Should().BeFalse();
    }

    [Fact(DisplayName = "Should_UseTransportDefaults_When_CertificateSourceIsSetWithoutFlags")]
    public void Should_UseTransportDefaults_When_CertificateSourceIsSetWithoutFlags()
    {
        using var certificate = CreateCertificate();
        var builder = Configure(new SslOptions { CertificateSource = TlsCertificateSource.FromCallback(() => certificate) });

        var setup = FindSetup(builder)!;
        setup.SuppressValidation.Should().BeFalse();
        setup.RequireMutualAuthentication.Should().BeTrue();
        setup.ValidateCertificateHostname.Should().BeFalse();
        setup.CustomValidator.Should().BeNull();
    }

    [Fact(DisplayName = "Should_AddNoSetup_When_CertificateSourceIsSetAndSslIsDisabled")]
    public void Should_AddNoSetup_When_CertificateSourceIsSetAndSslIsDisabled()
    {
        var builder = Configure(new SslOptions
        {
            CertificateSource = TlsCertificateSource.FromFolder("some-folder", null),
            CertificateOptions = new SslCertificateOptions { CommonName = "x" }
        }, enableSsl: false);

        FindSetup(builder).Should().BeNull();
        builder.Configuration.Value.HasPath("akka.remote.dot-netty.tcp.ssl").Should().BeFalse();
    }

    [Fact(DisplayName = "Should_TurnSslOnAndUseTheSource_When_CertificateSourceIsSetAndEnableSslIsNotSet")]
    public void Should_TurnSslOnAndUseTheSource_When_CertificateSourceIsSetAndEnableSslIsNotSet()
    {
        using var certificate = CreateCertificate();

        // A source turns TLS on, so an application that enables TLS in its own HOCON, or not at all, still uses it.
        var builder = Configure(new RemoteOptions
        {
            Ssl = new SslOptions { CertificateSource = TlsCertificateSource.FromCallback(() => certificate) }
        });

        var setup = FindSetup(builder);
        setup.Should().NotBeNull();
        setup!.Certificate.Thumbprint.Should().Be(certificate.Thumbprint);
        builder.Configuration.Value.GetBoolean("akka.remote.dot-netty.tcp.enable-ssl").Should().BeTrue();
        builder.Configuration.Value.HasPath("akka.remote.dot-netty.tcp.ssl").Should().BeFalse();
    }

    [Fact(DisplayName = "Should_Throw_When_BothX509CertificateAndCertificateSourceAreSet")]
    public void Should_Throw_When_BothX509CertificateAndCertificateSourceAreSet()
    {
        using var certificate = CreateCertificate();

        var act = () => Configure(new SslOptions
        {
            X509Certificate = certificate,
            CertificateSource = TlsCertificateSource.FromCallback(() => certificate)
        });

        act.Should().Throw<Akka.Configuration.ConfigurationException>()
            .WithMessage("*X509Certificate*CertificateSource*one of them*");
    }

    [Theory(DisplayName = "Should_EmitCommonNameStoreNameAndStoreLocationHocon_When_CommonNameIsSetWithoutValidator")]
    [InlineData("My", "current-user")]
    [InlineData("Root", "local-machine")]
    public void Should_EmitCommonNameStoreNameAndStoreLocationHocon_When_CommonNameIsSetWithoutValidator(
        string storeName, string storeLocation)
    {
        var builder = Configure(new SslOptions
        {
            CertificateOptions = new SslCertificateOptions
            {
                CommonName = "node.example.com",
                StoreName = storeName,
                StoreLocation = storeLocation
            }
        });

        FindSetup(builder).Should().BeNull();
        var cert = builder.Configuration.Value.GetConfig("akka.remote.dot-netty.tcp.ssl.certificate");
        cert.GetString("common-name").Should().Be("node.example.com");
        cert.GetString("store-name").Should().Be(storeName);
        cert.GetString("store-location").Should().Be(storeLocation);
    }

    [Theory(DisplayName = "Should_AddSourceBackedSetupWithValidator_When_CommonNameIsSetWithCustomValidator")]
    [InlineData(null, null)]
    [InlineData("My", "current-user")]
    [InlineData("Root", "local-machine")]
    public void Should_AddSourceBackedSetupWithValidator_When_CommonNameIsSetWithCustomValidator(
        string? storeName, string? storeLocation)
    {
        var builder = Configure(new SslOptions
        {
            SuppressValidation = false,
            RequireMutualAuthentication = true,
            ValidateCertificateHostname = true,
            CustomValidator = AcceptAll,
            CertificateOptions = new SslCertificateOptions
            {
                CommonName = $"no-such-certificate-{Guid.NewGuid():N}",
                StoreName = storeName,
                StoreLocation = storeLocation
            }
        });

        var setup = FindSetup(builder);
        setup.Should().NotBeNull();
        setup!.CustomValidator.Should().BeSameAs(AcceptAll);
        setup.RequireMutualAuthentication.Should().BeTrue();
        setup.ValidateCertificateHostname.Should().BeTrue();

        // A source looks the certificate up when asked and finds none in the store. A setup built
        // from a fixed certificate could not fail here, so this shows the setup is source-backed.
        var read = () => setup.Certificate;
        read.Should().Throw<InvalidOperationException>();

        builder.Configuration.Value.HasPath("akka.remote.dot-netty.tcp.ssl").Should().BeFalse();
    }

    [Fact(DisplayName = "Should_Throw_When_CommonNameWithCustomValidatorHasUnknownStoreLocation")]
    public void Should_Throw_When_CommonNameWithCustomValidatorHasUnknownStoreLocation()
    {
        var act = () => Configure(new SslOptions
        {
            CustomValidator = AcceptAll,
            CertificateOptions = new SslCertificateOptions { CommonName = "x", StoreLocation = "nowhere" }
        });

        act.Should().Throw<ArgumentException>().WithMessage("*local-machine | current-user*");
    }

    [Fact(DisplayName = "Should_AddSourceBackedSetupWithValidator_When_FolderPathIsSetWithCustomValidator")]
    public async Task Should_AddSourceBackedSetupWithValidator_When_FolderPathIsSetWithCustomValidator()
    {
        using var certificate = CreateCertificate();
        var folder = Directory.CreateTempSubdirectory("akka-tls-folder-");
        try
        {
            File.WriteAllBytes(Path.Combine(folder.FullName, "node.pfx"), certificate.Export(X509ContentType.Pfx, "pw"));

            var builder = Configure(new SslOptions
            {
                RequireMutualAuthentication = true,
                CustomValidator = AcceptAll,
                CertificateOptions = new SslCertificateOptions { Path = folder.FullName, Password = "pw" }
            });

            var setup = FindSetup(builder);
            setup.Should().NotBeNull();
            setup!.CustomValidator.Should().BeSameAs(AcceptAll);
            builder.Configuration.Value.HasPath("akka.remote.dot-netty.tcp.ssl").Should().BeFalse();

            // Reading the certificate never waits: the first read starts the source's first look-up, and a read after
            // it finishes returns the pick.
            var read = () => setup.Certificate;
            read.Should().Throw<InvalidOperationException>().WithMessage("No TLS certificate is available yet*");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            X509Certificate2? picked = null;
            while (picked is null && DateTime.UtcNow < deadline)
            {
                try
                {
                    picked = setup.Certificate;
                }
                catch (InvalidOperationException)
                {
                    await Task.Delay(20);
                }
            }

            picked.Should().NotBeNull("the source's first look-up must finish");
            picked!.Thumbprint.Should().Be(certificate.Thumbprint);
        }
        finally
        {
            folder.Delete(true);
        }
    }

    [Fact(DisplayName = "Should_EmitPathHoconAndNoSetup_When_FolderPathIsSetWithoutValidator")]
    public void Should_EmitPathHoconAndNoSetup_When_FolderPathIsSetWithoutValidator()
    {
        WithTempFolder(folder =>
        {
            var builder = Configure(new SslOptions { CertificateOptions = new SslCertificateOptions { Path = folder } });

            FindSetup(builder).Should().BeNull();
            builder.Configuration.Value.GetString("akka.remote.dot-netty.tcp.ssl.certificate.path")
                .Should().Be(folder);
        });
    }

    [Fact(DisplayName = "Should_ExchangeMessage_When_TwoHostedSystemsUseCertificateSourceWithMutualTls")]
    public async Task Should_ExchangeMessage_When_TwoHostedSystemsUseCertificateSourceWithMutualTls()
    {
        using var certificate = CreateCertificate();
        var thumbprint = certificate.Thumbprint;

        // The certificate is self-signed, so the validator accepts exactly this one.
        CertificateValidationCallback validator = (cert, _, _, _, _) => cert?.Thumbprint == thumbprint;

        IHost CreateHost(string name, Action<AkkaConfigurationBuilder>? more = null) =>
            new HostBuilder().ConfigureServices(services =>
            {
                services.AddAkka(name, (builder, _) =>
                {
                    builder.WithRemoting(new RemoteOptions
                    {
                        HostName = "127.0.0.1",
                        Port = 0,
                        EnableSsl = true,
                        Ssl = new SslOptions
                        {
                            RequireMutualAuthentication = true,
                            CertificateSource = TlsCertificateSource.FromCallback(() => certificate),
                            CustomValidator = validator
                        }
                    });
                    more?.Invoke(builder);
                });
            }).Build();

        using var server = CreateHost("tls-server", b => b.WithActors((system, _, _) =>
            system.ActorOf(Props.Create(() => new EchoActor()), "echo")));
        using var client = CreateHost("tls-client");

        await server.StartAsync();
        await client.StartAsync();

        var serverSystem = (ExtendedActorSystem)server.Services.GetRequiredService<ActorSystem>();
        var clientSystem = client.Services.GetRequiredService<ActorSystem>();
        var address = serverSystem.Provider.DefaultAddress;

        var echo = await clientSystem.ActorSelection($"{address}/user/echo")
            .Ask<string>("ping", TimeSpan.FromSeconds(20));

        echo.Should().Be("echo:ping");

        await server.StopAsync();
        await client.StopAsync();
    }

    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor()
        {
            Receive<string>(s => Sender.Tell("echo:" + s));
        }
    }
}
