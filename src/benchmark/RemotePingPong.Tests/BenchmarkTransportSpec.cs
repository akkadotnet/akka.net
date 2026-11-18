// -----------------------------------------------------------------------
// <copyright file="BenchmarkTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://dotnetfoundation.org/>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Xunit;

namespace RemotePingPong.Tests
{
    public sealed class BenchmarkTransportSpec : Akka.TestKit.Xunit.TestKit
    {
        public BenchmarkTransportSpec(ITestOutputHelper output) : base(output: output) { }

        [Theory(DisplayName = "Benchmark generated and supplied credentials authenticate bidirectional Artery TLS traffic")]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Should_AuthenticateBothDirections_When_UsingBenchmarkCredentials(bool supplied)
        {
            var path = supplied ? Path.Combine(Path.GetTempPath(), $"artery-benchmark-{Guid.NewGuid():N}.pfx") : null;
            try
            {
                if (path is not null)
                    WriteIdentity(path);

                using var transport = BenchmarkTransport.Create("artery-tls", false, path, null, null);
                var config = ConfigurationFactory.ParseString("""
                    akka.loglevel = INFO
                    akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                    akka.remote.artery {
                        enabled = on
                        canonical.hostname = "127.0.0.1"
                        canonical.port = 0
                    }
                    """);
                ActorSystem? first = null;
                ActorSystem? second = null;
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    first = ActorSystem.Create("BenchmarkTlsA", transport.Setup(config));
                    second = ActorSystem.Create("BenchmarkTlsB", transport.Setup(config));
                    second.ActorOf(Props.Create(() => new Echo()), "echo");
                    var address = ((ExtendedActorSystem)second).Provider.DefaultAddress;
                    var remote = first.ActorSelection($"akka://{second.Name}@127.0.0.1:{address.Port}/user/echo");
                    Assert.Equal("benchmark-credentials", await remote.Ask<string>("benchmark-credentials", deadline.Token));
                }
                finally
                {
                    await Task.WhenAll(first?.Terminate() ?? Task.CompletedTask,
                        second?.Terminate() ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(15));
                }
            }
            finally
            {
                if (path is not null)
                    File.Delete(path);
            }
        }

        private static void WriteIdentity(string path)
        {
            using var key = RSA.Create(2048);
            var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using var identity = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddHours(1));
            var bytes = identity.Export(X509ContentType.Pkcs12);
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using var file = new FileStream(path, options);
                file.Write(bytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        private sealed class Echo : ReceiveActor
        {
            public Echo() => Receive<string>(value => Sender.Tell(value));
        }
    }
}
