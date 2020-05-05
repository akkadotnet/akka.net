//-----------------------------------------------------------------------
// <copyright file="HeliosRemoteMessagingThroughputSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;

namespace Akka.Remote.Tests.Performance.Transports
{
    public class HeliosRemoteMessagingThroughputSpec : RemoteMessagingThroughputSpecBase
    {
        public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
        {
            var baseConfig = ConfigurationFactory.ParseString(@"
                akka {
              actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""

              remote {
                log-remote-lifecycle-events = off

                dot-netty.tcp {
                    port = 0
                    hostname = ""localhost""

                    # Used to configure the number of I/O worker threads on server sockets
      server-socket-worker-pool {
        # Min number of threads to cap factor-based number to
        pool-size-min = 2

        # The pool size factor is used to determine thread pool size
        # using the following formula: ceil(available processors * factor).
        # Resulting size is then bounded by the pool-size-min and
        # pool-size-max values.
        pool-size-factor = 1.0

        # Max number of threads to cap factor-based number to
        pool-size-max = 2
      }

      # Used to configure the number of I/O worker threads on client sockets
      client-socket-worker-pool {
        # Min number of threads to cap factor-based number to
        pool-size-min = 2

        # The pool size factor is used to determine thread pool size
        # using the following formula: ceil(available processors * factor).
        # Resulting size is then bounded by the pool-size-min and
        # pool-size-max values.
        pool-size-factor = 1.0

        # Max number of threads to cap factor-based number to
        pool-size-max = 2
      }
                }
              }
            ");

            var bindingConfig =
                ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.hostname = """ + ipOrHostname + @"""")
                .WithFallback(ConfigurationFactory.ParseString(@"akka.remote.dot-netty.tcp.port = " + port));

            return bindingConfig.WithFallback(baseConfig);
        }
    }
}
