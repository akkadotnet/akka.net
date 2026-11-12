// -----------------------------------------------------------------------
// <copyright file="MultiNodeTransportNativeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.TestKit.Tests;

public sealed class MultiNodeTransportNativeSpec
{
    private static void RequireNative()
    {
        if (!QuicConnection.IsSupported)
            Assert.Skip("Native QUIC unavailable; this skip is not QUIC qualification evidence.");
    }

    [Theory(DisplayName = "Multi-node QUIC factory authenticates actor traffic with independently owned credentials")]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Should_DeliverAuthenticatedTraffic_When_FactoryCreatesBothPeers(int streams)
    {
        RequireNative();
        using var identity = new Identity();
        ActorSystem? first = null;
        ActorSystem? second = null;
        try
        {
            first = MultiNodeTransport.CreateSystem("FactoryA", identity.Config(streams), identity.Lookup);
            second = MultiNodeTransport.CreateSystem("FactoryB", identity.Config(streams), identity.Lookup);
            ((ArteryRemoting)RARP.For(first).Provider.Transport).UsesQuicTransport.Should().BeTrue();
            var firstCertificate = first.Settings.Setup.Get<ArteryTlsSetup>().Value.Settings.ClientSettings.Certificate!;
            var secondCertificate = second.Settings.Setup.Get<ArteryTlsSetup>().Value.Settings.ClientSettings.Certificate!;
            firstCertificate.Should().NotBeSameAs(secondCertificate);
            second.ActorOf(Props.Create(() => new Echo()), "echo");
            var address = RARP.For(second).Provider.DefaultAddress;
            var echo = await first.ActorSelection($"{address}/user/echo").ResolveOne(TimeSpan.FromSeconds(10));
            var bytes = Enumerable.Range(0, 65537).Select(i => (byte)(i * 31)).ToArray();
            (await echo.Ask<byte[]>(bytes, TimeSpan.FromSeconds(10))).Should().Equal(bytes);
            await first.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
            using var key = secondCertificate.GetRSAPrivateKey();
            key.Should().NotBeNull("terminating another role must not dispose this role's certificate");
        }
        finally
        {
            if (first is not null) await first.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
            if (second is not null) await second.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Theory(DisplayName = "Multi-node QUIC rejects peer pin and hostname mismatches")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RejectPeer_When_PinOrHostnameDoesNotMatch(bool wrongHostname)
    {
        RequireNative();
        using var identity = new Identity(wrongHostname);
        ActorSystem? first = null;
        ActorSystem? second = null;
        try
        {
            string? ClientEnvironment(string name) => !wrongHostname && name == MultiNodeTransport.PinsVariable
                ? new string('F', 40) : identity.Lookup(name);
            first = MultiNodeTransport.CreateSystem("RejectedFactoryA", identity.Config(1), ClientEnvironment);
            second = MultiNodeTransport.CreateSystem("RejectedFactoryB", identity.Config(1), identity.Lookup);
            second.ActorOf(Props.Create(() => new Echo()), "echo");
            var address = RARP.For(second).Provider.DefaultAddress;
            await Assert.ThrowsAsync<ActorNotFoundException>(() =>
                first.ActorSelection($"{address}/user/echo").ResolveOne(TimeSpan.FromSeconds(2)));
            ((ArteryRemoting)RARP.For(first).Provider.Transport).Registry.AssociationFor(address)
                .CurrentState.UniqueRemoteAddress.Should().BeNull("authentication failure must not create an admitted peer");
        }
        finally
        {
            if (first is not null) await first.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
            if (second is not null) await second.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact(DisplayName = "Multi-node QUIC credentials survive failed startup until asynchronous teardown finishes")]
    public async Task Should_RetainCertificate_When_StartupFailsAfterNativeTransportStarts()
    {
        RequireNative();
        using var identity = new Identity();
        var failure = new FailingExtensionId();
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(identity.Config(1)), ExtensionsSetup.Create(failure));
        try
        {
            Assert.ThrowsAny<Exception>(() => MultiNodeTransport.CreateSystem("FailedFactory", setup, identity.Lookup));
            var system = failure.System ?? throw new InvalidOperationException("Startup did not reach the controlled failure.");
            var certificate = system.Settings.Setup.Get<ArteryTlsSetup>().Value.Settings.ClientSettings.Certificate!;
            system.WhenTerminated.IsCompleted.Should().BeFalse("the controlled shutdown task still holds teardown");
            using (var key = certificate.GetRSAPrivateKey())
                key.Should().NotBeNull("native cleanup still owns the certificate after the factory throws");
            failure.Release.TrySetResult(Akka.Done.Instance);
            await system.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.ThrowsAny<Exception>(() => certificate.GetRSAPrivateKey());
        }
        finally
        {
            failure.Release.TrySetResult(Akka.Done.Instance);
            if (failure.System is not null)
                await failure.System.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact(DisplayName = "TestKit setup-based creation uses the overridable factory and preserves setup entries")]
    public async Task Should_PreserveSetup_When_TestKitFactoryCreatesQuicSystem()
    {
        RequireNative();
        using var identity = new Identity();
        var marker = new EnvironmentSetup(identity.Lookup);
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(identity.Config(1)), marker);
        var probe = new FactoryProbe(setup);
        try
        {
            probe.Sys.Settings.Setup.Get<EnvironmentSetup>().Value.Should().BeSameAs(marker);
            probe.Sys.Settings.Setup.Get<ArteryTlsSetup>().HasValue.Should().BeTrue();
            ((ArteryRemoting)RARP.For(probe.Sys).Provider.Transport).UsesQuicTransport.Should().BeTrue();
        }
        finally
        {
            await probe.Sys.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    [Fact(DisplayName = "Multi-node factory leaves caller-supplied TLS credentials owned by the caller")]
    public async Task Should_PreserveCallerOwnership_When_TlsSetupIsSupplied()
    {
        RequireNative();
        using var identity = new Identity();
        using var certificate = TlsCertificateLoader.LoadPkcs12FromFile(
            identity.Lookup(MultiNodeTransport.CertificateVariable)!, null, X509KeyStorageFlags.EphemeralKeySet);
        var pin = TlsPeerPolicy.PinnedCertificates([certificate.Thumbprint]);
        var tls = new ArteryTlsSetup(ArteryTlsSettings.Mutual(certificate, pin,
            pin.And(TlsCertificateValidation.ValidateHostname())));
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(identity.Config(1)), tls);
        // No credential environment is available: typed setup must be sufficient.
        var system = MultiNodeTransport.CreateSystem("CallerOwnedFactory", setup,
            name => name == MultiNodeTransport.ModeVariable ? "artery-quic" : null);
        try
        {
            system.Settings.Setup.Get<ArteryTlsSetup>().Value.Should().BeSameAs(tls);
        }
        finally
        {
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
        using var key = certificate.GetRSAPrivateKey();
        key.Should().NotBeNull("the factory must not dispose caller-supplied credentials");
    }

    private sealed class Echo : ReceiveActor
    {
        public Echo() => Receive<byte[]>(bytes => Sender.Tell(bytes));
    }

    private sealed class EnvironmentSetup(Func<string, string?> lookup) : Setup
    {
        internal Func<string, string?> Lookup { get; } = lookup;
    }

    private sealed class FactoryProbe(ActorSystemSetup setup) : TestKitBase(new XunitAssertions(), setup)
    {
        protected override ActorSystem CreateActorSystem(string name, ActorSystemSetup completeSetup) =>
            MultiNodeTransport.CreateSystem(name, completeSetup, completeSetup.Get<EnvironmentSetup>().Value.Lookup);
    }

    private sealed class FailureExtension : IExtension;

    private sealed class FailingExtensionId : ExtensionIdProvider<FailureExtension>
    {
        internal ActorSystem? System { get; private set; }
        internal TaskCompletionSource<Akka.Done> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override FailureExtension CreateExtension(ExtendedActorSystem system)
        {
            System = system;
            CoordinatedShutdown.Get(system).AddTask(CoordinatedShutdown.PhaseBeforeServiceUnbind, "hold-native-cleanup", () => Release.Task);
            throw new InvalidOperationException("Controlled failure after native transport startup.");
        }
    }

    private sealed class Identity : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("akka-mntr-quic-");
        private readonly Dictionary<string, string?> _values;
        internal Identity(bool wrongHostname = false)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName(wrongHostname ? "wrong.invalid" : "localhost");
            if (!wrongHostname) names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var path = Path.Combine(_directory.FullName, "identity.pfx");
            File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx));
            _values = new Dictionary<string, string?>
            {
                [MultiNodeTransport.ModeVariable] = "artery-quic",
                [MultiNodeTransport.CertificateVariable] = path,
                [MultiNodeTransport.PinsVariable] = certificate.Thumbprint
            };
        }
        internal string? Lookup(string name) => _values.GetValueOrDefault(name);
        internal Config Config(int streams) => ConfigurationFactory.ParseString("akka.actor.provider = remote")
            .WithFallback(MultiNodeTransport.Defaults("127.0.0.1", 0, name => name == MultiNodeTransport.StreamsVariable ? streams.ToString() : Lookup(name)));
        public void Dispose() => _directory.Delete(recursive: true);
    }
}
