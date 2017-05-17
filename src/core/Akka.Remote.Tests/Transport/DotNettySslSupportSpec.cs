#region copyright
// -----------------------------------------------------------------------
//  <copyright file="DotNettySslSupportSpec.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using static Akka.Util.RuntimeDetector;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettySslSupportSpec : AkkaSpec
    {
        #region Setup / Config

        // valid to 01/01/2037
        private static readonly string ValidCertPath = "Resources/akka-validcert.pfx";

        private const string Password = "password";

        private static Config TestConfig(string certPath, string password)
        {
            var enableSsl = !string.IsNullOrEmpty(certPath);
            var config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                        log-transport = true
                    }
                }
            }");
            return !enableSsl
                ? config
                : config.WithFallback(@"akka.remote.dot-netty.tcp.ssl {
                    suppress-validation = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                    certificate {
                        path = """ + certPath + @"""
                        password = """ + password + @"""
                    }
                }");
        }

        private ActorSystem sys2;
        private Address address1;
        private Address address2;

        private ActorPath echoPath;

        private void Setup(string certPath, string password)
        {
            sys2 = ActorSystem.Create("sys2", TestConfig(certPath, password));
            InitializeLogger(sys2);

            var echo = sys2.ActorOf(Props.Create<Echo>(), "echo");

            address1 = RARP.For(Sys).Provider.DefaultAddress;
            address2 = RARP.For(sys2).Provider.DefaultAddress;
            echoPath = new RootActorPath(address2) / "user" / "echo";
        }

        #endregion

        // WARNING: YOU NEED TO RUN TEST IN ADMIN MODE IN ORDER TO ADD/REMOVE CERTIFICATES TO CERT STORE!
        public DotNettySslSupportSpec(ITestOutputHelper output) : base(TestConfig(ValidCertPath, Password), output)
        {
        }
        
        [Fact]
        public void Secure_transport_should_be_possible_between_systems_sharing_the_same_certificate()
        {
            // skip this test due to linux/mono certificate issues
            if (IsMono) return;

            Setup(ValidCertPath, Password);

            var probe = CreateTestProbe();
            Sys.ActorSelection(echoPath).Tell("hello", probe.Ref);
            probe.ExpectMsg("hello");
        }

        [Fact]
        public void Secure_transport_should_NOT_be_possible_between_systems_using_SSL_and_one_not_using_it()
        {
            Setup(null, null);

            var probe = CreateTestProbe();
            Assert.Throws<RemoteTransportException>(() =>
            {
                Sys.ActorSelection(echoPath).Tell("hello", probe.Ref);
                probe.ExpectNoMsg();
            });
        }

        #region helper classes / methods
        

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Shutdown(sys2, TimeSpan.FromSeconds(3));
            }
        }

        public class Echo : ReceiveActor
        {
            public Echo()
            {
                Receive<string>(str => Sender.Tell(str));
            }
        }

        #endregion
    }
}