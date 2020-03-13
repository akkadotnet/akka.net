//-----------------------------------------------------------------------
// <copyright file="TestTransportAssociationStressSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Remote.Transport;

namespace Akka.Remote.Tests.Performance.Transports
{
    public class TestTransportAssociationStressSpec : AssociationStressSpecBase
    {
        public override string CreateRegistryKey()
        {
            return Guid.NewGuid().ToString();
        }

        public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port, string registryKey = null)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"
               akka {
                  actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""
                
                  remote {
                    log-remote-lifecycle-events = off
                    enabled-transports = [
                      ""akka.remote.test"",
                    ]

                    test {
                      transport-class = ""Akka.Remote.Transport.TestTransport,Akka.Remote""
                      applied-adapters = []
                      maximum-payload-bytes = 128000b
                      scheme-identifier = test
                    }
                  }
                }
            ");

            port = 10; //BUG: setting the port to 0 causes the DefaultAddress to report the port as -1
            var remoteAddress = $"test://{actorSystemName}@{ipOrHostname}:{port}";
            var bindingConfig =
                ConfigurationFactory.ParseString(@"akka.remote.test.local-address = """ + remoteAddress + @"""");
            var registryKeyConfig = ConfigurationFactory.ParseString($"akka.remote.test.registry-key = {registryKey}");

            return registryKeyConfig.WithFallback(bindingConfig.WithFallback(baseConfig));
        }

        public override void Cleanup()
        {
            // nuke all registries
            AssociationRegistry.Clear();
            base.Cleanup();
        }
    }
}
