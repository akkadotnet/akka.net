using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence;
using Akka.ClusterSingleton;

namespace Akka.ClusterSharding
{
    /**
    * This extension provides sharding functionality of actors in a cluster.
    * The typical use case is when you have many stateful actors that together consume
    * more resources (e.g. memory) than fit on one machine. You need to distribute them across
    * several nodes in the cluster and you want to be able to interact with them using their
    * logical identifier, but without having to care about their physical location in the cluster,
    * which might also change over time. It could for example be actors representing Aggregate Roots in
    * Domain-Driven Design terminology. Here we call these actors "entries". These actors
    * typically have persistent (durable) state, but this feature is not limited to
    * actors with persistent state.
    *
    * In this context sharding means that actors with an identifier, so called entries,
    * can be automatically distributed across multiple nodes in the cluster. Each entry
    * actor runs only at one place, and messages can be sent to the entry without requiring
    * the sender to know the location of the destination actor. This is achieved by sending
    * the messages via a [[ShardRegion]] actor provided by this extension, which knows how
    * to route the message with the entry id to the final destination.
    *
    * This extension is supposed to be used by first, typically at system startup on each node
    * in the cluster, registering the supported entry types with the [[ClusterSharding#start]]
    * method and then the `ShardRegion` actor for a named entry type can be retrieved with
    * [[ClusterSharding#shardRegion]]. Messages to the entries are always sent via the local
    * `ShardRegion`. Some settings can be configured as described in the `akka.contrib.cluster.sharding`
    * section of the `reference.conf`.
    *
    * The `ShardRegion` actor is started on each node in the cluster, or group of nodes
    * tagged with a specific role. The `ShardRegion` is created with two application specific
    * functions to extract the entry identifier and the shard identifier from incoming messages.
    * A shard is a group of entries that will be managed together. For the first message in a
    * specific shard the `ShardRegion` request the location of the shard from a central coordinator,
    * the [[ShardCoordinator]]. The `ShardCoordinator` decides which `ShardRegion` that
    * owns the shard. The `ShardRegion` receives the decided home of the shard
    * and if that is the `ShardRegion` instance itself it will create a local child
    * actor representing the entry and direct all messages for that entry to it.
    * If the shard home is another `ShardRegion` instance messages will be forwarded
    * to that `ShardRegion` instance instead. While resolving the location of a
    * shard incoming messages for that shard are buffered and later delivered when the
    * shard home is known. Subsequent messages to the resolved shard can be delivered
    * to the target destination immediately without involving the `ShardCoordinator`.
    *
    * To make sure that at most one instance of a specific entry actor is running somewhere
    * in the cluster it is important that all nodes have the same view of where the shards
    * are located. Therefore the shard allocation decisions are taken by the central
    * `ShardCoordinator`, which is running as a cluster singleton, i.e. one instance on
    * the oldest member among all cluster nodes or a group of nodes tagged with a specific
    * role. The oldest member can be determined by [[akka.cluster.Member#isOlderThan]].
    *
    * The logic that decides where a shard is to be located is defined in a pluggable shard
    * allocation strategy. The default implementation [[ShardCoordinator.LeastShardAllocationStrategy]]
    * allocates new shards to the `ShardRegion` with least number of previously allocated shards.
    * This strategy can be replaced by an application specific implementation.
    *
    * To be able to use newly added members in the cluster the coordinator facilitates rebalancing
    * of shards, i.e. migrate entries from one node to another. In the rebalance process the
    * coordinator first notifies all `ShardRegion` actors that a handoff for a shard has started.
    * That means they will start buffering incoming messages for that shard, in the same way as if the
    * shard location is unknown. During the rebalance process the coordinator will not answer any
    * requests for the location of shards that are being rebalanced, i.e. local buffering will
    * continue until the handoff is completed. The `ShardRegion` responsible for the rebalanced shard
    * will stop all entries in that shard by sending `PoisonPill` to them. When all entries have
    * been terminated the `ShardRegion` owning the entries will acknowledge the handoff as completed
    * to the coordinator. Thereafter the coordinator will reply to requests for the location of
    * the shard and thereby allocate a new home for the shard and then buffered messages in the
    * `ShardRegion` actors are delivered to the new location. This means that the state of the entries
    * are not transferred or migrated. If the state of the entries are of importance it should be
    * persistent (durable), e.g. with `akka-persistence`, so that it can be recovered at the new
    * location.
    *
    * The logic that decides which shards to rebalance is defined in a pluggable shard
    * allocation strategy. The default implementation [[ShardCoordinator.LeastShardAllocationStrategy]]
    * picks shards for handoff from the `ShardRegion` with most number of previously allocated shards.
    * They will then be allocated to the `ShardRegion` with least number of previously allocated shards,
    * i.e. new members in the cluster. There is a configurable threshold of how large the difference
    * must be to begin the rebalancing. This strategy can be replaced by an application specific
    * implementation.
    *
    * The state of shard locations in the `ShardCoordinator` is persistent (durable) with
    * `akka-persistence` to survive failures. Since it is running in a cluster `akka-persistence`
    * must be configured with a distributed journal. When a crashed or unreachable coordinator
    * node has been removed (via down) from the cluster a new `ShardCoordinator` singleton
    * actor will take over and the state is recovered. During such a failure period shards
    * with known location are still available, while messages for new (unknown) shards
    * are buffered until the new `ShardCoordinator` becomes available.
    *
    * As long as a sender uses the same `ShardRegion` actor to deliver messages to an entry
    * actor the order of the messages is preserved. As long as the buffer limit is not reached
    * messages are delivered on a best effort basis, with at-most once delivery semantics,
    * in the same way as ordinary message sending. Reliable end-to-end messaging, with
    * at-least-once semantics can be added by using `AtLeastOnceDelivery` in `akka-persistence`.
    *
    * Some additional latency is introduced for messages targeted to new or previously
    * unused shards due to the round-trip to the coordinator. Rebalancing of shards may
    * also add latency. This should be considered when designing the application specific
    * shard resolution, e.g. to avoid too fine grained shards.
    *
    * The `ShardRegion` actor can also be started in proxy only mode, i.e. it will not
    * host any entries itself, but knows how to delegate messages to the right location.
    * A `ShardRegion` starts in proxy only mode if the roles of the node does not include
    * the node role specified in `akka.contrib.cluster.sharding.role` config property
    * or if the specified `entryProps` is `None`/`null`.
    *
    * If the state of the entries are persistent you may stop entries that are not used to
    * reduce memory consumption. This is done by the application specific implementation of
    * the entry actors for example by defining receive timeout (`context.setReceiveTimeout`).
    * If a message is already enqueued to the entry when it stops itself the enqueued message
    * in the mailbox will be dropped. To support graceful passivation without loosing such
    * messages the entry actor can send [[ShardRegion.Passivate]] to its parent `ShardRegion`.
    * The specified wrapped message in `Passivate` will be sent back to the entry, which is
    * then supposed to stop itself. Incoming messages will be buffered by the `ShardRegion`
    * between reception of `Passivate` and termination of the entry. Such buffered messages
    * are thereafter delivered to a new incarnation of the entry.
    *
    */

