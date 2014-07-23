using System;
using Akka.Actor;
using Akka.Remote;

namespace Akka.Cluster
{
    public class NodeMetrics
    {
    }

    public class GossipStats
    {
    }

    public class VectorClockStats
    {
    }

    public class ClusterActorRefProvider
    {
        public RemoteTransport Transport { get { throw new NotImplementedException();} }
    }

}
