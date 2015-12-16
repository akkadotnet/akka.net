using System;
using Akka.Configuration;

namespace Akka.Remote.Tests.Performance
{
    public class TestTransportRemoteMessagingThroughputSpec : RemoteMessagingThroughputSpecBase
    {
        public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
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
                  registry-key = aX33k12WKg
                  maximum-payload-bytes = 128000b
                  scheme-identifier = test
                }
              }
            ");

            port = 10; //BUG: setting the port to 0 causes the DefaultAddress to report the port as -1
            var remoteAddress = String.Format("test://{0}@{1}:{2}", actorSystemName, ipOrHostname, port);
            var bindingConfig =
                ConfigurationFactory.ParseString(@"akka.remote.test.local-address = """+ remoteAddress +@"""");

            return bindingConfig.WithFallback(baseConfig);
        }
    }
}