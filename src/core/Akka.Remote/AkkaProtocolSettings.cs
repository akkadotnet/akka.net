using System;
using Akka.Configuration;

namespace Akka.Remote
{
    public class AkkaProtocolSettings
    {
        public Config TransportFailureDetectorConfig { get; private set; }

        public string TransportFailureDetectorImplementationClass { get; private set; }

        public TimeSpan TransportHeartBeatInterval { get; private set; }

        public AkkaProtocolSettings(Config config)
        {
            TransportFailureDetectorConfig = config.GetConfig("akka.remote.transport-failure-detector");
            TransportFailureDetectorImplementationClass = TransportFailureDetectorConfig.GetString("implementation-class");
            TransportHeartBeatInterval = TransportFailureDetectorConfig.GetMillisDuration("heartbeat-interval");
        }
    }
}