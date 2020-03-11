//-----------------------------------------------------------------------
// <copyright file="ThreadPoolDispatcherRemoteMessagingThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
