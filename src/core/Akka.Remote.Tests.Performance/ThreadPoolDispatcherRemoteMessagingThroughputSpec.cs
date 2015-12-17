using Akka.Configuration;
using Akka.Remote.Tests.Performance.Transports;

namespace Akka.Remote.Tests.Performance
{
    public class ThreadPoolDispatcherRemoteMessagingThroughputSpec : TestTransportRemoteMessagingThroughputSpec
    {
        public static Config ThreadPoolDispatcherConfig => ConfigurationFactory.ParseString(@"
            akka.remote.default-remote-dispatcher {
              type = Dispatcher
            }
    
            akka.remote.backoff-remote-dispatcher {
              type = Dispatcher
            }
        ");

        public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            return ThreadPoolDispatcherConfig.WithFallback(base.CreateActorSystemConfig(actorSystemName, ipOrHostname, port));
        }
    }
}