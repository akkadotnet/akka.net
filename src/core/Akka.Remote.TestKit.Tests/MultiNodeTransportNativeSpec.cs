// -----------------------------------------------------------------------
// <copyright file="MultiNodeTransportNativeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Quic;
using System.Net.Security;
using System.Runtime.Versioning;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Event;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Serialization;
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

    [Theory(DisplayName = "Factory certificate cleanup survives provider initialization failure before transport startup")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_DisposeOwnedCertificate_When_ProviderInitializationFailsBeforeTransportStarts(bool beforeGuardians)
    {
        RequireNative();
        using var identity = new Identity();
        ActorSystem? failed = null;
        X509Certificate2? certificate = null;
        var serialization = SerializationSetup.Create(system =>
        {
            failed = system;
            certificate = system.Settings.Setup.Get<ArteryTlsSetup>().Value.Settings.ClientSettings.Certificate!;
            if (beforeGuardians)
                throw new InvalidOperationException("Controlled serialization failure before guardian initialization.");
            return ImmutableHashSet<SerializerDetails>.Empty;
        });
        var invalid = ConfigurationFactory.ParseString("akka.remote.artery.advanced.outbound-lanes = 0")
            .WithFallback(identity.Config(1));
        var setup = ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(invalid), serialization);
        try
        {
            var failure = Assert.ThrowsAny<Exception>(() => MultiNodeTransport.CreateSystem("EarlyFailedFactory", setup, identity.Lookup));
            failure.ToString().Should().Contain(beforeGuardians ? "Controlled serialization failure" : "outbound-lanes");
            failed.Should().NotBeNull("the failure must reach the early provider initialization boundary");
            certificate.Should().NotBeNull();
            if (beforeGuardians)
                ((ExtendedActorSystem)failed!).Provider.RootGuardian.Should().BeNull("this case must precede any local actors");
            await AssertCertificateDisposedAsync(certificate!);
        }
        finally
        {
            if (failed is not null)
            {
                if (((ExtendedActorSystem)failed).Provider.RootGuardian is not null)
                {
                    ((ExtendedActorSystem)failed).Abort();
                    await failed.WhenTerminated.WaitAsync(TimeSpan.FromSeconds(15));
                }
                else if (failed.Scheduler is IDisposable scheduler)
                {
                    // No actors/native objects exist. Normal termination requires a guardian;
                    // close the scheduler explicitly, matching Start's failed-init fallback.
                    scheduler.Dispose();
                }
            }
        }
    }

    [Fact(DisplayName = "Factory credentials remain available to an enrolled native listener after actor-system termination")]
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("osx")]
    public async Task Should_RetainFactoryCertificate_When_AnEnrolledNativeListenerOutlivesTermination()
    {
        RequireNative();
        using var identity = new Identity();
        var system = MultiNodeTransport.CreateSystem("NativeOwnerFactory", identity.Config(1), identity.Lookup);
        var certificate = system.Settings.Setup.Get<ArteryTlsSetup>().Value.Settings.ClientSettings.Certificate!;
        var resources = system.Settings.Setup.Get<ArteryQuicResourceSetup>().Value.Lifetime;
        using var listenerOwner = resources.Acquire();
        QuicListener? listener = null;
        Task<QuicConnection>? accepting = null;
        var serverDisposed = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var protocol = new SslApplicationProtocol("akka-quic-native-lifetime-test");
            var policy = TlsPeerPolicy.PinnedCertificates(certificate.Thumbprint);
            var log = Logging.GetLogger(system, typeof(MultiNodeTransportNativeSpec));
            listener = await QuicListener.ListenAsync(new QuicListenerOptions
            {
                ListenEndPoint = new IPEndPoint(IPAddress.Loopback, 0),
                ApplicationProtocols = [protocol],
                ConnectionOptionsCallback = (_, _, _) =>
                {
                    var authentication = TlsServerSettings.Mutual(certificate, policy)
                        .CreateAuthenticationOptions("native-lifetime-test", log);
                    authentication.ApplicationProtocols = [protocol];
                    return ValueTask.FromResult(new QuicServerConnectionOptions
                    {
                        ServerAuthenticationOptions = authentication,
                        DefaultCloseErrorCode = 1,
                        DefaultStreamErrorCode = 1,
                        HandshakeTimeout = TimeSpan.FromSeconds(5)
                    });
                }
            }, deadline.Token);
            await system.Terminate().WaitAsync(deadline.Token);
            resources.WhenReleased.IsCompleted.Should().BeFalse("the controlled listener is still a real native owner");
            using (var key = certificate.GetRSAPrivateKey())
                key.Should().NotBeNull("the real factory termination callback must honor native ownership");

            using var clientCertificate = TlsCertificateLoader.LoadPkcs12FromFile(
                identity.Lookup(MultiNodeTransport.CertificateVariable)!, null,
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
            var clientAuthentication = TlsClientSettings.Mutual(clientCertificate, policy)
                .CreateAuthenticationOptions("127.0.0.1", "127.0.0.1", log);
            clientAuthentication.ApplicationProtocols = [protocol];
            accepting = listener.AcceptConnectionAsync(deadline.Token).AsTask();
            await using (var client = await QuicConnection.ConnectAsync(new QuicClientConnectionOptions
            {
                RemoteEndPoint = listener.LocalEndPoint,
                ClientAuthenticationOptions = clientAuthentication,
                DefaultCloseErrorCode = 1,
                DefaultStreamErrorCode = 1,
                HandshakeTimeout = TimeSpan.FromSeconds(5)
            }, deadline.Token))
            {
                var server = await accepting.WaitAsync(deadline.Token);
                client.NegotiatedApplicationProtocol.Should().Be(protocol);
                server.NegotiatedApplicationProtocol.Should().Be(protocol);
                await client.CloseAsync(1, deadline.Token);
                await server.DisposeAsync();
                serverDisposed = true;
            }
            await listener.DisposeAsync();
            listener = null;
            listenerOwner.Dispose();
            await resources.WhenReleased.WaitAsync(deadline.Token);
            await AssertCertificateDisposedAsync(certificate);
        }
        finally
        {
            deadline.Cancel();
            if (listener is not null)
                await listener.DisposeAsync();
            if (accepting is not null && !serverDisposed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var server = await accepting.WaitAsync(cleanup.Token);
                    await server.DisposeAsync();
                }
                catch (Exception exception) when (exception is OperationCanceledException or QuicException or AuthenticationException)
                {
                    accepting.IsCompleted.Should().BeTrue("cleanup must settle the accept before releasing its native owner");
                }
            }
            listenerOwner.Dispose();
            await system.Terminate().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private static async Task AssertCertificateDisposedAsync(X509Certificate2 certificate)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            try
            {
                using var key = certificate.GetRSAPrivateKey();
            }
            catch (CryptographicException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), deadline.Token);
        }
    }

    [Theory(DisplayName = "Factory certificate disposal follows native release rather than a completed managed shutdown callback")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_RetainOwnedCertificate_When_NativeReleaseIsPending(bool released)
    {
        using var identity = new Identity();
        using var certificate = TlsCertificateLoader.LoadPkcs12FromFile(
            identity.Lookup(MultiNodeTransport.CertificateVariable)!, null,
            OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
        var nativeReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (released)
            nativeReleased.SetResult();
        var disposal = MultiNodeTransport.DisposeAfterNativeReleaseAsync(certificate, nativeReleased.Task);
        try
        {
            if (!released)
            {
                disposal.IsCompleted.Should().BeFalse();
                using var key = certificate.GetRSAPrivateKey();
                key.Should().NotBeNull("actor termination cannot dispose credentials still owned by native cleanup");
                nativeReleased.SetResult();
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await disposal.WaitAsync(deadline.Token);
            Assert.ThrowsAny<Exception>(() => certificate.GetRSAPrivateKey());
        }
        finally
        {
            nativeReleased.TrySetResult();
            await disposal;
        }
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
