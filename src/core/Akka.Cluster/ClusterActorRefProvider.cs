using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Cluster.Configuration;
using Akka.Event;
using Akka.Remote;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// The `ClusterActorRefProvider` will load the <see cref="Cluster"/>
    /// extension, i.e. the cluster will automatically be started when
    /// the `ClusterActorRefProvider` is used.
    /// </summary>
    public class ClusterActorRefProvider : RemoteActorRefProvider
    {
        public ClusterActorRefProvider(string systemName, Settings settings, EventStream eventStream /*DynamicAcccess*/) : base(systemName, settings, eventStream)
        {
            var clusterConfig = ClusterConfigFactory.Default();
            settings.InjectTopLevelFallback(clusterConfig);
        }

        public override void Init(ActorSystemImpl system)
        {
            //Complete the usual RemoteActorRefProvider initializations - need access to transports and RemoteWatcher before clustering can work
            base.Init(system);

            // initialize/load the Cluster extension
            Cluster.Get(system);
        }

        protected override ActorRef CreateRemoteWatcher(ActorSystem system)
        {
            // make sure Cluster extension is initialized/loaded from init thread
            Cluster.Get(system);

            var failureDetector = CreateRemoteWatcherFailureDetector(system);
            return system.ActorOf(ClusterRemoteWatcher.Props(
                failureDetector,
                RemoteSettings.WatchHeartBeatInterval,
                RemoteSettings.WatchUnreachableReaperInterval,
                RemoteSettings.WatchHeartbeatExpectedResponseAfter), "remote-watcher");
        }

        //TODO: Deployment stuff
    }
}
