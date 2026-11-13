// -----------------------------------------------------------------------
//  <copyright file="RemoteCustomValidatorSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Remote.Transport.DotNetty;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Remote.Hosting.Tests;

/// <summary>
/// <see cref="SslOptions.CustomValidator"/> must apply no matter how the certificate is configured.
/// </summary>
public class RemoteCustomValidatorSpecs
{
    private static readonly string CertPath = Path.Combine(AppContext.BaseDirectory, "Resources", "akka-validcert.pfx");
    private const string CertPassword = "password";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private sealed class EchoActor : ReceiveActor
    {
        public EchoActor()
        {
            ReceiveAny(msg => Sender.Tell(msg));
        }
    }

    private static CertificateValidationCallback Validator(ConcurrentQueue<string> calls, bool verdict)
        => (_, _, peer, _, _) =>
        {
            calls.Enqueue(peer);
            return verdict;
        };

    private static AkkaConfigurationBuilder BuildWith(Action<RemoteOptions> configure)
    {
        var builder = new AkkaConfigurationBuilder(new ServiceCollection(), "test");
        builder.WithRemoting(configure);
        return builder;
    }

    private static SslCertificateOptions FileCertificate()
        => new() { Path = CertPath, Password = CertPassword };

    private static async Task<(IHost Host, ActorSystem System)> StartNodeAsync(string name, SslOptions ssl)
    {
        var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddAkka(name, (builder, _) =>
            {
                builder.WithRemoting(options =>
                {
                    options.HostName = "127.0.0.1";
                    options.Port = 0;
                    options.EnableSsl = true;
                    options.Ssl = ssl;
                });
            });
        }).Build();

        await host.StartAsync();
        var system = host.Services.GetRequiredService<ActorSystem>();
        system.ActorOf(Props.Create(() => new EchoActor()), "echo");
        return (host, system);
    }

    private static async Task StopAsync(params IHost?[] hosts)
    {
        foreach (var host in hosts)
        {
            if (host is null)
                continue;
            try
            {
                await host.StopAsync(Timeout);
            }
            finally
            {
                host.Dispose();
            }
        }
    }

    private static Task<IActorRef> ResolveEchoAsync(ActorSystem from, ActorSystem to)
    {
        var address = ((ExtendedActorSystem)to).Provider.DefaultAddress;
        return from.ActorSelection(new RootActorPath(address) / "user" / "echo")
            .ResolveOne(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Runs a real TLS handshake between two nodes. The server accepts everything; the client uses
    /// <paramref name="clientVerdict"/>.
    /// </summary>
    private static async Task RunHandshakeAsync(
        Func<CertificateValidationCallback, SslOptions> sslFactory,
        bool clientVerdict)
    {
        var serverCalls = new ConcurrentQueue<string>();
        var clientCalls = new ConcurrentQueue<string>();
        IHost? server = null;
        IHost? client = null;

        try
        {
            ActorSystem serverSystem, clientSystem;
            (server, serverSystem) = await StartNodeAsync("server", sslFactory(Validator(serverCalls, true)));
            (client, clientSystem) = await StartNodeAsync("client", sslFactory(Validator(clientCalls, clientVerdict)));

            if (clientVerdict)
            {
                var echo = await ResolveEchoAsync(clientSystem, serverSystem);
                var reply = await echo.Ask<string>("hello", Timeout);

                reply.Should().Be("hello");
                clientCalls.Should().NotBeEmpty("the client must run the validator on the server certificate");
                serverCalls.Should().NotBeEmpty("the server must run the validator on the client certificate");
            }
            else
            {
                // A rejected handshake on the dialing side shuts the client ActorSystem down.
                await Assert.ThrowsAnyAsync<Exception>(() => ResolveEchoAsync(clientSystem, serverSystem));
                await clientSystem.WhenTerminated.WaitAsync(Timeout);

                clientCalls.Should().NotBeEmpty("the client must run the validator on the server certificate");
            }
        }
        finally
        {
            await StopAsync(client, server);
        }
    }

    [Fact(DisplayName = "Should_ThrowConfigurationException_When_CustomValidatorHasNoCertificate")]
    public void Should_ThrowConfigurationException_When_CustomValidatorHasNoCertificate()
    {
        var act = () => BuildWith(o =>
        {
            o.EnableSsl = true;
            o.Ssl.CustomValidator = CertificateValidation.ValidateChain();
        });

        act.Should().Throw<ConfigurationException>()
            .Which.Message.Should().Contain("CustomValidator")
            .And.Contain("X509Certificate")
            .And.Contain("CertificateOptions");
    }

    [Fact(DisplayName = "Should_ThrowConfigurationException_When_CustomValidatorAndThumbprintModeHasNoThumbprint")]
    public void Should_ThrowConfigurationException_When_CustomValidatorAndThumbprintModeHasNoThumbprint()
    {
        var act = () => BuildWith(o =>
        {
            o.EnableSsl = true;
            o.Ssl.CustomValidator = CertificateValidation.ValidateChain();
            o.Ssl.CertificateOptions.UseThumbprintOverFile = true;
            o.Ssl.CertificateOptions.Path = CertPath; // ignored in thumbprint mode, as in the transport
        });

        act.Should().Throw<ConfigurationException>().Which.Message.Should().Contain("CustomValidator");
    }

    [Fact(DisplayName = "Should_RegisterSslSetupWithLoadedCertificate_When_FilePathAndCustomValidator")]
    public void Should_RegisterSslSetupWithLoadedCertificate_When_FilePathAndCustomValidator()
    {
        var validator = CertificateValidation.ValidateChain();

        var builder = BuildWith(o =>
        {
            o.EnableSsl = true;
            o.Ssl.SuppressValidation = true;
            o.Ssl.RequireMutualAuthentication = false;
            o.Ssl.ValidateCertificateHostname = true;
            o.Ssl.CustomValidator = validator;
            o.Ssl.CertificateOptions = FileCertificate();
        });

        var setup = builder.Setups.OfType<DotNettySslSetup>().Single();
        setup.Certificate.HasPrivateKey.Should().BeTrue();
        setup.Certificate.Thumbprint.Should()
            .Be(X509CertificateLoader.LoadPkcs12FromFile(CertPath, CertPassword).Thumbprint);
        setup.SuppressValidation.Should().BeTrue();
        setup.RequireMutualAuthentication.Should().BeFalse();
        setup.ValidateCertificateHostname.Should().BeTrue();
        setup.CustomValidator.Should().BeSameAs(validator);

        // the setup overrides HOCON, so the certificate settings must not be emitted as well
        builder.Configuration.Value.GetString("akka.remote.dot-netty.tcp.ssl.certificate.path")
            .Should().BeNullOrEmpty();
    }

    [Fact(DisplayName = "Should_EmitHoconAndNoSslSetup_When_FilePathWithoutCustomValidator")]
    public void Should_EmitHoconAndNoSslSetup_When_FilePathWithoutCustomValidator()
    {
        var builder = BuildWith(o =>
        {
            o.EnableSsl = true;
            o.Ssl.CertificateOptions = FileCertificate();
        });

        builder.Setups.OfType<DotNettySslSetup>().Should().BeEmpty();
        var certificate = builder.Configuration.Value.GetConfig("akka.remote.dot-netty.tcp.ssl.certificate");
        certificate.GetString("path").Should().Be(CertPath);
        certificate.GetString("password").Should().Be(CertPassword);
    }

    [Fact(DisplayName = "Should_UseValidatorVerdict_When_FilePathCertificateAndValidatorAccepts")]
    public Task Should_UseValidatorVerdict_When_FilePathCertificateAndValidatorAccepts()
        => RunHandshakeAsync(validator => new SslOptions
        {
            CustomValidator = validator,
            CertificateOptions = FileCertificate()
        }, clientVerdict: true);

    [Fact(DisplayName = "Should_FailHandshake_When_FilePathCertificateAndValidatorRejects")]
    public Task Should_FailHandshake_When_FilePathCertificateAndValidatorRejects()
        => RunHandshakeAsync(validator => new SslOptions
        {
            CustomValidator = validator,
            CertificateOptions = FileCertificate()
        }, clientVerdict: false);

    // Like the transport's own thumbprint test, this one needs a writable certificate store and
    // is skipped where XUNIT_SKIP_LOCAL_FACT=true (CI).
    private static void SkipWhenStoreUnavailable()
        => Assert.SkipWhen(
            string.Equals(Environment.GetEnvironmentVariable("XUNIT_SKIP_LOCAL_FACT"), "true",
                StringComparison.OrdinalIgnoreCase),
            "Certificate store tests are skipped when XUNIT_SKIP_LOCAL_FACT is true");

    private static async Task WithStoredCertificateAsync(Func<string, Task> test)
    {
        var certificate = X509CertificateLoader.LoadPkcs12FromFile(CertPath, CertPassword);
        using var store = new X509Store("My", StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);

        // never remove a certificate this test did not add
        var added = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false).Count == 0;
        if (added)
            store.Add(certificate);

        try
        {
            await test(certificate.Thumbprint);
        }
        finally
        {
            if (added)
            {
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, certificate.Thumbprint, false);
                foreach (var cert in found)
                    store.Remove(cert);
            }
        }
    }

    private static SslCertificateOptions ThumbprintCertificate(string thumbprint)
        => new()
        {
            // a bogus path proves that the thumbprint wins when UseThumbprintOverFile is set
            Path = Path.Combine(AppContext.BaseDirectory, "does-not-exist.pfx"),
            UseThumbprintOverFile = true,
            Thumbprint = thumbprint,
            StoreName = "My",
            StoreLocation = "current-user"
        };

    [Fact(DisplayName = "Should_UseValidatorVerdict_When_ThumbprintCertificateAndValidatorAccepts")]
    public async Task Should_UseValidatorVerdict_When_ThumbprintCertificateAndValidatorAccepts()
    {
        SkipWhenStoreUnavailable();
        await WithStoredCertificateAsync(thumbprint => RunHandshakeAsync(validator => new SslOptions
        {
            SuppressValidation = true, // self-signed test certificate; the validator decides
            CustomValidator = validator,
            CertificateOptions = ThumbprintCertificate(thumbprint)
        }, clientVerdict: true));
    }

    [Fact(DisplayName = "Should_FailHandshake_When_ThumbprintCertificateAndValidatorRejects")]
    public async Task Should_FailHandshake_When_ThumbprintCertificateAndValidatorRejects()
    {
        SkipWhenStoreUnavailable();
        await WithStoredCertificateAsync(thumbprint => RunHandshakeAsync(validator => new SslOptions
        {
            SuppressValidation = true,
            CustomValidator = validator,
            CertificateOptions = ThumbprintCertificate(thumbprint)
        }, clientVerdict: false));
    }

    [Fact(DisplayName = "Should_ThrowArgumentException_When_CustomValidatorAndThumbprintNotInStore")]
    public void Should_ThrowArgumentException_When_CustomValidatorAndThumbprintNotInStore()
    {
        SkipWhenStoreUnavailable();

        var act = () => BuildWith(o =>
        {
            o.EnableSsl = true;
            o.Ssl.SuppressValidation = true;
            o.Ssl.CustomValidator = CertificateValidation.ValidateChain();
            o.Ssl.CertificateOptions = ThumbprintCertificate("0000000000000000000000000000000000000000");
        });

        // same exception type the transport throws for a missing thumbprint
        act.Should().Throw<ArgumentException>();
    }
}
