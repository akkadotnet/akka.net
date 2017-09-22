using Akka.Actor;
using Akka.Cluster.Tools.Client;
using System.Collections.Immutable;

namespace DocsExamples.Networking.ClusterClient
{
    public class ClientListener : UntypedActor
    {
        private readonly IActorRef _targetClient;

        public ClientListener(IActorRef targetClient)
        {
            _targetClient = targetClient;
        }

        protected override void OnReceive(object message)
        {
            Context.Become(ReceiveWithContactPoints(ImmutableHashSet<ActorPath>.Empty));
        }

        protected override void PreStart()
        {
            _targetClient.Tell(SubscribeContactPoints.Instance);
        }

        public UntypedReceive ReceiveWithContactPoints(IImmutableSet<ActorPath> contactPoints)
        {
            return (message) =>
            {
                switch (message)
                {
                    // Now do something with the up-to-date "cps"
                    case ContactPoints cp:
                        Context.Become(ReceiveWithContactPoints(cp.ContactPointsList));
                        break;
                    // Now do something with an up-to-date "contactPoints + cp"
                    case ContactPointAdded cpa:
                        Context.Become(ReceiveWithContactPoints(contactPoints.Add(cpa.ContactPoint)));
                        break;
                    // Now do something with an up-to-date "contactPoints - cp"
                    case ContactPointRemoved cpr:
                        Context.Become(ReceiveWithContactPoints(contactPoints.Remove(cpr.ContactPoint)));
                        break;
                }
            };
        }
    }
}
