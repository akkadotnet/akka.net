using System;
using Akka.Actor;
using Akka.Event;
using Akka.Remote;

namespace Akka.Cluster
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// The `ClusterActorRefProvider` will load the [[akka.cluster.Cluster]]
    /// extension, i.e. the cluster will automatically be started when
    /// the `ClusterActorRefProvider` is used.
    /// </summary>
    public class ClusterActorRefProvider : RemoteActorRefProvider
    {
        public ClusterActorRefProvider(string systemName, Settings settings, EventStream eventStream /*DynamicAcccess*/) : base(systemName, settings, eventStream)
        {
        }

        public override void Init(ActorSystem system)
        {
            base.Init(system);

            // initialize/load the Cluster extension
            //TODO: scope is very wrong
            var b =new Cluster(system);
        }

        public RemoteTransport Transport { get { throw new NotImplementedException(); } }
    }
}
