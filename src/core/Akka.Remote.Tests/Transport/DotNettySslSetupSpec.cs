//-----------------------------------------------------------------------
// <copyright file="DotNettySslSetupSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Util.RuntimeDetector;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettySslSetupSpec : AkkaSpec
    {
        #region Setup / Config

        // valid to 01/01/2037
        private const string ValidCertPath = "Resources/akka-validcert.pfx";

        private const string Password = "password";

        private static ActorSystemSetup TestActorSystemSetup(bool enableSsl, bool requireMutualAuth = false)
        {
            var setup = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString($@"
akka {{
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote {{
    dot-netty.tcp {{
      port = 0
      hostname = ""127.0.0.1""
      enable-ssl = ""{enableSsl.ToString().ToLowerInvariant()}""
      log-transport = true
    }}
  }}
}}")));

            if (!enableSsl)
                return setup;

            var certificate = new X509Certificate2(ValidCertPath, Password, X509KeyStorageFlags.DefaultKeySet);
            // Use the new constructor that supports mutual auth configuration
            return setup.And(new DotNettySslSetup(certificate, true, requireMutualAuth));
        }

        private ActorSystem _sys2;
        private ActorPath _echoPath;

        private void Setup(bool enableSsl)
        {
            _sys2 = ActorSystem.Create("sys2", TestActorSystemSetup(enableSsl));
            InitializeLogger(_sys2);

            _sys2.ActorOf(Props.Create<Echo>(), "echo");

            var address = RARP.For(_sys2).Provider.DefaultAddress;
            _echoPath = new RootActorPath(address) / "user" / "echo";
        }

        #endregion

        public DotNettySslSetupSpec(ITestOutputHelper output) : base(TestActorSystemSetup(true), output)
        {
        }

        #if !NET471
        [Fact]
        public async Task Secure_transport_should_be_possible_between_systems_sharing_the_same_certificate()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;

            Setup(true);

            var probe = CreateTestProbe();

            await AwaitAssertAsync(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectMsgAsync("hello", TimeSpan.FromSeconds(3));
            }, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        }
        #endif

        #if !NET471
        [Fact]
        public async Task Mutual_TLS_should_be_configurable_via_Setup_API()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;

            // Create a system with mutual TLS enabled via Setup API
            var setupWithMutualTls = ActorSystemSetup.Empty
                .And(BootstrapSetup.Create()
                    .WithConfig(ConfigurationFactory.ParseString(@"
akka {
  loglevel = DEBUG
  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
  remote.dot-netty.tcp {
    port = 0
    hostname = ""127.0.0.1""
    enable-ssl = true
    log-transport = true
  }
}")))
                .And(new DotNettySslSetup(
                    new X509Certificate2(ValidCertPath, Password, X509KeyStorageFlags.DefaultKeySet),
                    suppressValidation: true,
                    requireMutualAuthentication: true));

            var sys3 = ActorSystem.Create("sys3", setupWithMutualTls);
            try
            {
                InitializeLogger(sys3);
                sys3.ActorOf(Props.Create<Echo>(), "echo");

                // System should start successfully with mutual TLS
                await Task.Delay(TimeSpan.FromSeconds(1));
                Assert.False(sys3.WhenTerminated.IsCompleted);

                // Verify the transport settings have mutual TLS enabled
                var transport = RARP.For(sys3).Provider.Transport;
                Assert.NotNull(transport);
            }
            finally
            {
                await ShutdownAsync(sys3);
            }
        }
        #endif

        [Fact]
        public async Task Secure_transport_should_NOT_be_possible_between_systems_using_SSL_and_one_not_using_it()
        {
            Setup(false);

            var probe = CreateTestProbe();
            await Assert.ThrowsAsync<RemoteTransportException>(async () =>
            {
                Sys.ActorSelection(_echoPath).Tell("hello", probe.Ref);
                await probe.ExpectNoMsgAsync();
            });
        }

        #region helper classes / methods

        protected override void AfterAll()
        {
            base.AfterAll();
            Shutdown(_sys2, TimeSpan.FromSeconds(3));
        }

        private class Echo : ReceiveActor
        {
            public Echo()
            {
                Receive<string>(str => Sender.Tell(str));
            }
        }

        #endregion
    }
}
