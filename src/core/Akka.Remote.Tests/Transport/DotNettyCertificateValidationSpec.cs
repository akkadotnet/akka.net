//-----------------------------------------------------------------------
// <copyright file="DotNettyCertificateValidationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Tests that SSL certificate validation happens at startup, not during runtime.
    /// This ensures fail-fast behavior when certificates are misconfigured.
    /// </summary>
    public class DotNettyCertificateValidationSpec : AkkaSpec
    {
        private const string ValidCertPath = "Resources/akka-validcert.pfx";
        private const string Password = "password";
        private static readonly string NoKeyCertPath = Path.Combine("Resources", "validation-no-key.cer");

        public DotNettyCertificateValidationSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
        {
        }

        private static Config CreateConfig(bool enableSsl, string certPath, string certPassword)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote.dot-netty.tcp {
                    port = 0
                    hostname = ""127.0.0.1""
                    enable-ssl = " + (enableSsl ? "on" : "off") + @"
                    log-transport = off
                }
            }");

            if (!enableSsl || string.IsNullOrEmpty(certPath))
                return baseConfig;

            var escapedPath = certPath.Replace("\\", "\\\\");
            var ssl = $@"akka.remote.dot-netty.tcp.ssl {{
                suppress-validation = on
                certificate {{
                    path = ""{escapedPath}""
                    password = ""{certPassword ?? string.Empty}""
                }}
            }}";
            return baseConfig.WithFallback(ssl);
        }

        private static void CreateCertificateWithoutPrivateKey()
        {
            var fullCert = new X509Certificate2(ValidCertPath, Password, X509KeyStorageFlags.Exportable);
            var publicKeyBytes = fullCert.Export(X509ContentType.Cert);
            var dir = Path.GetDirectoryName(NoKeyCertPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(NoKeyCertPath, publicKeyBytes);
        }

        [Fact]
        public void Server_should_fail_at_startup_with_certificate_without_private_key()
        {
            CreateCertificateWithoutPrivateKey();

            try
            {
                // Server with cert that has no private key should FAIL TO START
                var serverConfig = CreateConfig(true, NoKeyCertPath, null);

                // This should throw an exception during ActorSystem.Create (wrapped in AggregateException)
                var aggregateEx = Assert.Throws<AggregateException>(() =>
                {
                    using var server = ActorSystem.Create("ServerSystem", serverConfig);
                });

                // Unwrap the inner exception
                var innerEx = aggregateEx.InnerException ?? aggregateEx;
                while (innerEx is AggregateException agg && agg.InnerException != null)
                    innerEx = agg.InnerException;

                // Should be ConfigurationException about private key
                Assert.IsType<ConfigurationException>(innerEx);
                Assert.Contains("private key", innerEx.Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try
                {
                    if (File.Exists(NoKeyCertPath))
                        File.Delete(NoKeyCertPath);
                }
                catch { /* ignore */ }
            }
        }

        [Fact]
        public void Server_should_start_successfully_with_valid_certificate()
        {
            // Server with valid cert should start normally
            var serverConfig = CreateConfig(true, ValidCertPath, Password);

            using var server = ActorSystem.Create("ServerSystem", serverConfig);
            InitializeLogger(server);

            // Server should be running
            Assert.False(server.WhenTerminated.IsCompleted);
        }

        [Fact]
        public void Server_should_start_successfully_without_ssl()
        {
            // Server without SSL should start normally
            var serverConfig = CreateConfig(false, null, null);

            using var server = ActorSystem.Create("ServerSystem", serverConfig);
            InitializeLogger(server);

            // Server should be running
            Assert.False(server.WhenTerminated.IsCompleted);
        }
    }
}
