//-----------------------------------------------------------------------
// <copyright file="ForkJoinDispatcherRemoteMessagingThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Remote.Tests.Performance.Transports;

namespace Akka.Remote.Tests.Performance
{
    public class ForkJoinDispatcherRemoteMessagingThroughputSpec : TestTransportRemoteMessagingThroughputSpec
    {
        public static Config ForkJoinDispatcherConfig => ConfigurationFactory.ParseString(@"
            akka.remote.default-remote-dispatcher {
              type = ForkJoinDispatcher
              dedicated-thread-pool {
                # Fixed number of threads to have in this threadpool
                thread-count = 4
              }
            }
    
            akka.remote.backoff-remote-dispatcher {
              type = ForkJoinDispatcher
              dedicated-thread-pool {
                # Fixed number of threads to have in this threadpool
                thread-count = 4
              }
            }
        ");

        public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            return ForkJoinDispatcherConfig.WithFallback(base.CreateActorSystemConfig(actorSystemName, ipOrHostname, port));
        }
    }
}
