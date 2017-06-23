using Akka.Actor;
using Akka.Cluster.Tools.Client;
using System.Collections.Immutable;

namespace DocsExamples.Networking.ClusterClient
{
    public class ReceptionistListener : UntypedActor
    {
        private readonly IActorRef _targetReceptionist;

        public ReceptionistListener(IActorRef targetReceptionist)
        {
            _targetReceptionist = targetReceptionist;
        }

        protected override void OnReceive(object message)
        {
            Context.Become(ReceiveWithContactPoints(ImmutableHashSet<IActorRef>.Empty));
        }

        protected override void PreStart()
        {
            _targetReceptionist.Tell(SubscribeClusterClients.Instance);
        }

        public UntypedReceive ReceiveWithContactPoints(IImmutableSet<IActorRef> contactPoints)
        {
            return (message) =>
            {
                switch (message)
                {
                    // Now do something with the up-to-date "c"
                    case ClusterClients cc:
                        Context.Become(ReceiveWithContactPoints(cc.ClusterClientsList));
                        break;
                    // Now do something with an up-to-date "clusterClients + c"
                    case ClusterClientUp ccu:
                        Context.Become(ReceiveWithContactPoints(contactPoints.Add(ccu.ClusterClient)));
                        break;
                    // Now do something with an up-to-date "clusterClients - c"
                    case ClusterClientUnreachable ccun:
                        Context.Become(ReceiveWithContactPoints(contactPoints.Remove(ccun.ClusterClient)));
                        break;
                }
            };
        }
    }
}
