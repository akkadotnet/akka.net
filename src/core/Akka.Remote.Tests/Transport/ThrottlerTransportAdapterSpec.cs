using Akka.Configuration;
using Akka.TestKit;

namespace Akka.Remote.Tests.Transport
{
    public class ThrottlerTransportAdapterSpec : AkkaSpec
    {
        #region Setup / Config

        public static Config ThrottlerTransportAdapterSpecConfig
        {
            get
            {
                return ConfigurationFactory.ParseString(@"
                akka {
                  actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
                  remote.helios.tcp.hostname = ""localhost""
                  remote.log-remote-lifecycle-events = off
                  remote.retry-gate-closed-for = 1 s
                  remote.transport-failure-detector.heartbeat-interval = 1 s
                  remote.transport-failure-detector.acceptable-heartbeat-pause = 3 s
                  remote.helios.tcp.applied-adapters = [""trttl""]
                  remote.helios.tcp.port = 0
                }");
            }
        }

        #endregion
    }
}