    ///**
    //* Marker type of entry identifier (`String`).
    //*/
    //type EntryId = String
    ///**
    //* Marker type of shard identifier (`String`).
    //*/
    //type ShardId = String
    ///**
    //* Marker type of application messages (`Any`).
    //*/
    //type Msg = Any

    using Msg = Object;
    using EntryId = String;
    using ShardId = String;

    public class ClusterSharding : IExtension
    {
        private readonly IActorRef _guardian;

        private readonly ConcurrentDictionary<string, IActorRef> _regions =
            new ConcurrentDictionary<string, IActorRef>();

        private readonly ExtendedActorSystem _system;
        private Cluster.Cluster _cluster;

        public ClusterSharding(ExtendedActorSystem system)
        {
            _system = system;
            Settings = new ClusterShardingSettings(system);

            //TODO: scala is lazy 
            _guardian = system.ActorOf<ClusterShardingGuardian>(Settings.GuardianName);
        }

        public ClusterShardingSettings Settings { get; private set; }

        /**
        * Scala API: Register a named entry type by defining the [[akka.actor.Props]] of the entry actor
        * and functions to extract entry and shard identifier from messages. The [[ShardRegion]] actor
        * for this type can later be retrieved with the [[#shardRegion]] method.
        *
        * Some settings can be configured as described in the `akka.contrib.cluster.sharding` section
        * of the `reference.conf`.
        *
        * @param typeName the name of the entry type
        * @param entryProps the `Props` of the entry actors that will be created by the `ShardRegion`,
        *   if not defined (None) the `ShardRegion` on this node will run in proxy only mode, i.e.
        *   it will delegate messages to other `ShardRegion` actors on other nodes, but not host any
        *   entry actors itself
        * @param roleOverride specifies that this entry type requires cluster nodes with a specific role.
        *   if not defined (None), then defaults to standard behavior of using Role (if any) from configuration
        * @param rememberEntries true if entry actors shall created be automatically restarted upon `Shard`
        *   restart. i.e. if the `Shard` is started on a different `ShardRegion` due to rebalance or crash.
        * @param idExtractor partial function to extract the entry id and the message to send to the
        *   entry from the incoming message, if the partial function does not match the message will
        *   be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        * @param shardResolver function to determine the shard id for an incoming message, only messages
        *   that passed the `idExtractor` will be used
        * @param allocationStrategy possibility to use a custom shard allocation and
        *   rebalancing logic
        * @return the actor ref of the [[ShardRegion]] that is to be responsible for the shard
        */

        public IActorRef Start(
            string typeName,
            Props entryProps,
            string roleOverride,
            bool rememberEntries,
            IdExtractor idExtractor,
            ShardResolver shardResolver,
            ShardAllocationStrategy allocationStrategy)
        {
            //Not used in scala?
            var resolvedRole = roleOverride ?? Settings.Role;

            var timeout = _system.Settings.CreationTimeout;
            var startMsg = new Start(typeName, entryProps, roleOverride, rememberEntries, idExtractor, shardResolver,
                allocationStrategy);

            var started = _guardian.Ask<Started>(startMsg, timeout).Result;
            var shardRegion = started.ShardRegion;
            _regions.TryAdd(typeName, shardRegion);
            return shardRegion;
        }

        /**
        * Register a named entry type by defining the [[akka.actor.Props]] of the entry actor and
        * functions to extract entry and shard identifier from messages. The [[ShardRegion]] actor
        * for this type can later be retrieved with the [[#shardRegion]] method.
        *
        * The default shard allocation strategy [[ShardCoordinator.LeastShardAllocationStrategy]]
        * is used.
        *
        * Some settings can be configured as described in the `akka.contrib.cluster.sharding` section
        * of the `reference.conf`.
        *
        * @param typeName the name of the entry type
        * @param entryProps the `Props` of the entry actors that will be created by the `ShardRegion`,
        *   if not defined (None) the `ShardRegion` on this node will run in proxy only mode, i.e.
        *   it will delegate messages to other `ShardRegion` actors on other nodes, but not host any
        *   entry actors itself
        * @param roleOverride specifies that this entry type requires cluster nodes with a specific role.
        *   if not defined (None), then defaults to standard behavior of using Role (if any) from configuration
        * @param rememberEntries true if entry actors shall created be automatically restarted upon `Shard`
        *   restart. i.e. if the `Shard` is started on a different `ShardRegion` due to rebalance or crash.
        * @param idExtractor partial function to extract the entry id and the message to send to the
        *   entry from the incoming message, if the partial function does not match the message will
        *   be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        * @param shardResolver function to determine the shard id for an incoming message, only messages
        *   that passed the `idExtractor` will be used
        * @return the actor ref of the [[ShardRegion]] that is to be responsible for the shard
        */

