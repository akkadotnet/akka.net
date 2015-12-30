using System;
using Akka.Configuration;
using Akka.Remote.Transport;

namespace Akka.Remote.Tests.Performance.Transports
{
    public class TestTransportRemoteMessagingThroughputSpec : RemoteMessagingThroughputSpecBase
    {
        private readonly string _registryKey = Guid.NewGuid().ToString();

        public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = ""DEBUG""
                    stdout-loglevel = ""DEBUG""
                    actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""

                  remote {
                    log-received-messages = on
                    log-send-messages = on
                    log-remote-lifecycle-events = on

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
                ConfigurationFactory.ParseString(@"akka.remote.test.local-address = """+ remoteAddress +@"""");
            var registryKeyConfig = ConfigurationFactory.ParseString($"akka.remote.test.registry-key = {_registryKey}");

            return registryKeyConfig.WithFallback(bindingConfig.WithFallback(baseConfig));
        }

        public override void Cleanup()
        {
            base.Cleanup();

            // force all content logged by the TestTransport to be released
            AssociationRegistry.Get(_registryKey).Reset();
        }
    }
}