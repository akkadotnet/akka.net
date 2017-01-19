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
using Akka.Remote.Transport.DotNetty;
using Akka.TestKit;
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
            
        }
    }
}