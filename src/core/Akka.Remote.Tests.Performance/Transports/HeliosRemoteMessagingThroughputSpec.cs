namespace Akka.Remote.Tests.Performance.Transports
{
    // todo: SKIP FOR NOW - BUGS
    //public class HeliosRemoteMessagingThroughputSpec : RemoteMessagingThroughputSpecBase
    //{
    //    public override Config CreateActorSystemConfig(string actorSystemName, string ipOrHostname, int port)
    //    {
    //        var baseConfig = ConfigurationFactory.ParseString(@"
    //            akka {
    //          actor.provider = ""Akka.Remote.RemoteActorRefProvider,Akka.Remote""

    //          remote {
    //            log-remote-lifecycle-events = off

    //            helios.tcp {
    //                port = 0
    //                hostname = ""localhost""

    //                # Used to configure the number of I/O worker threads on server sockets
    //  server-socket-worker-pool {
    //    # Min number of threads to cap factor-based number to
    //    pool-size-min = 1

    //    # The pool size factor is used to determine thread pool size
    //    # using the following formula: ceil(available processors * factor).
    //    # Resulting size is then bounded by the pool-size-min and
    //    # pool-size-max values.
    //    pool-size-factor = 1.0

    //    # Max number of threads to cap factor-based number to
    //    pool-size-max = 1
    //  }

    //  # Used to configure the number of I/O worker threads on client sockets
    //  client-socket-worker-pool {
    //    # Min number of threads to cap factor-based number to
    //    pool-size-min = 1

    //    # The pool size factor is used to determine thread pool size
    //    # using the following formula: ceil(available processors * factor).
    //    # Resulting size is then bounded by the pool-size-min and
    //    # pool-size-max values.
    //    pool-size-factor = 1.0

    //    # Max number of threads to cap factor-based number to
    //    pool-size-max = 1
    //  }
    //            }
    //          }
    //        ");

    //        var bindingConfig =
    //            ConfigurationFactory.ParseString(@"akka.remote.helios.tcp.hostname = """ + ipOrHostname + @"""")
    //            .WithFallback(ConfigurationFactory.ParseString(@"akka.remote.helios.tcp.port = " + port));

    //        return bindingConfig.WithFallback(baseConfig);
    //    }
    //}
}