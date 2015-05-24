using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Event;
using Akka.Persistence;

namespace Akka.Cluster.Sharding
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
        private Akka.Cluster.Cluster _cluster;

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
            IShardAllocationStrategy allocationStrategy)
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
            IMessageExtractor messageExtractor, IShardAllocationStrategy allocationStrategy)
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
        public IShardAllocationStrategy AllocationStrategy { get; private set; }

        public Start(string typeName, Props entryProps, string roleOverride, bool rememberEntries,
            IdExtractor idIdExtractor, ShardResolver shardResolver, IShardAllocationStrategy allocationStrategy)
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
            var cluster = Context.System.GetExtension<Akka.Cluster.Cluster>();
            var clusterSharding = Context.System.GetExtension<ClusterSharding>();
            var settings = clusterSharding.Settings;

            Receive<Start>(start =>
            {
                //TODO: escape data string or escape uri string?
                var encName = Uri.EscapeDataString(start.TypeName);
                var coordinatorSingletonManagerName = encName + "Coordinator";
                var coordinatorPath =
                    (Self.Path / coordinatorSingletonManagerName / "singleton" / "coordinator").ToStringWithoutAddress();

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
        public abstract class ShardRegionCommand { }

        /**
        * If the state of the entries are persistent you may stop entries that are not used to
        * reduce memory consumption. This is done by the application specific implementation of
        * the entry actors for example by defining receive timeout (`context.setReceiveTimeout`).
        * If a message is already enqueued to the entry when it stops itself the enqueued message
        * in the mailbox will be dropped. To support graceful passivation without loosing such
        * messages the entry actor can send this `Passivate` message to its parent `ShardRegion`.
        * The specified wrapped `stopMessage` will be sent back to the entry, which is
        * then supposed to stop itself. Incoming messages will be buffered by the `ShardRegion`
        * between reception of `Passivate` and termination of the entry. Such buffered messages
        * are thereafter delivered to a new incarnation of the entry.
        *
        * [[akka.actor.PoisonPill]] is a perfectly fine `stopMessage`.
        */
        public class Passivate : ShardRegionCommand
        {
            public Passivate(object stopMessage)
            {
                StopMessage = stopMessage;
            }

            public object StopMessage { get; private set; }
        }

        public class Retry : ShardRegionCommand
        {
            private static readonly Retry _instance = new Retry();

            public static Retry Instance
            {
                get { return _instance; }
            }
        }

        private string _typeName;
        private Props _entryProps;
        private string _role;
        private string _coordinatorPath;
        private TimeSpan _retryInterval;
        private TimeSpan _shardFailureBackoff;
        private TimeSpan _entryRestartBackoff;
        private TimeSpan _snapshotInterval;
        private int _bufferSize;
        private bool _rememberEntries;
        private IdExtractor _idExtractor;
        private ShardResolver _shardResolver;

        public ShardRegion(string typeName, Props entryProps, string role, string coordinatorPath,
            TimeSpan retryInterval, TimeSpan shardFailureBackoff, TimeSpan entryRestartBackoff,
            TimeSpan snapshotInterval, int bufferSize, bool rememberEntries, IdExtractor idExtractor,
            ShardResolver shardResolver)
        {
            _typeName = typeName;
            _entryProps = entryProps;
            _role = role;
            _coordinatorPath = coordinatorPath;
            _retryInterval = retryInterval;
            _shardFailureBackoff = shardFailureBackoff;
            _entryRestartBackoff = entryRestartBackoff;
            _snapshotInterval = snapshotInterval;
            _bufferSize = bufferSize;
            _rememberEntries = rememberEntries;
            _idExtractor = idExtractor;
            _shardResolver = shardResolver;
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

    
    /**
     * Interface of the pluggable shard allocation and rebalancing logic used by the [[ShardCoordinator]].
     *
     * Java implementations should extend [[AbstractShardAllocationStrategy]].
     */
    public interface IShardAllocationStrategy
    {
        /**
         * Invoked when the location of a new shard is to be decided.
         * @param requester actor reference to the [[ShardRegion]] that requested the location of the
         *   shard, can be returned if preference should be given to the node where the shard was first accessed
         * @param shardId the id of the shard to allocate
         * @param currentShardAllocations all actor refs to `ShardRegion` and their current allocated shards,
         *   in the order they were allocated
         * @return a `Future` of the actor ref of the [[ShardRegion]] that is to be responsible for the shard, must be one of
         *   the references included in the `currentShardAllocations` parameter
         */
        Task<IActorRef> AllocateShard(IActorRef requester, ShardId shardId, IDictionary<IActorRef, ShardId[]> currentShardAllocations);

        /**
         * Invoked periodically to decide which shards to rebalance to another location.
         * @param currentShardAllocations all actor refs to `ShardRegion` and their current allocated shards,
         *   in the order they were allocated
         * @param rebalanceInProgress set of shards that are currently being rebalanced, i.e.
         *   you should not include these in the returned set
         * @return a `Future` of the shards to be migrated, may be empty to skip rebalance in this round
         */
        Task<ISet<ShardId>> Rebalance(IDictionary<IActorRef, ShardId[]> currentShardAllocations, ISet<ShardId> rebalanceInProgress);
    }

    /**
     * Java API: Java implementations of custom shard allocation and rebalancing logic used by the [[ShardCoordinator]]
     * should extend this abstract class and implement the two methods.
     */
    public abstract class BaseShardAllocationStrategy : IShardAllocationStrategy
    {
        public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IDictionary<IActorRef, ShardId[]> currentShardAllocations)
        {
            throw new NotImplementedException();
        }

        public Task<ISet<string>> Rebalance(IDictionary<IActorRef, ShardId[]> currentShardAllocations, ISet<string> rebalanceInProgress)
        {
            throw new NotImplementedException();
        }
    }

    public class LeastShardAllocationStrategy : IShardAllocationStrategy
    {
        public LeastShardAllocationStrategy(int leastShardAllocationRebalanceThreshold,
            int leastShardAllocationMaxSimultaneousRebalance)
        {
        }

        public Task<IActorRef> AllocateShard(IActorRef requester, string shardId, IDictionary<IActorRef, ShardId[]> currentShardAllocations)
        {
            throw new NotImplementedException();
        }

        public Task<ISet<string>> Rebalance(IDictionary<IActorRef, ShardId[]> currentShardAllocations, ISet<string> rebalanceInProgress)
        {
            throw new NotImplementedException();
        }
    }

    public class ClusterShardingSettings
    {
        private Akka.Cluster.Cluster _cluster;

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
    public delegate Tuple<EntryId, Msg> IdExtractor(Msg message);



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
        EntryId EntryId(object message);

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
                Context.System.Scheduler.ScheduleTellOnce(failureBackoff, Self, StartCoordinator, Self);
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

    /**
     * Periodic message to trigger rebalance
     */
    internal sealed class RebalanceTick
    {
        public static readonly RebalanceTick Instance = new RebalanceTick();
        private RebalanceTick() { }
    }

    /**
     * End of rebalance process performed by [[RebalanceWorker]]
     */
    internal sealed class RebalanceDone
    {
        public readonly ShardId Shard;
        public readonly bool Ok;

        public RebalanceDone(string shard, bool ok)
        {
            Shard = shard;
            Ok = ok;
        }
    }

    /**
     * INTERNAL API. Rebalancing process is performed by this actor.
     * It sends `BeginHandOff` to all `ShardRegion` actors followed by
     * `HandOff` to the `ShardRegion` responsible for the shard.
     * When the handoff is completed it sends [[RebalanceDone]] to its
     * parent `ShardCoordinator`. If the process takes longer than the
     * `handOffTimeout` it also sends [[RebalanceDone]].
     */
    public class RebalanceWorker : ActorBase
    {
        public static Props Props(string shard, IActorRef @from, TimeSpan handOffTimeout, ISet<IActorRef> regions)
        {
            return Actor.Props.Create(() => new RebalanceWorker(shard, @from, handOffTimeout, regions));
        }

        private readonly ShardId _shard;
        private readonly IActorRef _from;
        private readonly ISet<IActorRef> _remaining;

        public RebalanceWorker(string shard, IActorRef @from, TimeSpan handOffTimeout, ISet<IActorRef> regions)
        {
            _shard = shard;
            _from = @from;

            foreach (var region in regions)
            {
                region.Tell(new BeginHandOff(shard));
            }

            _remaining = new HashSet<IActorRef>(regions);
            Context.System.Scheduler.ScheduleTellOnce(handOffTimeout, Self, ReceiveTimeout.Instance, Self);
        }

        protected override bool Receive(object message)
        {
            if (message is BeginHandOffAck)
            {
                var shard = ((BeginHandOffAck)message).Shard;
                _remaining.Remove(Sender);
                if (_remaining.Count == 0)
                {
                    _from.Tell(new HandOff(shard));
                    Context.Become(StoppingShard);
                }
            }
            else if (message is ReceiveTimeout)
            {
                Done(false);
            }
            else return false;
            return true;
        }

        private bool StoppingShard(object message)
        {
            if (message is ShardStopped) Done(true);
            else if (message is ReceiveTimeout) Done(false);
            else return false;
            return true;
        }

        private void Done(bool ok)
        {
            Context.Parent.Tell(new RebalanceDone(_shard, ok));
            Context.Stop(Self);
        }
    }

    /**
     * Check if we've received a shard start request
     */
    internal sealed class ResendShardHost
    {
        public readonly ShardId Shard;
        public readonly IActorRef Region;

        public ResendShardHost(string shard, IActorRef region)
        {
            Shard = shard;
            Region = region;
        }
    }

    public interface ICoordinatorCommand { }

    public interface ICoordinatorMessage { }

    ///**
    // * `ShardRegion` registers to `ShardCoordinator`, until it receives [[RegisterAck]].
    // */
    [Serializable]
    public sealed class Register : ICoordinatorCommand
    {
        public readonly IActorRef ShardRegion;

        public Register(IActorRef shardRegion)
        {
            ShardRegion = shardRegion;
        }
    }

    /**
     * `ShardRegion` in proxy only mode registers to `ShardCoordinator`, until it receives [[RegisterAck]].
     */
    [Serializable]
    public sealed class RegisterProxy : ICoordinatorCommand
    {
        public readonly IActorRef ShardRegionProxy;

        public RegisterProxy(IActorRef shardRegionProxy)
        {
            ShardRegionProxy = shardRegionProxy;
        }
    }

    /**
     * Acknowledgement from `ShardCoordinator` that [[Register]] or [[RegisterProxy]] was sucessful.
     */
    public sealed class RegisterAck : ICoordinatorMessage
    {
        public readonly IActorRef Coordinator;

        public RegisterAck(IActorRef coordinator)
        {
            Coordinator = coordinator;
        }
    }

    /**
     * Periodic message to trigger persistent snapshot
     */
    public sealed class SnapshotTick : ICoordinatorMessage
    {
        public static readonly SnapshotTick Instance = new SnapshotTick();

        private SnapshotTick() { }
    }

    /**
     * `ShardRegion` requests the location of a shard by sending this message
     * to the `ShardCoordinator`.
     */
    [Serializable]
    public sealed class GetShardHome : ICoordinatorCommand
    {
        public readonly ShardId Shard;

        public GetShardHome(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * `ShardCoordinator` replies with this message for [[GetShardHome]] requests.
     */
    [Serializable]
    public sealed class ShardHome : ICoordinatorMessage
    {
        public readonly ShardId Shard;
        public readonly IActorRef Ref;

        public ShardHome(string shard, IActorRef @ref)
        {
            Shard = shard;
            Ref = @ref;
        }
    }

    /**
     * `ShardCoodinator` informs a `ShardRegion` that it is hosting this shard
     */
    [Serializable]
    public sealed class HostShard : ICoordinatorMessage
    {
        public readonly ShardId Shard;

        public HostShard(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * `ShardRegion` replies with this message for [[HostShard]] requests which lead to it hosting the shard
     */
    [Serializable]
    public sealed class ShardStarted : ICoordinatorMessage
    {
        public readonly ShardId Shard;

        public ShardStarted(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * `ShardCoordinator` initiates rebalancing process by sending this message
     * to all registered `ShardRegion` actors (including proxy only). They are
     * supposed to discard their known location of the shard, i.e. start buffering
     * incoming messages for the shard. They reply with [[BeginHandOffAck]].
     * When all have replied the `ShardCoordinator` continues by sending
     * `HandOff` to the `ShardRegion` responsible for the shard.
     */
    [Serializable]
    public sealed class BeginHandOff : ICoordinatorMessage
    {
        public readonly ShardId Shard;

        public BeginHandOff(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * Acknowledgement of [[BeginHandOff]]
     */
    [Serializable]
    public sealed class BeginHandOffAck : ICoordinatorCommand
    {
        public readonly ShardId Shard;

        public BeginHandOffAck(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * When all `ShardRegion` actors have acknoledged the `BeginHandOff` the
     * `ShardCoordinator` sends this message to the `ShardRegion` responsible for the
     * shard. The `ShardRegion` is supposed to stop all entries in that shard and when
     * all entries have terminated reply with `ShardStopped` to the `ShardCoordinator`.
     */
    [Serializable]
    public sealed class HandOff : ICoordinatorMessage
    {
        public readonly ShardId Shard;

        public HandOff(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * Reply to `HandOff` when all entries in the shard have been terminated.
     */
    [Serializable]
    public sealed class ShardStopped : ICoordinatorCommand
    {
        public readonly ShardId Shard;

        public ShardStopped(string shard)
        {
            Shard = shard;
        }
    }

    /**
     * Result of `allocateShard` is piped to self with this message.
     */
    [Serializable]
    public sealed class AllocateShardResult : ICoordinatorCommand
    {
        public readonly ShardId Shard;
        public readonly IActorRef ShardRegion; // option
        public readonly IActorRef GetShardHomeSender;

        public AllocateShardResult(string shard, IActorRef shardRegion, IActorRef getShardHomeSender)
        {
            Shard = shard;
            ShardRegion = shardRegion;
            GetShardHomeSender = getShardHomeSender;
        }
    }

    /**
     * Result of `rebalance` is piped to self with this message.
     */
    [Serializable]
    public sealed class RebalanceResult : ICoordinatorCommand
    {
        public readonly IEnumerable<ShardId> Shards;

        public RebalanceResult(IEnumerable<string> shards)
        {
            Shards = shards;
        }
    }

    /// <summary>
    /// DomainEvents for the persistent state of the event sourced ShardCoordinator
    /// </summary>
    public interface IDomainEvent { }

    [Serializable]
    public class ShardRegionRegistered : IDomainEvent
    {
        public readonly IActorRef Region;

        public ShardRegionRegistered(IActorRef region)
        {
            Region = region;
        }
    }

    [Serializable]
    public class ShardRegionProxyRegistered : IDomainEvent
    {
        public readonly IActorRef RegionProxy;
        public ShardRegionProxyRegistered(IActorRef regionProxy)
        {
            RegionProxy = regionProxy;
        }
    }

    [Serializable]
    public class ShardRegionTerminated : IDomainEvent
    {
        public readonly IActorRef Region;
        public ShardRegionTerminated(IActorRef region)
        {
            Region = region;
        }
    }

    [Serializable]
    public class ShardRegionProxyTerminated : IDomainEvent
    {
        public readonly IActorRef RegionProxy;
        public ShardRegionProxyTerminated(IActorRef regionProxy)
        {
            RegionProxy = regionProxy;
        }
    }

    [Serializable]
    public class ShardHomeAllocated : IDomainEvent
    {
        public readonly ShardId Shard;
        public readonly IActorRef Region;

        public ShardHomeAllocated(string shard, IActorRef region)
        {
            Shard = shard;
            Region = region;
        }
    }

    [Serializable]
    public class ShardHomeDeallocated : IDomainEvent
    {
        public readonly ShardId Shard;

        public ShardHomeDeallocated(string shard)
        {
            Shard = shard;
        }
    }
}