        public IActorRef Start(
            string typeName,
            Props entryProps,
            string roleOverride,
            bool rememberEntries,
            IdExtractor idExtractor,
            ShardResolver shardResolver)
        {
            return Start(typeName, entryProps, roleOverride, rememberEntries, idExtractor, shardResolver,
                new LeastShardAllocationStrategy(
                    Settings.LeastShardAllocationRebalanceThreshold,
                    Settings.LeastShardAllocationMaxSimultaneousRebalance));
        }

        /**
         * Java API: Register a named entry type by defining the [[akka.actor.Props]] of the entry actor
         * and functions to extract entry and shard identifier from messages. The [[ShardRegion]] actor
         * for this type can later be retrieved with the [[#shardRegion]] method.
         *
         * Some settings can be configured as described in the `akka.contrib.cluster.sharding` section
         * of the `reference.conf`.
         *
         * @param typeName the name of the entry type
         * @param entryProps the `Props` of the entry actors that will be created by the `ShardRegion`,
         *   if not defined (null) the `ShardRegion` on this node will run in proxy only mode, i.e.
         *   it will delegate messages to other `ShardRegion` actors on other nodes, but not host any
         *   entry actors itself
         * @param roleOverride specifies that this entry type requires cluster nodes with a specific role.
         *   if not defined (None), then defaults to standard behavior of using Role (if any) from configuration
         * @param rememberEntries true if entry actors shall created be automatically restarted upon `Shard`
         *   restart. i.e. if the `Shard` is started on a different `ShardRegion` due to rebalance or crash.
         * @param messageExtractor functions to extract the entry id, shard id, and the message to send to the
         *   entry from the incoming message
         * @param allocationStrategy possibility to use a custom shard allocation and
         *   rebalancing logic
         * @return the actor ref of the [[ShardRegion]] that is to be responsible for the shard
         */

        public IActorRef Start(string typeName, Props entryProps, string roleOverride, bool rememberEntries,
            IMessageExtractor messageExtractor, ShardAllocationStrategy allocationStrategy)
        {
            IdExtractor idExtractor = messageExtractor.ToIdExtractor();
            ShardResolver shardResolver = ShardResolvers.Default;

            return Start(typeName, entryProps, roleOverride, rememberEntries, idExtractor, shardResolver,
                allocationStrategy);
        }

        /**
        * Java API: Register a named entry type by defining the [[akka.actor.Props]] of the entry actor
        * and functions to extract entry and shard identifier from messages. The [[ShardRegion]] actor
        * for this type can later be retrieved with the [[#shardRegion]] method.
        *
        * The default shard allocation strategy [[ShardCoordinator.LeastShardAllocationStrategy]]
        * is used.
        *
        * Some settings can be configured as described in the `akka.contrib.cluster.sharding` section
        * of the `reference.conf`.
        *
        * @param typeName the name of the entry type
        * @param entryProps the `Props` of the entry actors that will be created by the `ShardRegion`,
        *   if not defined (null) the `ShardRegion` on this node will run in proxy only mode, i.e.
        *   it will delegate messages to other `ShardRegion` actors on other nodes, but not host any
        *   entry actors itself
        * @param roleOverride specifies that this entry type requires cluster nodes with a specific role.
        *   if not defined (None), then defaults to standard behavior of using Role (if any) from configuration
        * @param rememberEntries true if entry actors shall created be automatically restarted upon `Shard`
        *   restart. i.e. if the `Shard` is started on a different `ShardRegion` due to rebalance or crash.
        * @param messageExtractor functions to extract the entry id, shard id, and the message to send to the
        *   entry from the incoming message
        * @return the actor ref of the [[ShardRegion]] that is to be responsible for the shard
        */

        public IActorRef Start(string typeName, Props entryProps, string roleOverride, bool rememberEntries,
            IMessageExtractor messageExtractor)
        {
            return Start(typeName, entryProps, roleOverride, rememberEntries, messageExtractor,
                new LeastShardAllocationStrategy(Settings.LeastShardAllocationRebalanceThreshold,
                    Settings.LeastShardAllocationMaxSimultaneousRebalance));
        }

        /**
        * Retrieve the actor reference of the [[ShardRegion]] actor responsible for the named entry type.
        * The entry type must be registered with the [[#start]] method before it can be used here.
        * Messages to the entry is always sent via the `ShardRegion`.
        */

        public IActorRef ShardingRegion(string typeName)
        {
            IActorRef region;
            if (_regions.TryGetValue(typeName, out region))
            {
                return region;
            }
            throw new ArgumentException(string.Format("Shard type {0} must be started first", typeName));
        }
    }


    /**
    * INTERNAL API.
    */

    public class Started : INoSerializationVerificationNeeded
    {
        public IActorRef ShardRegion { get; private set; }

        public Started(IActorRef shardRegion)
        {
            ShardRegion = shardRegion;
        }
    }

    public class Start
    {
        public string TypeName { get; private set; }
        public Props EntryProps { get; private set; }
        public string RoleOverride { get; private set; }
        public bool RememberEntries { get; private set; }
        public IdExtractor IdExtractor { get; private set; }
        public ShardResolver ShardResolver { get; private set; }
        public ShardAllocationStrategy AllocationStrategy { get; private set; }

