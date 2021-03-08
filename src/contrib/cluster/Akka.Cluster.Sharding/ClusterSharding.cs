//-----------------------------------------------------------------------
// <copyright file="ClusterSharding.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Pattern;
using Akka.Util;

namespace Akka.Cluster.Sharding
{
    using EntityId = String;
    using Msg = Object;
    using ShardId = String;

    /// <summary>
    /// Marker trait for remote messages and persistent events/snapshots with special serializer.
    /// </summary>
    public interface IClusterShardingSerializable { }

    /// <summary>
    /// TBD
    /// </summary>
    public class ClusterShardingExtensionProvider : ExtensionIdProvider<ClusterSharding>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override ClusterSharding CreateExtension(ExtendedActorSystem system)
        {
            var extension = new ClusterSharding(system);
            return extension;
        }
    }

    /// <summary>
    /// Convenience implementation of <see cref="IMessageExtractor"/> that
    /// construct ShardId based on the <see cref="MurmurHash.StringHash"/> of the EntityId.
    /// The number of unique shards is limited by the given MaxNumberOfShards.
    /// </summary>
    public abstract class HashCodeMessageExtractor : IMessageExtractor
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly int MaxNumberOfShards;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxNumberOfShards">TBD</param>
        protected HashCodeMessageExtractor(int maxNumberOfShards)
        {
            MaxNumberOfShards = maxNumberOfShards;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public abstract string EntityId(object message);

        /// <summary>
        /// Default implementation pass on the message as is.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public virtual object EntityMessage(object message)
        {
            return message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public virtual string ShardId(object message)
        {
            EntityId id;
            if (message is ShardRegion.StartEntity se)
                id = se.EntityId;
            else
                id = EntityId(message);

            return (Math.Abs(MurmurHash.StringHash(id)) % MaxNumberOfShards).ToString();
        }
    }


#pragma warning disable CS0419 // Ambiguous reference in cref attribute
    /// <summary>
    /// <para>
    /// This extension provides sharding functionality of actors in a cluster.
    /// The typical use case is when you have many stateful actors that together consume
    /// more resources (e.g. memory) than fit on one machine.
    ///   - Distribution: You need to distribute them across several nodes in the cluster
    ///   - Location Transparency: You need to interact with them using their logical identifier,
    /// without having to care about their physical location in the cluster, which can change over time.
    /// </para>
    /// <para>
    /// '''Entities''':
    /// It could for example be actors representing Aggregate Roots in Domain-Driven Design
    /// terminology. Here we call these actors "entities" which typically have persistent
    /// (durable) state, but this feature is not limited to persistent state actors.
    /// </para>
    /// <para>
    /// '''Sharding''':
    /// In this context sharding means that actors with an identifier, or entities,
    /// can be automatically distributed across multiple nodes in the cluster.
    /// </para>
    /// <para>
    /// '''ShardRegion''':
    /// Each entity actor runs only at one place, and messages can be sent to the entity without
    /// requiring the sender to know the location of the destination actor. This is achieved by
    /// sending the messages via a <see cref="Sharding.ShardRegion"/> actor, provided by this extension. The <see cref="Sharding.ShardRegion"/>
    /// knows the shard mappings and routes inbound messages to the entity with the entity id.
    /// Messages to the entities are always sent via the local <see cref="Sharding.ShardRegion"/>.
    /// The <see cref="Sharding.ShardRegion"/> actor is started on each node in the cluster, or group of nodes
    /// tagged with a specific role. The <see cref="Sharding.ShardRegion"/> is created with two application specific
    /// functions to extract the entity identifier and the shard identifier from incoming messages.
    /// </para>
    /// <para>
    /// Typical usage of this extension:
    ///   1. At system startup on each cluster node by registering the supported entity types with
    /// the <see cref="ClusterSharding.Start"/> method
    ///   1. Retrieve the <see cref="Sharding.ShardRegion"/> actor for a named entity type with <see cref="ClusterSharding.ShardRegion"/>
    /// Settings can be configured as described in the `akka.cluster.sharding` section of the `reference.conf`.
    /// </para>
    /// <para>
    /// '''Shard and ShardCoordinator''':
    /// A shard is a group of entities that will be managed together. For the first message in a
    /// specific shard the <see cref="Sharding.ShardRegion"/> requests the location of the shard from a central
    /// <see cref="ShardCoordinator"/>. The <see cref="ShardCoordinator"/> decides which <see cref="Sharding.ShardRegion"/>
    /// owns the shard. The <see cref="Sharding.ShardRegion"/> receives the decided home of the shard
    /// and if that is the <see cref="Sharding.ShardRegion"/> instance itself it will create a local child
    /// actor representing the entity and direct all messages for that entity to it.
    /// If the shard home is another <see cref="Sharding.ShardRegion"/>, instance messages will be forwarded
    /// to that <see cref="Sharding.ShardRegion"/> instance instead. While resolving the location of a
    /// shard, incoming messages for that shard are buffered and later delivered when the
    /// shard location is known. Subsequent messages to the resolved shard can be delivered
    /// to the target destination immediately without involving the <see cref="ShardCoordinator"/>.
    /// To make sure at-most-one instance of a specific entity actor is running somewhere
    /// in the cluster it is important that all nodes have the same view of where the shards
    /// are located. Therefore the shard allocation decisions are taken by the central
    /// <see cref="ShardCoordinator"/>, a cluster singleton, i.e. one instance on
    /// the oldest member among all cluster nodes or a group of nodes tagged with a specific
    /// role. The oldest member can be determined by <see cref="Member.IsOlderThan"/>.
    /// </para>
    /// <para>
    /// '''Shard Rebalancing''':
    /// To be able to use newly added members in the cluster the coordinator facilitates rebalancing
    /// of shards, migrating entities from one node to another. In the rebalance process the
    /// coordinator first notifies all <see cref="Sharding.ShardRegion"/> actors that a handoff for a shard has begun.
    /// <see cref="Sharding.ShardRegion"/> actors will start buffering incoming messages for that shard, as they do when
    /// shard location is unknown. During the rebalance process the coordinator will not answer any
    /// requests for the location of shards that are being rebalanced, i.e. local buffering will
    /// continue until the handoff is complete. The <see cref="Sharding.ShardRegion"/> responsible for the rebalanced shard
    /// will stop all entities in that shard by sending them a <see cref="PoisonPill"/>. When all entities have
    /// been terminated the <see cref="Sharding.ShardRegion"/> owning the entities will acknowledge to the coordinator that
    /// the handoff has completed. Thereafter the coordinator will reply to requests for the location of
    /// the shard, allocate a new home for the shard and then buffered messages in the
    /// <see cref="Sharding.ShardRegion"/> actors are delivered to the new location. This means that the state of the entities
    /// are not transferred or migrated. If the state of the entities are of importance it should be
    /// persistent (durable), e.g. with `akka-persistence` so that it can be recovered at the new
    /// location.
    /// </para>
    /// <para>
    /// '''Shard Allocation''':
    /// The logic deciding which shards to rebalance is defined in a plugable shard allocation
    /// strategy. The default implementation <see cref="LeastShardAllocationStrategy"/>
    /// picks shards for handoff from the <see cref="Sharding.ShardRegion"/> with highest number of previously allocated shards.
    /// They will then be allocated to the <see cref="Sharding.ShardRegion"/> with lowest number of previously allocated shards,
    /// i.e. new members in the cluster. This strategy can be replaced by an application
    /// </para>
    /// <para>
    /// '''Recovery''':
    /// The state of shard locations in the <see cref="ShardCoordinator"/> is stored with `akka-distributed-data` or
    /// `akka-persistence` to survive failures. When a crashed or unreachable coordinator
    /// node has been removed (via down) from the cluster a new <see cref="ShardCoordinator"/> singleton
    /// actor will take over and the state is recovered. During such a failure period shards
    /// with known location are still available, while messages for new (unknown) shards
    /// are buffered until the new <see cref="ShardCoordinator"/> becomes available.
    /// </para>
    /// <para>
    /// '''Delivery Semantics''':
    /// As long as a sender uses the same <see cref="Sharding.ShardRegion"/> actor to deliver messages to an entity
    /// actor the order of the messages is preserved. As long as the buffer limit is not reached
    /// messages are delivered on a best effort basis, with at-most once delivery semantics,
    /// in the same way as ordinary message sending. Reliable end-to-end messaging, with
    /// at-least-once semantics can be added by using `AtLeastOnceDelivery` in `akka-persistence`.
    /// </para>
    /// <para>
    /// Some additional latency is introduced for messages targeted to new or previously
    /// unused shards due to the round-trip to the coordinator. Rebalancing of shards may
    /// also add latency. This should be considered when designing the application specific
    /// shard resolution, e.g. to avoid too fine grained shards.
    /// </para>
    /// <para>
    /// The <see cref="Sharding.ShardRegion"/> actor can also be started in proxy only mode, i.e. it will not
    /// host any entities itself, but knows how to delegate messages to the right location.
    /// </para>
    /// <para>
    /// If the state of the entities are persistent you may stop entities that are not used to
    /// reduce memory consumption. This is done by the application specific implementation of
    /// the entity actors for example by defining receive timeout (<see cref="IActorContext.SetReceiveTimeout"/>).
    /// If a message is already enqueued to the entity when it stops itself the enqueued message
    /// in the mailbox will be dropped. To support graceful passivation without losing such
    /// messages the entity actor can send <see cref="Passivate"/> to its parent <see cref="Sharding.ShardRegion"/>.
    /// The specified wrapped message in <see cref="Passivate"/> will be sent back to the entity, which is
    /// then supposed to stop itself. Incoming messages will be buffered by the <see cref="Sharding.ShardRegion"/>
    /// between reception of <see cref="Passivate"/>` and termination of the entity. Such buffered messages
    /// are thereafter delivered to a new incarnation of the entity.
    /// </para>
    ///
    /// </summary>
    public class ClusterSharding : IExtension
#pragma warning restore CS0419 // Ambiguous reference in cref attribute
    {
        private readonly Lazy<IActorRef> _guardian;
        private readonly ConcurrentDictionary<string, IActorRef> _regions = new ConcurrentDictionary<string, IActorRef>();
        private readonly ConcurrentDictionary<string, IActorRef> _proxies = new ConcurrentDictionary<string, IActorRef>();
        private readonly ExtendedActorSystem _system;
        private readonly Cluster _cluster;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static ClusterSharding Get(ActorSystem system)
        {
            return system.WithExtension<ClusterSharding, ClusterShardingExtensionProvider>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public ClusterSharding(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(DefaultConfig());
            _system.Settings.InjectTopLevelFallback(ClusterSingletonManager.DefaultConfig());
            _system.Settings.InjectTopLevelFallback(DistributedData.DistributedData.DefaultConfig());
            _cluster = Cluster.Get(_system);
            Settings = ClusterShardingSettings.Create(system);

            _guardian = new Lazy<IActorRef>(() =>
            {
                var guardianName = system.Settings.Config.GetString("akka.cluster.sharding.guardian-name");
                var dispatcher = system.Settings.Config.GetString("akka.cluster.sharding.use-dispatcher");
                if (string.IsNullOrEmpty(dispatcher)) dispatcher = Dispatchers.InternalDispatcherId;
                return system.SystemActorOf(Props.Create(() => new ClusterShardingGuardian()).WithDispatcher(dispatcher), guardianName);
            });
        }

        /// <summary>
        /// Gets object representing settings for the current cluster sharding plugin.
        /// </summary>
        public ClusterShardingSettings Settings { get; }

        /// <summary>
        /// Default HOCON settings for cluster sharding.
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<ClusterSharding>("Akka.Cluster.Sharding.reference.conf");
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return InternalStart(
                typeName,
                _ => entityProps,
                settings,
                extractEntityId,
                extractShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return InternalStartAsync(
                typeName,
                _ => entityProps,
                settings,
                extractEntityId,
                extractShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            return Start(
                typeName,
                entityProps,
                settings,
                extractEntityId,
                extractShardId,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            return StartAsync(
                typeName,
                entityProps,
                settings,
                extractEntityId,
                extractShardId,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return Start(
                typeName,
                entityProps,
                settings,
                messageExtractor.ToExtractEntityId(),
                messageExtractor.ShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return StartAsync(
                typeName,
                entityProps,
                settings,
                messageExtractor.ToExtractEntityId(),
                messageExtractor.ShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor)
        {
            return Start(
                typeName,
                entityProps,
                settings,
                messageExtractor,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityProps">
        /// The <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Props entityProps,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor)
        {
            return StartAsync(
                typeName,
                entityProps,
                settings,
                messageExtractor,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }


        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return InternalStart(
                typeName,
                entityPropsFactory,
                settings,
                extractEntityId,
                extractShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return InternalStartAsync(
                typeName,
                entityPropsFactory,
                settings,
                extractEntityId,
                extractShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        private IActorRef InternalStart(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            if (settings.ShouldHostShard(_cluster))
            {
                if (_regions.TryGetValue(typeName, out var shardRegion))
                {
                    // already started, use cached ActorRef
                    return shardRegion;
                }

                // it's ok to Start several time, the guardian will deduplicate concurrent requests
                var timeout = _system.Settings.CreationTimeout;
                var startMsg = new ClusterShardingGuardian.Start(
                    typeName,
                    entityPropsFactory,
                    settings,
                    extractEntityId,
                    extractShardId,
                    allocationStrategy,
                    handOffStopMessage);

                var reply = _guardian.Value.Ask(startMsg, timeout).Result;
                switch (reply)
                {
                    case ClusterShardingGuardian.Started started:
                        shardRegion = started.ShardRegion;
                        _regions.TryAdd(typeName, shardRegion);
                        return shardRegion;

                    case Status.Failure failure:
                        ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                        return ActorRefs.Nobody;

                    default:
                        throw new ActorInitializationException($"Unsupported guardian response: {reply}");
                }
            }
            else
            {
                _cluster.System.Log.Debug("Starting Shard Region Proxy [{0}] (no actors will be hosted on this node)...", typeName);
                return StartProxy(
                    typeName,
                    settings.Role,
                    extractEntityId,
                    extractShardId);
            }
        }

        private async Task<IActorRef> InternalStartAsync(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            if (settings.ShouldHostShard(_cluster))
            {
                if (_regions.TryGetValue(typeName, out var shardRegion))
                {
                    // already started, use cached ActorRef
                    return shardRegion;
                }

                // it's ok to Start several time, the guardian will deduplicate concurrent requests
                var timeout = _system.Settings.CreationTimeout;
                var startMsg = new ClusterShardingGuardian.Start(
                    typeName,
                    entityPropsFactory,
                    settings,
                    extractEntityId,
                    extractShardId,
                    allocationStrategy,
                    handOffStopMessage);

                var reply = await _guardian.Value.Ask(startMsg, timeout).ConfigureAwait(false);
                switch (reply)
                {
                    case ClusterShardingGuardian.Started started:
                        shardRegion = started.ShardRegion;
                        _regions.TryAdd(typeName, shardRegion);
                        return shardRegion;

                    case Status.Failure failure:
                        ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                        return ActorRefs.Nobody;

                    default:
                        throw new ActorInitializationException($"Unsupported guardian response: {reply}");
                }
            }
            else
            {
                _cluster.System.Log.Debug("Starting Shard Region Proxy [{0}] (no actors will be hosted on this node)...", typeName);
                return StartProxy(
                    typeName,
                    settings.Role,
                    extractEntityId,
                    extractShardId);
            }
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            return Start(
                typeName,
                entityPropsFactory,
                settings,
                extractEntityId,
                extractShardId,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            return StartAsync(
                typeName,
                entityPropsFactory,
                settings,
                extractEntityId,
                extractShardId,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return Start(
                typeName,
                entityPropsFactory,
                settings,
                messageExtractor.ToExtractEntityId(),
                messageExtractor.ShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <param name="allocationStrategy">Possibility to use a custom shard allocation and
        /// rebalancing logic</param>
        /// <param name="handOffStopMessage">
        /// The message that will be sent to entities when they are to be stopped
        /// for a rebalance or graceful shutdown of a <see cref="Sharding.ShardRegion"/>, e.g. <see cref="PoisonPill"/>.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor,
            IShardAllocationStrategy allocationStrategy,
            object handOffStopMessage)
        {
            return StartAsync(
                typeName,
                entityPropsFactory,
                settings,
                messageExtractor.ToExtractEntityId(),
                messageExtractor.ShardId,
                allocationStrategy,
                handOffStopMessage);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef Start(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor)
        {
            return Start(
                typeName,
                entityPropsFactory,
                settings,
                messageExtractor,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type by defining the <see cref="Actor.Props"/> of the entity actor
        /// and functions to extract entity and shard identifier from messages. The <see cref="Sharding.ShardRegion"/> actor
        /// for this type can later be retrieved with the <see cref="ShardRegion"/> method.
        ///
        /// This method will start a <see cref="ShardRegion"/> in proxy mode when there is no match between the roles of
        /// the current cluster node and the role specified in <see cref="ClusterShardingSettings"/> passed to this method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        ///
        /// </summary>
        /// <param name="typeName">The name of the entity type</param>
        /// <param name="entityPropsFactory">
        /// Function that, given an entity id, returns the <see cref="Actor.Props"/> of the entity actors that will be created by the <see cref="Sharding.ShardRegion"/>
        /// </param>
        /// <param name="settings">Configuration settings, see <see cref="ClusterShardingSettings"/></param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the cluster member doesn't have the role specified in <paramref name="settings"/>.
        /// </exception>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartAsync(
            string typeName,
            Func<string, Props> entityPropsFactory,
            ClusterShardingSettings settings,
            IMessageExtractor messageExtractor)
        {
            return StartAsync(
                typeName,
                entityPropsFactory,
                settings,
                messageExtractor,
                DefaultShardAllocationStrategy(settings),
                PoisonPill.Instance);
        }

        /// <summary>
        /// Register a named entity type <see cref="Sharding.ShardRegion"/> on this node that will run in proxy only mode,
        /// i.e. it will delegate messages to other <see cref="Sharding.ShardRegion"/> actors on other nodes, but not host any
        /// entity actors itself. The <see cref="Sharding.ShardRegion"/> actor for this type can later be retrieved with the
        /// <see cref="ShardRegion"/> method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        /// </summary>
        /// <param name="typeName">The name of the entity type.</param>
        /// <param name="role">
        /// Specifies that this entity type is located on cluster nodes with a specific role.
        /// If the role is not specified all nodes in the cluster are used.
        /// </param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef StartProxy(
            string typeName,
            string role,
            ExtractEntityId extractEntityId,
            ExtractShardId extractShardId)
        {
            if (_proxies.TryGetValue(typeName, out var shardProxy))
            {
                // already started, use cached ActorRef
                return shardProxy;
            }
            // it's ok to StartProxy several time, the guardian will deduplicate concurrent requests
            var timeout = _system.Settings.CreationTimeout;
            var settings = ClusterShardingSettings.Create(_system).WithRole(role);
            var startMsg = new ClusterShardingGuardian.StartProxy(typeName, settings, extractEntityId, extractShardId);
            var reply = _guardian.Value.Ask(startMsg, timeout).Result;
            switch (reply)
            {
                case ClusterShardingGuardian.Started started:
                    shardProxy = started.ShardRegion;
                    _proxies.TryAdd(typeName, shardProxy);
                    return shardProxy;

                case Status.Failure failure:
                    ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                    return ActorRefs.Nobody;

                default:
                    throw new ActorInitializationException($"Unsupported guardian response: {reply}");
            }
        }

        /// <summary>
        /// Register a named entity type <see cref="Sharding.ShardRegion"/> on this node that will run in proxy only mode,
        /// i.e. it will delegate messages to other <see cref="Sharding.ShardRegion"/> actors on other nodes, but not host any
        /// entity actors itself. The <see cref="Sharding.ShardRegion"/> actor for this type can later be retrieved with the
        /// <see cref="ShardRegion"/> method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        /// </summary>
        /// <param name="typeName">The name of the entity type.</param>
        /// <param name="role">
        /// Specifies that this entity type is located on cluster nodes with a specific role.
        /// If the role is not specified all nodes in the cluster are used.
        /// </param>
        /// <param name="extractEntityId">
        /// Partial function to extract the entity id and the message to send to the
        /// entity from the incoming message, if the partial function does not match the message will
        /// be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        /// </param>
        /// <param name="extractShardId">
        /// Function to determine the shard id for an incoming message, only messages
        /// that passed the `extractEntityId` will be used
        /// </param>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public async Task<IActorRef> StartProxyAsync(string typeName, string role, ExtractEntityId extractEntityId, ExtractShardId extractShardId)
        {
            if (_proxies.TryGetValue(typeName, out var shardProxy))
            {
                // already started, use cached ActorRef
                return shardProxy;
            }
            // it's ok to StartProxy several time, the guardian will deduplicate concurrent requests
            var timeout = _system.Settings.CreationTimeout;
            var settings = ClusterShardingSettings.Create(_system).WithRole(role);
            var startMsg = new ClusterShardingGuardian.StartProxy(typeName, settings, extractEntityId, extractShardId);
            var reply = await _guardian.Value.Ask(startMsg, timeout).ConfigureAwait(false);
            switch (reply)
            {
                case ClusterShardingGuardian.Started started:
                    shardProxy = started.ShardRegion;
                    _proxies.TryAdd(typeName, shardProxy);
                    return shardProxy;

                case Status.Failure failure:
                    ExceptionDispatchInfo.Capture(failure.Cause).Throw();
                    return ActorRefs.Nobody;

                default:
                    throw new ActorInitializationException($"Unsupported guardian response: {reply}");
            }
        }

        /// <summary>
        /// Register a named entity type <see cref="Sharding.ShardRegion"/> on this node that will run in proxy only mode,
        /// i.e. it will delegate messages to other <see cref="Sharding.ShardRegion"/> actors on other nodes, but not host any
        /// entity actors itself. The <see cref="Sharding.ShardRegion"/> actor for this type can later be retrieved with the
        /// <see cref="ShardRegion"/> method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        /// </summary>
        /// <param name="typeName">The name of the entity type.</param>
        /// <param name="role">
        /// Specifies that this entity type is located on cluster nodes with a specific role.
        /// If the role is not specified all nodes in the cluster are used.
        /// </param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public IActorRef StartProxy(string typeName, string role, IMessageExtractor messageExtractor)
        {
            return StartProxy(
                typeName,
                role,
                messageExtractor.ToExtractEntityId(),
                messageExtractor.ShardId);
        }

        /// <summary>
        /// Register a named entity type <see cref="Sharding.ShardRegion"/> on this node that will run in proxy only mode,
        /// i.e. it will delegate messages to other <see cref="Sharding.ShardRegion"/> actors on other nodes, but not host any
        /// entity actors itself. The <see cref="Sharding.ShardRegion"/> actor for this type can later be retrieved with the
        /// <see cref="ShardRegion"/> method.
        ///
        /// Some settings can be configured as described in the `akka.cluster.sharding` section
        /// of the `reference.conf`.
        /// </summary>
        /// <param name="typeName">The name of the entity type.</param>
        /// <param name="role">
        /// Specifies that this entity type is located on cluster nodes with a specific role.
        /// If the role is not specified all nodes in the cluster are used.
        /// </param>
        /// <param name="messageExtractor">
        /// Functions to extract the entity id, shard id, and the message to send to the entity from the incoming message.
        /// </param>
        /// <returns>The actor ref of the <see cref="Sharding.ShardRegion"/> that is to be responsible for the shard.</returns>
        public Task<IActorRef> StartProxyAsync(string typeName, string role, IMessageExtractor messageExtractor)
        {
            return StartProxyAsync(
                typeName,
                role,
                messageExtractor.ToExtractEntityId(),
                messageExtractor.ShardId);
        }

        /// <summary>
        /// Get all currently defined sharding type names.
        /// </summary>
        public ImmutableHashSet<EntityId> ShardTypeNames => _regions.Keys.ToImmutableHashSet();

#pragma warning disable CS0419 // Ambiguous reference in cref attribute
        /// <summary>
        /// Retrieve the actor reference of the <see cref="Sharding.ShardRegion"/> actor responsible for the named entity type.
        /// The entity type must be registered with the <see cref="Start"/> or <see cref="StartProxy"/> method before it
        /// can be used here. Messages to the entity is always sent via the <see cref="Sharding.ShardRegion"/>.
        /// </summary>
        /// <param name="typeName">TBD</param>
        /// <exception cref="ArgumentException">
        /// Thrown when shard region for provided <paramref name="typeName"/> has not been started yet.
        /// </exception>
        /// <returns>TBD</returns>
        public IActorRef ShardRegion(string typeName)
#pragma warning restore CS0419 // Ambiguous reference in cref attribute
        {
            if (_regions.TryGetValue(typeName, out var region))
                return region;
            if (_proxies.TryGetValue(typeName, out region))
                return region;

            throw new ArgumentException(
                $"Shard type [{typeName}] must be started first. Started regions [{string.Join(", ", _regions.Keys)}] proxies ${string.Join(", ", _proxies.Keys)}");
        }


#pragma warning disable CS0419 // Ambiguous reference in cref attribute
        /// <summary>
        /// Retrieve the actor reference of the <see cref="Sharding.ShardRegion"/> actor that will act as a proxy to the
        /// named entity type running in another data center. A proxy within the same data center can be accessed
        /// with <see cref="Sharding.ShardRegion"/> instead of this method. The entity type must be registered with the
        /// <see cref="StartProxy"/> method before it can be used here. Messages to the entity is always sent
        /// via the <see cref="Sharding.ShardRegion"/>.
        /// </summary>
        /// <param name="typeName"></param>
        /// <returns></returns>
        public IActorRef ShardRegionProxy(string typeName)
#pragma warning restore CS0419 // Ambiguous reference in cref attribute
        {
            if (_proxies.TryGetValue(typeName, out var proxy))
                return proxy;
            throw new ArgumentException($"Shard type [{typeName}] must be started first");
        }

        /// <summary>
        /// The default <see cref="IShardAllocationStrategy"/> is configured by `least-shard-allocation-strategy` properties.
        /// </summary>
        /// <param name="settings"></param>
        /// <returns></returns>
        public IShardAllocationStrategy DefaultShardAllocationStrategy(ClusterShardingSettings settings)
        {

            if (settings.TuningParameters.LeastShardAllocationAbsoluteLimit > 0)
            {
                // new algorithm
                var absoluteLimit = settings.TuningParameters.LeastShardAllocationAbsoluteLimit;
                var relativeLimit = settings.TuningParameters.LeastShardAllocationRelativeLimit;
                return ShardAllocationStrategy.LeastShardAllocationStrategy(absoluteLimit, relativeLimit);
            }
            else
            {
                // old algorithm
                var threshold = settings.TuningParameters.LeastShardAllocationRebalanceThreshold;
                var maxSimultaneousRebalance = settings.TuningParameters.LeastShardAllocationMaxSimultaneousRebalance;
                return new LeastShardAllocationStrategy(threshold, maxSimultaneousRebalance);
            }
        }
    }

    /// <summary>
    /// Interface of the function used by the <see cref="ShardRegion"/> to
    /// extract the shard id from an incoming message.
    /// Only messages that passed the <see cref="ExtractEntityId"/> will be used
    /// as input to this function.
    /// </summary>
    public delegate ShardId ExtractShardId(Msg message);

    /// <summary>
    /// Interface of the partial function used by the <see cref="ShardRegion"/> to
    /// extract the entity id and the message to send to the entity from an
    /// incoming message. The implementation is application specific.
    /// If the partial function does not match the message will be
    /// `unhandled`, i.e. posted as `Unhandled` messages on the event stream.
    /// Note that the extracted  message does not have to be the same as the incoming
    /// message to support wrapping in message envelope that is unwrapped before
    /// sending to the entity actor.
    /// </summary>
    public delegate Option<(EntityId, Msg)> ExtractEntityId(Msg message);

    /// <summary>
    /// Interface of functions to extract entity id,  shard id, and the message to send
    /// to the entity from an incoming message.
    /// </summary>
    public interface IMessageExtractor
    {
        /// <summary>
        /// Extract the entity id from an incoming <paramref name="message"/>.
        /// If <see langword="null"/> is returned the message will be `unhandled`, i.e. posted as `Unhandled`
        ///  messages on the event stream
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        EntityId EntityId(object message);

        /// <summary>
        /// Extract the message to send to the entity from an incoming <paramref name="message"/>.
        /// Note that the extracted message does not have to be the same as the incoming
        /// message to support wrapping in message envelope that is unwrapped before
        /// sending to the entity actor.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        object EntityMessage(object message);

        /// <summary>
        /// Extract the shard id from an incoming <paramref name="message"/>. Only messages that
        /// passed the <see cref="EntityId"/> method will be used as input to this method.
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        string ShardId(object message);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal static class Extensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static ExtractEntityId ToExtractEntityId(this IMessageExtractor self)
        {
            ExtractEntityId extractEntityId = msg =>
            {
                if (self.EntityId(msg) != null)
                    return (self.EntityId(msg), self.EntityMessage(msg));

                return Option<(string, object)>.None;
            };

            return extractEntityId;
        }
    }
}
