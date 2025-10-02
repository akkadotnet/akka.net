//-----------------------------------------------------------------------
// <copyright file="DotNettyMutualTlsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    /// <summary>
    /// Tests mutual TLS authentication enforcement in DotNetty transport.
    /// When require-mutual-authentication is enabled, both client and server must
    /// present valid certificates with accessible private keys.
    /// </summary>
    public class DotNettyMutualTlsSpec : AkkaSpec
    {
        private const string ValidCertPath = "Resources/akka-validcert.pfx";
        private const string Password = "password";

        public DotNettyMutualTlsSpec(ITestOutputHelper output) : base(ConfigurationFactory.Empty, output)
        {
        }

        private static Config CreateConfig(bool enableSsl, bool requireMutualAuth, bool suppressValidation = false)
        {
            var config = ConfigurationFactory.ParseString($@"
                akka {{
                    loglevel = DEBUG
                    actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                    remote.dot-netty.tcp {{
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = {(enableSsl ? "on" : "off")}
                        log-transport = off
                    }}
                }}
            ");

            if (!enableSsl)
                return config;

            var escapedPath = ValidCertPath.Replace("\\", "\\\\");
            var ssl = $@"
                akka.remote.dot-netty.tcp.ssl {{
                    suppress-validation = {(suppressValidation ? "on" : "off")}
                    require-mutual-authentication = {(requireMutualAuth ? "on" : "off")}
                    certificate {{
                        path = ""{escapedPath}""
                        password = ""{Password}""
                    }}
                }}
            ";
            return ConfigurationFactory.ParseString(ssl).WithFallback(config);
        }

        [Fact]
        public async Task Mutual_TLS_should_allow_connection_when_both_nodes_have_valid_certificates()
        {
            // Both server and client have valid certs, mutual TLS enabled
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Should successfully connect and communicate
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public async Task Mutual_TLS_disabled_should_allow_standard_TLS_connection()
        {
            // Server has mutual TLS disabled (standard server-only TLS)
            ActorSystem server = null;
            ActorSystem client = null;

            try
            {
                var serverConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                server = ActorSystem.Create("ServerSystem", serverConfig);
                InitializeLogger(server, "[SERVER] ");

                var clientConfig = CreateConfig(enableSsl: true, requireMutualAuth: false, suppressValidation: true);
                client = ActorSystem.Create("ClientSystem", clientConfig);
                InitializeLogger(client, "[CLIENT] ");

                var serverEcho = server.ActorOf(Props.Create(() => new EchoActor()), "echo");

                var serverAddr = RARP.For(server).Provider.DefaultAddress;
                var serverEchoPath = new RootActorPath(serverAddr) / "user" / "echo";

                // Should successfully connect with standard TLS
                var response = await client.ActorSelection(serverEchoPath).Ask<string>("hello", TimeSpan.FromSeconds(5));
                Assert.Equal("hello", response);
            }
            finally
            {
                if (client != null)
                    Shutdown(client, TimeSpan.FromSeconds(10));
                if (server != null)
                    Shutdown(server, TimeSpan.FromSeconds(10));
            }
        }

        [Fact]
        public void System_should_start_successfully_with_mutual_TLS_enabled()
        {
            // Verify that enabling mutual TLS doesn't break system startup
            ActorSystem sys = null;

            try
            {
                var config = CreateConfig(enableSsl: true, requireMutualAuth: true, suppressValidation: true);
                sys = ActorSystem.Create("TestSystem", config);
                InitializeLogger(sys);

                // System should be running
                Assert.False(sys.WhenTerminated.IsCompleted);

                // Remote should be initialized
                var remoteAddress = RARP.For(sys).Provider.DefaultAddress;
                Assert.NotNull(remoteAddress);
            }
            finally
            {
                if (sys != null)
                    Shutdown(sys, TimeSpan.FromSeconds(10));
            }
        }

        private sealed class EchoActor : ReceiveActor
        {
            public EchoActor()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }
}