        public Start(string typeName, Props entryProps, string roleOverride, bool rememberEntries,
            IdExtractor idIdExtractor, ShardResolver shardResolver, ShardAllocationStrategy allocationStrategy)
        {
            TypeName = typeName;
            EntryProps = entryProps;
            RoleOverride = roleOverride;
            RememberEntries = rememberEntries;
            IdExtractor = idIdExtractor;
            ShardResolver = shardResolver;
            AllocationStrategy = allocationStrategy;
        }
    }

    /**
    * INTERNAL API. [[ShardRegion]] and [[ShardCoordinator]] actors are createad as children
    * of this actor.
    */

    public class ClusterShardingGuardian : ReceiveActor
    {
        public ClusterShardingGuardian()
        {
            var cluster = Context.System.GetExtension<Cluster.Cluster>();
            var clusterSharding = Context.System.GetExtension<ClusterSharding>();
            var settings = clusterSharding.Settings;

            Receive<Start>(start =>
            {
                //TODO: escape data string or escape uri string?
                var encName = Uri.EscapeDataString(start.TypeName);
                var coordinatorSingletonManagerName = encName + "Coordinator";
                var coordinatorPath =
                    (Self.Path/coordinatorSingletonManagerName/"singleton"/"coordinator").ToStringWithoutAddress();

                var shardRegion = Context.Child(encName);

                if (shardRegion.Equals(ActorRefs.Nobody))
                {
                    var hasRequiredRole = settings.HasNecessaryClusterRole(start.RoleOverride);
                    if (hasRequiredRole && Context.Child(coordinatorSingletonManagerName).Equals(ActorRefs.Nobody))
                    {
                        /*
handOffTimeout = HandOffTimeout, 
            shardStartTimeout = ShardStartTimeout,
            rebalanceInterval = RebalanceInterval, 
            snapshotInterval = SnapshotInterval, 
            allocationStrategy)
                         */
                        var coordinatorProps = ShardCoordinator.Props(
                            handOffTimeout: settings.HandOffTimeout,
                            shardStartTimeout: settings.ShardStartTimeout,
                            rebalanceInterval: settings.RebalanceInterval,
                            snapshotInterval: settings.SnapshotInterval,
                            allocationStrategy: start.AllocationStrategy);

                        var singletonProps = ShardCoordinatorSupervisor.Props(settings.CoordinatorFailureBackoff, coordinatorProps);

                        Context.ActorOf(ClusterSingletonManager.Props(
                            singletonProps,
                            singletonName: "singleton",
                            terminationMessage: PoisonPill.Instance,
                            role: settings.Role),
                            name: coordinatorSingletonManagerName);
                    }

                    shardRegion = Context.ActorOf(ShardRegion.Props(
                        typeName: start.TypeName,
                        entryProps: hasRequiredRole ? start.EntryProps : Props.Empty,
                        role: settings.Role,
                        coordinatorPath: coordinatorPath,
                        retryInterval: settings.RetryInterval,
                        snapshotInterval: settings.SnapshotInterval,
                        shardFailureBackoff: settings.ShardFailureBackoff,
                        entryRestartBackoff: settings.EntryRestartBackoff,
                        bufferSize: settings.BufferSize,
                        rememberEntries: start.RememberEntries,
                        idExtractor: start.IdExtractor,
                        shardResolver: start.ShardResolver),
                        name: encName);
                }

                Sender.Tell(new Started(shardRegion));
            });
        }
    }

    /**
    * @see [[ClusterSharding$ ClusterSharding extension]]
    */

    public class ShardRegion : ReceiveActor
    {
        private string typeName;
        private Props entryProps;
        private string role;
        private string coordinatorPath;
        private TimeSpan retryInterval;
        private TimeSpan shardFailureBackoff;
        private TimeSpan entryRestartBackoff;
        private TimeSpan snapshotInterval;
        private int bufferSize;
        private bool rememberEntries;
        private IdExtractor idExtractor;
        private ShardResolver shardResolver;

        public ShardRegion(string typeName, Props entryProps, string role, string coordinatorPath,
            TimeSpan retryInterval, TimeSpan shardFailureBackoff, TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval, int bufferSize, bool rememberEntries, IdExtractor idExtractor,
            ShardResolver shardResolver)
        {
            this.typeName = typeName;
            this.entryProps = entryProps;
            this.role = role;
            this.coordinatorPath = coordinatorPath;
            this.retryInterval = retryInterval;
            this.shardFailureBackoff = shardFailureBackoff;
            this.entryRestartBackoff = entryRestartBackoff;
            this.snapshotInterval = snapshotInterval;
            this.bufferSize = bufferSize;
            this.rememberEntries = rememberEntries;
            this.idExtractor = idExtractor;
            this.shardResolver = shardResolver;
        }

        /**
        * Scala API: Factory method for the [[akka.actor.Props]] of the [[ShardRegion]] actor.
        */

