//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    public class DistributedPubSubSettings
    {
        /// <summary>
        /// Creates cluster publish/subscribe settings from the default configuration `akka.cluster.pub-sub`.
        /// </summary>
        public static DistributedPubSubSettings Create(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.cluster.pub-sub");
            if (config == null) throw new ArgumentException("Actor system settings has no configuration for akka.cluster.pub-sub defined");

            return Create(config);
        }

        /// <summary>
        /// Creates cluster publish subscribe settings from provided configuration with the same layout as `akka.cluster.pub-sub`.
        /// </summary>
        public static DistributedPubSubSettings Create(Config config)
        {
            RoutingLogic routingLogic = null;
            var routingLogicName = config.GetString("routing-logic");
            switch (routingLogicName)
            {
                case "random":
                    routingLogic = new RandomLogic();
                    break;
                case "round-robin":
                    routingLogic = new RoundRobinRoutingLogic();
                    break;
                case "broadcast":
                    routingLogic = new BroadcastRoutingLogic();
                    break;
                case "consistent-hashing":
                    throw new ArgumentException("Consistent hashing routing logic cannot be used by the pub-sub mediator");
                default:
                    throw new ArgumentException("Unknown routing logic is tried to be applied to the pub-sub mediator: " +
                                                routingLogicName);
            }

            return new DistributedPubSubSettings(
                config.GetString("role"),
                routingLogic,
                config.GetTimeSpan("gossip-interval"),
                config.GetTimeSpan("removed-time-to-live"),
                config.GetInt("max-delta-elements"));
        }

        /// <summary>
        /// The mediator starts on members tagged with this role. Uses all if undefined.
        /// </summary>
        public readonly string Role;

        /// <summary>
        /// The routing logic to use for <see cref="DistributedPubSubMediator.Send"/>.
        /// </summary>
        public readonly RoutingLogic RoutingLogic;

        /// <summary>
        /// How often the <see cref="DistributedPubSubMediator"/> should send out gossip information
        /// </summary>
        public readonly TimeSpan GossipInterval;

        /// <summary>
        /// Removed entries are pruned after this duration.
        /// </summary>
        public readonly TimeSpan RemovedTimeToLive;

        /// <summary>
        /// Maximum number of elements to transfer in one message when synchronizing the registries. 
        /// Next chunk will be transferred in next round of gossip.
        /// </summary>
        public readonly int MaxDeltaElements;

        /// <summary>
        /// Creates a new instance of the <see cref="DistributedPubSubSettings"/>.
        /// </summary>
        public DistributedPubSubSettings(string role, RoutingLogic routingLogic, TimeSpan gossipInterval, TimeSpan removedTimeToLive, int maxDeltaElements)
        {
            Role = !string.IsNullOrEmpty(role) ? role : null;
            RoutingLogic = routingLogic;
            GossipInterval = gossipInterval;
            RemovedTimeToLive = removedTimeToLive;
            MaxDeltaElements = maxDeltaElements;
        }

        public DistributedPubSubSettings WithRole(string role)
        {
            return new DistributedPubSubSettings(role, RoutingLogic, GossipInterval, RemovedTimeToLive, MaxDeltaElements);
        }

        public DistributedPubSubSettings WithRoutingLogic(RoutingLogic routingLogic)
        {
            return new DistributedPubSubSettings(Role, routingLogic, GossipInterval, RemovedTimeToLive, MaxDeltaElements);
        }

        public DistributedPubSubSettings WithGossipInterval(TimeSpan gossipInterval)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, gossipInterval, RemovedTimeToLive, MaxDeltaElements);
        }

        public DistributedPubSubSettings WithRemovedTimeToLive(TimeSpan removedTtl)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, GossipInterval, removedTtl, MaxDeltaElements);
        }

        public DistributedPubSubSettings WithMaxDeltaElements(int maxDeltaElements)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, GossipInterval, RemovedTimeToLive, maxDeltaElements);
        }
    }
}