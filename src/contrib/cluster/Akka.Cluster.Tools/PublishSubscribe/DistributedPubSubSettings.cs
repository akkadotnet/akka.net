//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

            var routingLogic = config.GetString("routing-logic")?.ToLowerInvariant() switch
            {
                "random" => (RoutingLogic) new RandomLogic(),
                "round-robin" => new RoundRobinRoutingLogic(),
                "broadcast" => new BroadcastRoutingLogic(),
                "consistent-hashing" => throw new ArgumentException("Consistent hashing routing logic cannot be used by the pub-sub mediator"),
                var unknown => throw new ArgumentException($"Unknown routing logic is tried to be applied to the pub-sub mediator: {unknown}")
            };

            var overflowLogic = config.GetString("buffered-message-overflow-strategy")?.ToLowerInvariant() switch
            {
                "drop-head" => OverflowStrategy.DropHead,
                "drop-tail" => OverflowStrategy.DropTail,
                "drop-buffer" => OverflowStrategy.DropBuffer,
                "drop-new" => OverflowStrategy.DropNew,
                "fail" => OverflowStrategy.Fail,
                var unknown => throw new ArgumentException($"Unknown buffer overflow strategy: {unknown}. Valid values are 'drop-head', 'drop-tail', 'drop-buffer', 'drop-new', and 'fail'.")
            };

            var defaultConfig = Create(DistributedPubSub.DefaultConfig().GetConfig("akka.cluster.pub-sub"));
            return new DistributedPubSubSettings(
                config.GetString("role", defaultConfig.Role),
                routingLogic,
                config.GetTimeSpan("gossip-interval", defaultConfig.GossipInterval),
                config.GetTimeSpan("removed-time-to-live", defaultConfig.RemovedTimeToLive),
                config.GetInt("max-delta-elements", defaultConfig.MaxDeltaElements),
                config.GetBoolean("send-to-dead-letters-when-no-subscribers", defaultConfig.SendToDeadLettersWhenNoSubscribers),
                config.GetBoolean("wait-for-subscribers", defaultConfig.WaitForSubscribers),
                config.GetInt("max-buffered-messages-per-topic", defaultConfig.MaxBufferedMessagePerTopic),
                config.GetTimeSpan("buffered-message-timeout", defaultConfig.BufferedMessageTimeout),
                config.GetTimeSpan("buffered-message-timeout-check-interval", defaultConfig.BufferedMessageTimeoutCheckInterval),
                overflowLogic);
        }

        /// <summary>
        /// The mediator starts on members tagged with this role. Uses all if undefined.
        /// </summary>
        public string Role { get; }

        /// <summary>
        /// The routing logic to use for <see cref="DistributedPubSubMediator"/>.
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
        /// When set to <c>true</c>, mediator will buffer messages that are failed to be published or sent
        /// because there are no subscribers in the cluster
        /// </summary>
        public bool WaitForSubscribers { get; }
        
        /// <summary>
        /// When <see cref="WaitForSubscribers"/> is set to <c>true</c>, this will set the maximum message buffer size
        /// for each topic 
        /// </summary>
        public int MaxBufferedMessagePerTopic { get; }
        
        /// <summary>
        /// When <see cref="WaitForSubscribers"/> is set to <c>true</c>, this will determine how long an unsent message
        /// is being kept inside the buffer before it is ultimately being sent to dead letter.
        /// </summary>
        public TimeSpan BufferedMessageTimeout { get; }

        /// <summary>
        /// When <see cref="WaitForSubscribers"/> is set to <c>true</c>, this will determine the interval on which
        /// all buffered message will be checked for timeout condition
        /// </summary>
        public TimeSpan BufferedMessageTimeoutCheckInterval { get; }
        
        /// <summary>
        /// When <see cref="WaitForSubscribers"/> is set to <c>true</c>, this will determine how mediator will
        /// behave when a topic buffer overflowed
        /// </summary>
        public OverflowStrategy BufferedMessageOverflowStrategy { get; }
        
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
        [Obsolete("Use .ctor that supports WaitForSubscribers instead. Since 1.4.42")]
        public DistributedPubSubSettings(
            string role,
            RoutingLogic routingLogic,
            TimeSpan gossipInterval,
            TimeSpan removedTimeToLive,
            int maxDeltaElements,
            bool sendToDeadLettersWhenNoSubscribers)
            : this(
                role: role,
                routingLogic: routingLogic,
                gossipInterval: gossipInterval,
                removedTimeToLive: removedTimeToLive,
                maxDeltaElements: maxDeltaElements, 
                sendToDeadLettersWhenNoSubscribers: sendToDeadLettersWhenNoSubscribers,
                waitForSubscribers: false,
                maxBufferedMessagePerTopic: 0,
                bufferedMessageTimeout: TimeSpan.Zero,
                bufferedMessageTimeoutCheckInterval: TimeSpan.Zero,
                bufferedMessageOverflowStrategy: OverflowStrategy.DropHead)
        {
        }

        /// <summary>
        /// Creates a new instance of the <see cref="DistributedPubSubSettings" />.
        /// </summary>
        /// <param name="role">The role that will host <see cref="DistributedPubSubMediator"/> instances.</param>
        /// <param name="routingLogic">Optional. The routing logic used for distributing messages for topic groups.</param>
        /// <param name="gossipInterval">The gossip interval for propagating topic/subscriber data to other mediators.</param>
        /// <param name="removedTimeToLive">The amount of time it takes to prune a deactivated subscriber from the network.</param>
        /// <param name="maxDeltaElements">The maximum number of delta elements that can be propagated in a single gossip tick.</param>
        /// <param name="sendToDeadLettersWhenNoSubscribers">When a message is published to a topic with no subscribers send it to the dead letters.</param>
        /// <param name="waitForSubscribers">Should the mediator buffers messages that are failed to be published or sent or not</param>
        /// <param name="maxBufferedMessagePerTopic">Maximum message buffer size for each topic</param>
        /// <param name="bufferedMessageTimeout">How long an unsent message is being kept inside the buffer before it is ultimately being sent to dead letter.</param>
        /// <param name="bufferedMessageTimeoutCheckInterval">Buffered message timeout condition check interval</param>
        /// <param name="bufferedMessageOverflowStrategy">Determine how the mediator should behave when a topic buffer overflows</param>
        /// <exception cref="ArgumentException">Thrown if a user tries to use a <see cref="ConsistentHashingRoutingLogic"/> with routingLogic.</exception>
        public DistributedPubSubSettings(
            string role,
            RoutingLogic routingLogic,
            TimeSpan gossipInterval,
            TimeSpan removedTimeToLive,
            int maxDeltaElements,
            bool sendToDeadLettersWhenNoSubscribers,
            bool waitForSubscribers,
            int maxBufferedMessagePerTopic,
            TimeSpan bufferedMessageTimeout,
            TimeSpan bufferedMessageTimeoutCheckInterval,
            OverflowStrategy bufferedMessageOverflowStrategy)
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
            WaitForSubscribers = waitForSubscribers;
            MaxBufferedMessagePerTopic = maxBufferedMessagePerTopic;
            BufferedMessageTimeout = bufferedMessageTimeout;
            BufferedMessageTimeoutCheckInterval = bufferedMessageTimeoutCheckInterval;
            BufferedMessageOverflowStrategy = bufferedMessageOverflowStrategy;
        }

        private DistributedPubSubSettings Copy(
            string? role = null,
            RoutingLogic? routingLogic = null,
            TimeSpan? gossipInterval = null,
            TimeSpan? removedTimeToLive = null,
            int? maxDeltaElements = null,
            bool? sendToDeadLettersWhenNoSubscribers = null,
            bool? waitForSubscribers = null,
            int? maxBufferedMessagePerTopic = null,
            TimeSpan? bufferedMessageTimeout = null,
            TimeSpan? bufferedMessageTimeoutCheckInterval = null,
            OverflowStrategy? bufferedMessageOverflowStrategy = null)
        {
            return new DistributedPubSubSettings(
                role ?? Role,
                routingLogic ?? RoutingLogic,
                gossipInterval ?? GossipInterval,
                removedTimeToLive ?? RemovedTimeToLive,
                maxDeltaElements ?? MaxDeltaElements,
                sendToDeadLettersWhenNoSubscribers ?? SendToDeadLettersWhenNoSubscribers,
                waitForSubscribers ?? WaitForSubscribers,
                maxBufferedMessagePerTopic ?? MaxBufferedMessagePerTopic,
                bufferedMessageTimeout ?? BufferedMessageTimeout,
                bufferedMessageTimeoutCheckInterval ?? BufferedMessageTimeoutCheckInterval,
                bufferedMessageOverflowStrategy ?? BufferedMessageOverflowStrategy);
        }
        
        public DistributedPubSubSettings WithRole(string role)
            => Copy(role: role);

        public DistributedPubSubSettings WithRoutingLogic(RoutingLogic routingLogic)
            => Copy(routingLogic: routingLogic);

        public DistributedPubSubSettings WithGossipInterval(TimeSpan gossipInterval)
            => Copy(gossipInterval: gossipInterval);

        public DistributedPubSubSettings WithRemovedTimeToLive(TimeSpan removedTtl)
            => Copy(removedTimeToLive: removedTtl);

        public DistributedPubSubSettings WithMaxDeltaElements(int maxDeltaElements)
            => Copy(maxDeltaElements: maxDeltaElements);

        public DistributedPubSubSettings WithSendToDeadLettersWhenNoSubscribers(bool sendToDeadLetterWhenNoSubscribers)
            => Copy(sendToDeadLettersWhenNoSubscribers: sendToDeadLetterWhenNoSubscribers);
        
        public DistributedPubSubSettings WithWaitForSubscribers(bool waitForSubscribers)
            => Copy(waitForSubscribers: waitForSubscribers);
        
        public DistributedPubSubSettings WithMaxBufferedMessagePerTopic(int maxBufferedMessagePerTopic)
            => Copy(maxBufferedMessagePerTopic: maxBufferedMessagePerTopic);
        
        public DistributedPubSubSettings WithBufferedMessageTimeout(TimeSpan bufferedMessageTimeout)
            => Copy(bufferedMessageTimeout: bufferedMessageTimeout);
        
        public DistributedPubSubSettings WithBufferedMessageTimeoutCheckInterval(TimeSpan bufferedMessageTimeoutCheckInterval)
            => Copy(bufferedMessageTimeoutCheckInterval: bufferedMessageTimeoutCheckInterval);
        
    }
}
