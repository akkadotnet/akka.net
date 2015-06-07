using System;
using Akka.Actor;
using Akka.Routing;

namespace Akka.Cluster.Tools.PubSub
{
    public class DistributedPubSubSettings
    {
        public readonly string Role;
        public readonly RoutingLogic RoutingLogic;
        public readonly TimeSpan GossipInterval;
        public readonly TimeSpan RemovedTimeToLive;
        public readonly int MaxDeltaElements;

        /**
         * @param role Start the mediator on members tagged with this role.
         *   All members are used if undefined.
         * @param routingLogic The routing logic to use for `Send`.
         * @param gossipInterval How often the DistributedPubSubMediator should send out gossip information
         * @param removedTimeToLive Removed entries are pruned after this duration
         * @param maxDeltaElements Maximum number of elements to transfer in one message when synchronizing
         *   the registries. Next chunk will be transferred in next round of gossip.
         */
        private DistributedPubSubSettings(string role, RoutingLogic routingLogic, TimeSpan gossipInterval, TimeSpan removedTimeToLive, int maxDeltaElements)
        {
            Role = !string.IsNullOrEmpty(role) ? role : null;
            RoutingLogic = routingLogic;
            GossipInterval = gossipInterval;
            RemovedTimeToLive = removedTimeToLive;
            MaxDeltaElements = maxDeltaElements;
        }

        public static DistributedPubSubSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.pub-sub");
            if(config == null) throw new ArgumentException("Actor system settings has no configuration for akka.cluster.pub-sub defined");

            RoutingLogic routingLogic = null;
            var routingLogicName = config.GetString("routing-logic");
            switch (routingLogicName)
            {
                case "random": routingLogic = new RandomLogic(); break;
                case "round-robin": routingLogic = new RoundRobinRoutingLogic(); break;
                case "broadcast": routingLogic = new BroadcastRoutingLogic(); break;
                case "consistent-hashing": throw new ArgumentException("Consistent hashing routing logic cannot be used by the pub-sub mediator");
                default: throw new ArgumentException("Unknown routing logic is tried to be applied to the pub-sub mediator: " + routingLogicName);
            }

            return new DistributedPubSubSettings(
                config.GetString("role"),
                routingLogic,
                config.GetTimeSpan("gossip-interval"),
                config.GetTimeSpan("removed-time-to-live"),
                config.GetInt("max-delta-elements"));
        }
    }
}