#region copyright
// -----------------------------------------------------------------------
//  <copyright file="BackwardCompatibilitySpec.cs" company="Akka.NET project">
//      Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet>
//  </copyright>
// -----------------------------------------------------------------------
#endregion

using System;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Internals;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Transport
{
    public class BackwardCompatibilitySpec : AkkaSpec
    {
        private static readonly Config TestConfig = ConfigurationFactory.ParseString(@"
            akka {
                actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""

                # explicitly make use of helios configuration instead of dot-netty
                remote.helios.tcp {
                    hostname = localhost
                    port = 11311
                }
            }");
        

        public BackwardCompatibilitySpec(ITestOutputHelper output) : base(TestConfig, output)
        {
        }

        [Fact]
        public void DotNetty_transport_can_fallback_to_helios_settings()
        {
            var remoteActorRefProvider = RARP.For(Sys).Provider;
            var remoteSettings = remoteActorRefProvider.RemoteSettings;

            Assert.Equal(typeof(TcpTransport), Type.GetType(remoteSettings.Transports.First().TransportClass));
            Assert.Equal("localhost", remoteActorRefProvider.DefaultAddress.Host);
            Assert.Equal(11311, remoteActorRefProvider.DefaultAddress.Port.Value);
        }

        [Fact]
        public void DotNetty_transport_can_communicate_with_Helios_transport()
        {
            var heliosConfig = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = DEBUG
                    actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""

                    remote {
                        enabled-transports = [""akka.remote.helios.tcp""]
                        helios.tcp {
                            hostname = localhost
                            port = 11223
                        }
                    }
                }");

            using (var heliosSystem = ActorSystem.Create("helios-system", heliosConfig))
            {
                InitializeLogger(heliosSystem);
                heliosSystem.ActorOf(Props.Create<Echo>(), "echo");
                Sys.ActorOf(Props.Create<Echo>(), "echo");

                var heliosProvider = RARP.For(heliosSystem).Provider;

                Assert.Equal(
                    "Akka.Remote.Transport.Helios.HeliosTcpTransport, Akka.Remote.Transport.Helios", 
                    heliosProvider.RemoteSettings.Transports.First().TransportClass);

                var backAddress = RARP.For(Sys).Provider.DefaultAddress;
                var address = heliosProvider.DefaultAddress;

                Assert.Equal(11223, address.Port.Value);

                var echo = Sys.ActorSelection(new RootActorPath(address) / "user" / "echo");
                echo.Tell("hello", TestActor);
                ExpectMsg("hello");

                var echoBack = heliosSystem.ActorSelection(new RootActorPath(backAddress) / "user" / "echo");
                echoBack.Tell("hello back", TestActor);
                ExpectMsg("hello back");
            }
        }
        
        private sealed class Echo : ReceiveActor
        {
            public Echo()
            {
                ReceiveAny(msg => Sender.Tell(msg));
            }
        }
    }
}