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
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Internals;
using Microsoft.Build.Framework;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    public class DotNettySslSupportSpec : AkkaSpec
    {
        #region Setup / Config

        private readonly ITestOutputHelper _output;
        private const string CertPath1 = "Resources/test-cert";
        private const string CertPath2 = "Resources/test-cert2";

        private static Config TestConfig(string certPath = null)
        {
            var enableSsl = !string.IsNullOrEmpty(certPath);
            var config = ConfigurationFactory.ParseString(@"
            akka {
                loglevel = DEBUG
                actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                remote {
                    dot-netty.tcp {
                        log-transport=true
                        port = 0
                        hostname = ""127.0.0.1""
                        enable-ssl = """ + enableSsl.ToString().ToLowerInvariant() + @"""
                    }
                }
            }");
            return enableSsl ? config.WithFallback("akka.remote.dot-netty.tcp.ssl.certificate.path = " + certPath) : config;
        }

        private ActorSystem sys2;
        private Address address1;
        private Address address2;

        private ActorPath echoPath;

        private void Setup(string certPath)
        {
            sys2 = ActorSystem.Create("sys2", TestConfig(certPath));
            AddTestLogging();

            var echo = sys2.ActorOf(Props.Create<Echo>(), "echo");

            address1 = RARP.For(Sys).Provider.DefaultAddress;
            address2 = RARP.For(sys2).Provider.DefaultAddress;
            echoPath = new RootActorPath(address2) / "user" / "echo";
        }

        private void AddTestLogging()
        {
            if (_output != null)
            {
                var system = (ExtendedActorSystem) sys2;
                var logger = system.SystemActorOf(Props.Create(() => new TestOutputLogger(_output)), "log-test");
                logger.Tell(new InitializeLogger(system.EventStream));
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                Shutdown(sys2, TimeSpan.FromSeconds(3));
            }
        }

        #endregion

        public DotNettySslSupportSpec(ITestOutputHelper output) : base(TestConfig(CertPath1), output)
        {
            _output = output;
        }

        [Fact]
        public void Secure_transport_should_be_possible_between_systems_sharing_the_same_certificate()
        {
            Setup(CertPath1);

            var probe = CreateTestProbe();
            Sys.ActorSelection(echoPath).Tell("hello", probe.Ref);
            probe.ExpectMsg("hello");
        }

        [Fact]
        public void Secure_transport_should_NOT_be_possible_between_systems_using_SSL_and_one_not_using_it()
        {
            Setup(null);

            var probe = CreateTestProbe();
            Sys.ActorSelection(echoPath).Tell("hello", probe.Ref);
            probe.ExpectNoMsg();
        }

        [Fact]
        public void Secure_transport_should_NOT_be_possible_between_systems_having_different_certificates()
        {
            Setup(CertPath2);

            var probe = CreateTestProbe();
            Sys.ActorSelection(echoPath).Tell("hello", probe.Ref);
            probe.ExpectNoMsg();
        }

        #region helper classes / methods

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