        public static Props Props(
            string typeName,
            Props entryProps,
            string role,
            string coordinatorPath,
            TimeSpan retryInterval,
            TimeSpan shardFailureBackoff,
            TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval,
            int bufferSize,
            bool rememberEntries,
            IdExtractor idExtractor,
            ShardResolver shardResolver)
        {
            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, entryProps, role, coordinatorPath, retryInterval, shardFailureBackoff,
                            entryRestartBackoff, snapshotInterval, bufferSize, rememberEntries, idExtractor,
                            shardResolver));
        }

        public static Props Props(
            string typeName,
            Props entryProps,
            string role,
            string coordinatorPath,
            TimeSpan retryInterval,
            TimeSpan shardFailureBackoff,
            TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval,
            int bufferSize,
            bool rememberEntries,
            IMessageExtractor messageExtractor)
        {
            IdExtractor idExtractor = messageExtractor.ToIdExtractor();
            ShardResolver shardResolver = ShardResolvers.Default;

            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, entryProps, role, coordinatorPath, retryInterval, shardFailureBackoff,
                            entryRestartBackoff, snapshotInterval, bufferSize, rememberEntries, idExtractor,
                            shardResolver));
        }


        /**
        * Scala API: Factory method for the [[akka.actor.Props]] of the [[ShardRegion]] actor
        * when using it in proxy only mode.
        */
        public static Props ProxyProps(string typeName, string role, string coordinatorPath, TimeSpan retryInterval,
            int bufferSize, IdExtractor idExtractor, ShardResolver shardResolver)
        {
            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, null, role, coordinatorPath, retryInterval, TimeSpan.Zero,
                            TimeSpan.Zero, TimeSpan.Zero, bufferSize, false, idExtractor, shardResolver));
        }

        /**
        * Java API: : Factory method for the [[akka.actor.Props]] of the [[ShardRegion]] actor
        * when using it in proxy only mode.
        */
        public static Props ProxyProps(string typeName, string role, string coordinatorPath, TimeSpan retryInterval,
            int bufferSize, IMessageExtractor messageExtractor)
        {
            IdExtractor idExtractor = messageExtractor.ToIdExtractor();
            ShardResolver shardResolver = ShardResolvers.Default;

            return
                Actor.Props.Create(
                    () =>
                        new ShardRegion(typeName, null, role, coordinatorPath, retryInterval, TimeSpan.Zero,
                            TimeSpan.Zero, TimeSpan.Zero, bufferSize, false, idExtractor, shardResolver));
        }
    }





    public class ShardAllocationStrategy
    {
    }

    public class LeastShardAllocationStrategy : ShardAllocationStrategy
    {
        public LeastShardAllocationStrategy(int leastShardAllocationRebalanceThreshold,
            int leastShardAllocationMaxSimultaneousRebalance)
        {
        }
    }

    public class ClusterShardingSettings
    {
        private Cluster.Cluster _cluster;

        public ClusterShardingSettings(ActorSystem system)
        {
            var config = system.Settings.Config.GetConfig("akka.contrib.cluster.sharding");
            Role = config.GetString("role");
            GuardianName = config.GetString("guardian-name");
            CoordinatorFailureBackoff = config.GetTimeSpan("coordinator-failure-backoff");
            RetryInterval = config.GetTimeSpan("retry-interval");
            BufferSize = config.GetInt("buffer-size");
            HandOffTimeout = config.GetTimeSpan("handoff-timeout");
            ShardStartTimeout = config.GetTimeSpan("shard-start-timeout");
            ShardFailureBackoff = config.GetTimeSpan("shard-failure-backoff");
            EntryRestartBackoff = config.GetTimeSpan("entry-restart-backoff");
            RebalanceInterval = config.GetTimeSpan("rebalance-interval");
            SnapshotInterval = config.GetTimeSpan("snapshot-interval");
            LeastShardAllocationRebalanceThreshold =
                config.GetInt("least-shard-allocation-strategy.rebalance-threshold");
            LeastShardAllocationMaxSimultaneousRebalance =
                config.GetInt("least-shard-allocation-strategy.max-simultaneous-rebalance");
        }

        public string Role { get; private set; }
        public string GuardianName { get; private set; }
        public TimeSpan CoordinatorFailureBackoff { get; private set; }
        public TimeSpan RetryInterval { get; private set; }
        public int BufferSize { get; private set; }
        public TimeSpan HandOffTimeout { get; private set; }
        public TimeSpan ShardStartTimeout { get; private set; }
        public TimeSpan ShardFailureBackoff { get; private set; }
        public TimeSpan EntryRestartBackoff { get; private set; }
        public TimeSpan RebalanceInterval { get; private set; }
        public TimeSpan SnapshotInterval { get; private set; }
        public int LeastShardAllocationRebalanceThreshold { get; private set; }
        public int LeastShardAllocationMaxSimultaneousRebalance { get; private set; }

        public bool HasNecessaryClusterRole(string role)
        {
            return _cluster.SelfRoles.Any(s => s == role);
        }

      
    }

    /**
    * Interface of the function used by the [[ShardRegion]] to
    * extract the shard id from an incoming message.
    * Only messages that passed the [[IdExtractor]] will be used
    * as input to this function.
    */
    public delegate ShardId ShardResolver(Msg message);

    public static class ShardResolvers
    {
        public static readonly ShardResolver Default = msg => (ShardId)msg;
    }

    /**
    * Interface of the partial function used by the [[ShardRegion]] to
    * extract the entry id and the message to send to the entry from an
    * incoming message. The implementation is application specific.
    * If the partial function does not match the message will be
    * `unhandled`, i.e. posted as `Unhandled` messages on the event stream.
    * Note that the extracted  message does not have to be the same as the incoming
    * message to support wrapping in message envelope that is unwrapped before
    * sending to the entry actor.
    */
    //type IdExtractor = PartialFunction[Msg, (EntryId, Msg)]
    public delegate Tuple<EntryId,Msg> IdExtractor(Msg message);



    /**
    * Java API: Interface of functions to extract entry id,
    * shard id, and the message to send to the entry from an
    * incoming message.
    */
    public interface IMessageExtractor
    {
        /**
        * Extract the entry id from an incoming `message`. If `null` is returned
        * the message will be `unhandled`, i.e. posted as `Unhandled` messages on the event stream
        */
        string EntryId(object message);

        /**
         * Extract the message to send to the entry from an incoming `message`.
         * Note that the extracted message does not have to be the same as the incoming
         * message to support wrapping in message envelope that is unwrapped before
         * sending to the entry actor.
         */
        object EntryMessage(object message);

        /**
        * Extract the entry id from an incoming `message`. Only messages that passed the [[#entryId]]
        * function will be used as input to this function.
        */
        string ShardId(object message);
    }

    public static class Extensions
    {
        public static IdExtractor ToIdExtractor(this IMessageExtractor self)
        {
            IdExtractor idExtractor = msg =>
            {
                if (self.EntryId(msg) != null)
                    return Tuple.Create(self.EntryId(msg), self.EntryMessage(msg));
                //TODO: should we really use tuples?

                return null;
            };

            return idExtractor;
        }
    }

    public class Shard : PersistentActor
    {

        public override ShardId PersistenceId
        {
            get { throw new NotImplementedException(); }
        }

        protected override bool ReceiveRecover(Msg message)
        {
//case EntryStarted(id) if rememberEntries ⇒ state = state.copy(state.entries + id)
//case EntryStopped(id) if rememberEntries ⇒ state = state.copy(state.entries - id)
//case SnapshotOffer(_, snapshot: State)   ⇒ state = snapshot
//case RecoveryCompleted                   ⇒ state.entries foreach getEntry
            throw new NotImplementedException();
        }

        protected override bool ReceiveCommand(Msg message)
        {
//case Terminated(ref)                                ⇒ receiveTerminated(ref)
//case msg: CoordinatorMessage                        ⇒ receiveCoordinatorMessage(msg)
//case msg: ShardCommand                              ⇒ receiveShardCommand(msg)
//case msg: ShardRegionCommand                        ⇒ receiveShardRegionCommand(msg)
//case PersistenceFailure(payload: StateChange, _, _) ⇒ persistenceFailure(payload)
//case msg if idExtractor.isDefinedAt(msg)            ⇒ deliverMessage(msg, sender())
            throw new NotImplementedException();
        }
    }
    /*

private[akka] class Shard(
  typeName: String,
  shardId: ShardRegion.ShardId,
  entryProps: Props,
  shardFailureBackoff: FiniteDuration,
  entryRestartBackoff: FiniteDuration,
  snapshotInterval: FiniteDuration,
  bufferSize: Int,
  rememberEntries: Boolean,
  idExtractor: ShardRegion.IdExtractor,
  shardResolver: ShardRegion.ShardResolver) extends PersistentActor with ActorLogging {

  import ShardRegion.{ handOffStopperProps, EntryId, Msg, Passivate }
  import ShardCoordinator.Internal.{ HandOff, ShardStopped }
  import Shard.{ State, RetryPersistence, RestartEntry, EntryStopped, EntryStarted, SnapshotTick }
  import akka.contrib.pattern.ShardCoordinator.Internal.CoordinatorMessage
  import akka.contrib.pattern.ShardRegion.ShardRegionCommand
  import akka.persistence.RecoveryCompleted

  import context.dispatcher
  val snapshotTask = context.system.scheduler.schedule(snapshotInterval, snapshotInterval, self, SnapshotTick)

  override def persistenceId = s"/sharding/${typeName}Shard/${shardId}"

  var state = State.Empty
  var idByRef = Map.empty[ActorRef, EntryId]
  var refById = Map.empty[EntryId, ActorRef]
  var passivating = Set.empty[ActorRef]
  var messageBuffers = Map.empty[EntryId, Vector[(Msg, ActorRef)]]

  var handOffStopper: Option[ActorRef] = None

  def totalBufferSize = messageBuffers.foldLeft(0) { (sum, entry) ⇒ sum + entry._2.size }

  def processChange[A](event: A)(handler: A ⇒ Unit): Unit =
    if (rememberEntries) persist(event)(handler)
    else handler(event)

  override def receiveRecover: Receive = {
    case EntryStarted(id) if rememberEntries ⇒ state = state.copy(state.entries + id)
    case EntryStopped(id) if rememberEntries ⇒ state = state.copy(state.entries - id)
    case SnapshotOffer(_, snapshot: State)   ⇒ state = snapshot
    case RecoveryCompleted                   ⇒ state.entries foreach getEntry
  }

  override def receiveCommand: Receive = {
    case Terminated(ref)                                ⇒ receiveTerminated(ref)
    case msg: CoordinatorMessage                        ⇒ receiveCoordinatorMessage(msg)
    case msg: ShardCommand                              ⇒ receiveShardCommand(msg)
    case msg: ShardRegionCommand                        ⇒ receiveShardRegionCommand(msg)
    case PersistenceFailure(payload: StateChange, _, _) ⇒ persistenceFailure(payload)
    case msg if idExtractor.isDefinedAt(msg)            ⇒ deliverMessage(msg, sender())
  }

  def receiveShardCommand(msg: ShardCommand): Unit = msg match {
    case SnapshotTick              ⇒ saveSnapshot(state)
    case RetryPersistence(payload) ⇒ retryPersistence(payload)
    case RestartEntry(id)          ⇒ getEntry(id)
  }

  def receiveShardRegionCommand(msg: ShardRegionCommand): Unit = msg match {
    case Passivate(stopMessage) ⇒ passivate(sender(), stopMessage)
    case _                      ⇒ unhandled(msg)
  }

  def receiveCoordinatorMessage(msg: CoordinatorMessage): Unit = msg match {
    case HandOff(`shardId`) ⇒ handOff(sender())
    case HandOff(shard)     ⇒ log.warning("Shard [{}] can not hand off for another Shard [{}]", shardId, shard)
    case _                  ⇒ unhandled(msg)
  }

  def persistenceFailure(payload: StateChange): Unit = {
    log.debug("Persistence of [{}] failed, will backoff and retry", payload)
    if (!messageBuffers.isDefinedAt(payload.entryId)) {
      messageBuffers = messageBuffers.updated(payload.entryId, Vector.empty)
    }

    context.system.scheduler.scheduleOnce(shardFailureBackoff, self, RetryPersistence(payload))
  }

  def retryPersistence(payload: StateChange): Unit = {
    log.debug("Retrying Persistence of [{}]", payload)
    persist(payload) { _ ⇒
      payload match {
        case msg: EntryStarted ⇒ sendMsgBuffer(msg)
        case msg: EntryStopped ⇒ passivateCompleted(msg)
      }
    }
  }

  def handOff(replyTo: ActorRef): Unit = handOffStopper match {
    case Some(_) ⇒ log.warning("HandOff shard [{}] received during existing handOff", shardId)
    case None ⇒
      log.debug("HandOff shard [{}]", shardId)

      if (state.entries.nonEmpty) {
        handOffStopper = Some(context.watch(context.actorOf(handOffStopperProps(shardId, replyTo, idByRef.keySet))))

        //During hand off we only care about watching for termination of the hand off stopper
        context become {
          case Terminated(ref) ⇒ receiveTerminated(ref)
        }
      } else {
        replyTo ! ShardStopped(shardId)
        context stop self
      }
  }

  def receiveTerminated(ref: ActorRef): Unit = {
    if (handOffStopper.exists(_ == ref)) {
      context stop self
    } else if (idByRef.contains(ref) && handOffStopper.isEmpty) {
      val id = idByRef(ref)
      if (messageBuffers.getOrElse(id, Vector.empty).nonEmpty) {
        //Note; because we're not persisting the EntryStopped, we don't need
        // to persist the EntryStarted either.
        log.debug("Starting entry [{}] again, there are buffered messages for it", id)
        sendMsgBuffer(EntryStarted(id))
      } else {
        if (rememberEntries && !passivating.contains(ref)) {
          log.debug("Entry [{}] stopped without passivating, will restart after backoff", id)
          context.system.scheduler.scheduleOnce(entryRestartBackoff, self, RestartEntry(id))
        } else processChange(EntryStopped(id))(passivateCompleted)
      }

      passivating = passivating - ref
    }
  }

  def passivate(entry: ActorRef, stopMessage: Any): Unit = {
    idByRef.get(entry) match {
      case Some(id) if !messageBuffers.contains(id) ⇒
        log.debug("Passivating started on entry {}", id)

        passivating = passivating + entry
        messageBuffers = messageBuffers.updated(id, Vector.empty)
        entry ! stopMessage

      case _ ⇒ //ignored
    }
  }

  // EntryStopped persistence handler
  def passivateCompleted(event: EntryStopped): Unit = {
    log.debug("Entry stopped [{}]", event.entryId)

    val ref = refById(event.entryId)
    idByRef -= ref
    refById -= event.entryId

    state = state.copy(state.entries - event.entryId)
    messageBuffers = messageBuffers - event.entryId
  }

  // EntryStarted persistence handler
  def sendMsgBuffer(event: EntryStarted): Unit = {
    //Get the buffered messages and remove the buffer
    val messages = messageBuffers.getOrElse(event.entryId, Vector.empty)
    messageBuffers = messageBuffers - event.entryId

    if (messages.nonEmpty) {
      log.debug("Sending message buffer for entry [{}] ([{}] messages)", event.entryId, messages.size)
      getEntry(event.entryId)

      //Now there is no deliveryBuffer we can try to redeliver
      // and as the child exists, the message will be directly forwarded
      messages foreach {
        case (msg, snd) ⇒ deliverMessage(msg, snd)
      }
    }
  }

  def deliverMessage(msg: Any, snd: ActorRef): Unit = {
    val (id, payload) = idExtractor(msg)
    if (id == null || id == "") {
      log.warning("Id must not be empty, dropping message [{}]", msg.getClass.getName)
      context.system.deadLetters ! msg
    } else {
      messageBuffers.get(id) match {
        case None ⇒ deliverTo(id, msg, payload, snd)

        case Some(buf) if totalBufferSize >= bufferSize ⇒
          log.debug("Buffer is full, dropping message for entry [{}]", id)
          context.system.deadLetters ! msg

        case Some(buf) ⇒
          log.debug("Message for entry [{}] buffered", id)
          messageBuffers = messageBuffers.updated(id, buf :+ ((msg, snd)))
      }
    }
  }

  def deliverTo(id: EntryId, msg: Any, payload: Msg, snd: ActorRef): Unit = {
    val name = URLEncoder.encode(id, "utf-8")
    context.child(name) match {
      case Some(actor) ⇒
        actor.tell(payload, snd)

      case None if rememberEntries ⇒
        //Note; we only do this if remembering, otherwise the buffer is an overhead
        messageBuffers = messageBuffers.updated(id, Vector((msg, snd)))
        persist(EntryStarted(id))(sendMsgBuffer)

      case None ⇒
        getEntry(id).tell(payload, snd)
    }
  }

  def getEntry(id: EntryId): ActorRef = {
    val name = URLEncoder.encode(id, "utf-8")
    context.child(name).getOrElse {
      log.debug("Starting entry [{}] in shard [{}]", id, shardId)

      val a = context.watch(context.actorOf(entryProps, name))
      idByRef = idByRef.updated(a, id)
      refById = refById.updated(id, a)
      state = state.copy(state.entries + id)
      a
    }
  }
    */

    /**
    * @see [[ClusterSharding$ ClusterSharding extension]]
    */
    public class ShardCoordinatorSupervisor : ReceiveActor
    {
        /**
        * Factory method for the [[akka.actor.Props]] of the [[ShardCoordinator]] actor.
        */
        public static Props Props(TimeSpan failureBackoff, Props coordinatorProps)
        {
            return Actor.Props.Create(() => new ShardCoordinatorSupervisor(failureBackoff, coordinatorProps));
        }

        /**
        * INTERNAL API
        */
        public static readonly object StartCoordinator = new object();
        
        public ShardCoordinatorSupervisor(TimeSpan failureBackoff, Props coordinatorProps)
        {
            Receive<Terminated>(t =>
            {
                //context.system.scheduler.scheduleOnce(failureBackoff, self, StartCoordinator)
                Context.System.Scheduler.ScheduleTellOnce(failureBackoff,Self, StartCoordinator,Self);
            });

            Receive<object>(_ => _ == StartCoordinator, _ =>
            {
                //def startCoordinator(): Unit = {
                //  // it will be stopped in case of PersistenceFailure
                //  context.watch(context.actorOf(coordinatorProps, "coordinator"))
                //}
                Context.Watch(Context.ActorOf(coordinatorProps, "coordinator"));
            });
        }
    }

    public class ShardCoordinator : PersistentActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private static readonly object RebalanceTick = new object();
        private static readonly object SnapshotTick = new object();
        private readonly ICancelable _rebalanceTask;
        private ICancelable _snapshotTask;

        public ShardCoordinator(TimeSpan handOffTimeout, TimeSpan shardStartTimeout, TimeSpan rebalanceInterval,TimeSpan snapshotInterval, ShardAllocationStrategy allocationStrategy)
        {
            var persistentState = State.Empty;                              // = State.empty
            var rebalanceInProgress = new HashSet<ShardId>();                  //  Set.empty[ShardId]
            var unAckedHostShards = new Dictionary<string, ICancelable>();     // Map.empty[ShardId, Cancellable]

            _rebalanceTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(rebalanceInterval, rebalanceInterval, Self, RebalanceTick, Self);
            _snapshotTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(snapshotInterval, snapshotInterval, Self, SnapshotTick, Self);
        }

        public override ShardId PersistenceId
        {
            get
            {
                return Self.Path.ToStringWithoutAddress();
            }
        }

        protected override void PostStop()
        {
            base.PostStop();
            _rebalanceTask.Cancel();
        }

        protected override bool ReceiveRecover(Msg message)
        {
            if (message is DomainEvent)
            {
                var evt = message as DomainEvent;
                _log.Debug("receiveRecover {0}", evt);
                //evt match {
                //case ShardRegionRegistered(region) ⇒
                //    persistentState = persistentState.updated(evt)
                //case ShardRegionProxyRegistered(proxy) ⇒
                //    persistentState = persistentState.updated(evt)
                //case ShardRegionTerminated(region) ⇒
                //    if (persistentState.regions.contains(region))
                //    persistentState = persistentState.updated(evt)
                //    else {
                //    log.debug("ShardRegionTerminated, but region {} was not registered. This inconsistency is due to that " +
                //        " some stored ActorRef in Akka v2.3.0 and v2.3.1 did not contain full address information. It will be " +
                //        "removed by later watch.", region)
                //    }
                //case ShardRegionProxyTerminated(proxy) ⇒
                //    if (persistentState.regionProxies.contains(proxy))
                //    persistentState = persistentState.updated(evt)
                //case ShardHomeAllocated(shard, region) ⇒
                //    persistentState = persistentState.updated(evt)
                //case _: ShardHomeDeallocated ⇒
                //    persistentState = persistentState.updated(evt)
                //}
            }
            else if (message is SnapshotOffer)
            {
                //_log.debug("receiveRecover SnapshotOffer {}", state)
                ////Old versions of the state object may not have unallocatedShard set,
                //// thus it will be null.
                //if (state.unallocatedShards == null)
                //    persistentState = state.copy(unallocatedShards = Set.empty)
                //else
                //    persistentState = state

            }
            else if (message is RecoveryCompleted)
            {
                //PersistentState.RegionProxies.ForEach(Context.Watch);
                //PersistentState.Regions.ForEach (region =>
                //{
                //    Context.Watch(region.???);
                //});
                //PersistentState.Shards.ForEach(shard =>
                //{
                //    ??
                //    SendHostShardMsg(a, r);
                //});
                //AllocateShardHomes();
            }
            return false; //TODO: ????
        }

        protected override bool ReceiveCommand(Msg message)
        {
            throw new NotImplementedException();
        }

        public static Props Props(TimeSpan handOffTimeout, TimeSpan shardStartTimeout, TimeSpan rebalanceInterval, TimeSpan snapshotInterval, ShardAllocationStrategy allocationStrategy)
        {
            return
                Actor.Props.Create(
                    () =>
                        new ShardCoordinator(handOffTimeout, shardStartTimeout, rebalanceInterval, snapshotInterval,
                            allocationStrategy));
        }
    }

    public static class State
    {
        public static readonly object Empty = new object();
    }


    public abstract class DomainEvent { }

    public class ShardRegionRegistered : DomainEvent
    {
        public ShardRegionRegistered(IActorRef region)
        {
            Region = region;
        }

        public IActorRef Region { get;private set; }
    }

    public class ShardRegionProxyRegistered : DomainEvent
    {
        public ShardRegionProxyRegistered(IActorRef regionProxy)
        {
            RegionProxy = regionProxy;
        }

        public IActorRef RegionProxy { get; private set; }
    }

    public class ShardRegionTerminated : DomainEvent
    {
        public ShardRegionTerminated(IActorRef region)
        {
            Region = region;
        }

        public IActorRef Region { get; private set; }
    }

    public class ShardRegionProxyTerminated : DomainEvent
    {
        public ShardRegionProxyTerminated(IActorRef regionProxy)
        {
            RegionProxy = regionProxy;
        }

        public IActorRef RegionProxy { get; private set; }
    }

    public class ShardHomeAllocated : DomainEvent
    {
        public ShardHomeAllocated(IActorRef region, string shardId)
        {
            Region = region;
            ShardId = shardId;
        }

        public ShardId ShardId { get;private set; }
        public IActorRef Region { get; private set; }
    }

    public class ShardHomeDeallocated : DomainEvent
    {
        public ShardHomeDeallocated(string shardId)
        {
            ShardId = shardId;
        }

        public ShardId ShardId { get; private set; }
    }
}