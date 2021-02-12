//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Routing;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class DistributedPubSubSettings : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Creates cluster publish/subscribe settings from the default configuration `akka.cluster.pub-sub`.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public static DistributedPubSubSettings Create(ActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DistributedPubSub.DefaultConfig());

            var config = system.Settings.Config.GetConfig("akka.cluster.pub-sub");
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DistributedPubSubSettings>("akka.cluster.pub-sub");

            return Create(config);
        }

        /// <summary>
        /// Creates cluster publish subscribe settings from provided configuration with the same layout as `akka.cluster.pub-sub`.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public static DistributedPubSubSettings Create(Config config)
        {
            if (config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DistributedPubSubSettings>();

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

            // TODO: This will fail if DistributedPubSub.DefaultConfig() is not inside the fallback chain.
            // TODO: "gossip-interval" key depends on Config.GetTimeSpan() to return a TimeSpan.Zero default.
            // TODO: "removed-time-to-live" key depends on Config.GetTimeSpan() to return a TimeSpan.Zero default.
            // TODO: "max-delta-elements" key depends on Config.GetInt() to return a 0 default.
            return new DistributedPubSubSettings(
                config.GetString("role", null),
                routingLogic,
                config.GetTimeSpan("gossip-interval"),
                config.GetTimeSpan("removed-time-to-live"),
                config.GetInt("max-delta-elements"),
                config.GetBoolean("send-to-dead-letters-when-no-subscribers"));
        }

        /// <summary>
        /// The mediator starts on members tagged with this role. Uses all if undefined.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// The routing logic to use for <see cref="DistributedPubSubMediator.Send"/>.
        /// </summary>
        public RoutingLogic RoutingLogic { get; }

        /// <summary>
        /// How often the <see cref="DistributedPubSubMediator"/> should send out gossip information
        /// </summary>
        public TimeSpan GossipInterval { get; }

        /// <summary>
        /// Removed entries are pruned after this duration.
        /// </summary>
        public TimeSpan RemovedTimeToLive { get; }

        /// <summary>
        /// Maximum number of elements to transfer in one message when synchronizing the registries.
        /// Next chunk will be transferred in next round of gossip.
        /// </summary>
        public int MaxDeltaElements { get; }

        /// <summary>
        /// When a message is published to a topic with no subscribers send it to the dead letters.
        /// </summary>
        public bool SendToDeadLettersWhenNoSubscribers { get; }

        /// <summary>
        /// Creates a new instance of the <see cref="DistributedPubSubSettings" />.
        /// </summary>
        /// <param name="role">The role that will host <see cref="DistributedPubSubMediator"/> instances.</param>
        /// <param name="routingLogic">Optional. The routing logic used for distributing messages for topic groups.</param>
        /// <param name="gossipInterval">The gossip interval for propagating topic/subscriber data to other mediators.</param>
        /// <param name="removedTimeToLive">The amount of time it takes to prune a deactivated subscriber from the network.</param>
        /// <param name="maxDeltaElements">The maximum number of delta elements that can be propagated in a single gossip tick.</param>
        /// <exception cref="ArgumentException">Thrown if a user tries to use a <see cref="ConsistentHashingRoutingLogic"/> with routingLogic.</exception>
        [Obsolete("Obsolete - please us the full constructor instead. This constructor only exists for backwards API compatibility.")]
        public DistributedPubSubSettings(
            string role,
            RoutingLogic routingLogic,
            TimeSpan gossipInterval,
            TimeSpan removedTimeToLive,
            int maxDeltaElements) : this(role, routingLogic, gossipInterval, removedTimeToLive, maxDeltaElements, true){ }

        /// <summary>
        /// Creates a new instance of the <see cref="DistributedPubSubSettings" />.
        /// </summary>
        /// <param name="role">The role that will host <see cref="DistributedPubSubMediator"/> instances.</param>
        /// <param name="routingLogic">Optional. The routing logic used for distributing messages for topic groups.</param>
        /// <param name="gossipInterval">The gossip interval for propagating topic/subscriber data to other mediators.</param>
        /// <param name="removedTimeToLive">The amount of time it takes to prune a deactivated subscriber from the network.</param>
        /// <param name="maxDeltaElements">The maximum number of delta elements that can be propagated in a single gossip tick.</param>
        /// <param name="sendToDeadLettersWhenNoSubscribers">When a message is published to a topic with no subscribers send it to the dead letters.</param>
        /// <exception cref="ArgumentException">Thrown if a user tries to use a <see cref="ConsistentHashingRoutingLogic"/> with routingLogic.</exception>
        public DistributedPubSubSettings(
            string role,
            RoutingLogic routingLogic,
            TimeSpan gossipInterval,
            TimeSpan removedTimeToLive,
            int maxDeltaElements,
            bool sendToDeadLettersWhenNoSubscribers)
        {
            if (routingLogic is ConsistentHashingRoutingLogic)
            {
                throw new ArgumentException("ConsistentHashingRoutingLogic cannot be used by the pub-sub mediator");
            }

            Role = !string.IsNullOrEmpty(role) ? role : null;
            RoutingLogic = routingLogic;
            GossipInterval = gossipInterval;
            RemovedTimeToLive = removedTimeToLive;
            MaxDeltaElements = maxDeltaElements;
            SendToDeadLettersWhenNoSubscribers = sendToDeadLettersWhenNoSubscribers;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="role">TBD</param>
        /// <returns>TBD</returns>
        public DistributedPubSubSettings WithRole(string role)
        {
            return new DistributedPubSubSettings(role, RoutingLogic, GossipInterval, RemovedTimeToLive, MaxDeltaElements, SendToDeadLettersWhenNoSubscribers);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routingLogic">TBD</param>
        /// <returns>TBD</returns>
        public DistributedPubSubSettings WithRoutingLogic(RoutingLogic routingLogic)
        {
            return new DistributedPubSubSettings(Role, routingLogic, GossipInterval, RemovedTimeToLive, MaxDeltaElements, SendToDeadLettersWhenNoSubscribers);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="gossipInterval">TBD</param>
        /// <returns>TBD</returns>
        public DistributedPubSubSettings WithGossipInterval(TimeSpan gossipInterval)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, gossipInterval, RemovedTimeToLive, MaxDeltaElements, SendToDeadLettersWhenNoSubscribers);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="removedTtl">TBD</param>
        /// <returns>TBD</returns>
        public DistributedPubSubSettings WithRemovedTimeToLive(TimeSpan removedTtl)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, GossipInterval, removedTtl, MaxDeltaElements, SendToDeadLettersWhenNoSubscribers);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxDeltaElements">TBD</param>
        /// <returns>TBD</returns>
        public DistributedPubSubSettings WithMaxDeltaElements(int maxDeltaElements)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, GossipInterval, RemovedTimeToLive, maxDeltaElements, SendToDeadLettersWhenNoSubscribers);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sendToDeadLetterWhenNoSubscribers">TBD</param>
        /// <returns></returns>
        public DistributedPubSubSettings WithSendToDeadLettersWhenNoSubscribers(bool sendToDeadLetterWhenNoSubscribers)
        {
            return new DistributedPubSubSettings(Role, RoutingLogic, GossipInterval, RemovedTimeToLive, MaxDeltaElements, sendToDeadLetterWhenNoSubscribers);
        }
    }
}
