//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGuardian.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.DistributedData;
using Akka.Pattern;

namespace Akka.Cluster.Sharding
{
    /// <summary>
    /// INTERNAL API: <see cref="ShardRegion"/> and <see cref="PersistentShardCoordinator"/> actors are created as children of this actor.
    /// </summary>
    internal sealed class ClusterShardingGuardian : ReceiveActor
    {
        #region messages

        /// <summary>
        /// A response message for a <see cref="Start"/> and <see cref="StartProxy"/> requests.
        /// It returns an <see cref="IActorRef"/> of a local shard region or shard region proxy
        /// that can be used as a communication channel to a specific entities.
        /// </summary>
        [Serializable]
        public sealed class Started : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ShardRegion;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardRegion">TBD</param>
            public Started(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }
        }

        /// <summary>
        /// A message send to a <see cref="ClusterShardingGuardian"/> living on a current node in order
        /// to start shard region (identified by a given <see cref="TypeName"/>), which is able to host
        /// and manage the lifecycle of the entities initialized with <see cref="EntityProps"/> object.
        /// 
        /// This message is not serializable and should not be send to remote actor systems.
        /// </summary>
        [Serializable]
        public sealed class Start : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// A type name used to identify shard regions of the same type, but living on different nodes.
            /// </summary>
            public readonly string TypeName;

            /// <summary>
            /// A <see cref="Props"/> used to incarnate actor entities. Entities are created ad-hoc, when
            /// the messages are routed to them or when a `akka.cluster.sharding.remember-entities` option
            /// is set on and entities have been migrated using <see cref="ShardRegion.StartEntity"/> message.
            /// 
            /// All shard regions sharing the same <see cref="TypeName"/> are supposed to use the same 
            /// <see cref="EntityProps"/> in order to create entities of the same type and capabilities.
            /// </summary>
            public readonly Props EntityProps;

            /// <summary>
            /// A cluster sharding settings used to configured specific details around cluster sharding capabilities.
            /// </summary>
            public readonly ClusterShardingSettings Settings;

            /// <summary>
            /// A delegate used to extract an entity id from a routed message.
            /// </summary>
            public readonly ExtractEntityId ExtractEntityId;

            /// <summary>
            /// A delegate used to extract a shard id from a routed message.
            /// </summary>
            public readonly ExtractShardId ExtractShardId;

            /// <summary>
            /// An implementation of <see cref="IShardAllocationStrategy"/> interface, used to provide 
            /// logic necessary to determine placement of newly created shards or their new placement
            /// when a rebalance event triggers.
            /// </summary>
            public readonly IShardAllocationStrategy AllocationStrategy;

            /// <summary>
            /// A hand off message used to stop entities living on a target shard prior its migration
            /// to another node.
            /// </summary>
            public readonly object HandOffStopMessage;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="entityProps">TBD</param>
            /// <param name="settings">TBD</param>
            /// <param name="extractEntityId">TBD</param>
            /// <param name="extractShardId">TBD</param>
            /// <param name="allocationStrategy">TBD</param>
            /// <param name="handOffStopMessage">TBD</param>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="typeName"/> or <paramref name="entityProps"/> is undefined.
            /// </exception>
            public Start(string typeName, Props entityProps, ClusterShardingSettings settings,
                ExtractEntityId extractEntityId, ExtractShardId extractShardId, IShardAllocationStrategy allocationStrategy, object handOffStopMessage)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName), "ClusterSharding start requires type name to be provided");
                if (entityProps == null) throw new ArgumentNullException(nameof(entityProps), $"ClusterSharding start requires Props for [{typeName}] to be provided");

                TypeName = typeName;
                EntityProps = entityProps;
                Settings = settings;
                ExtractEntityId = extractEntityId;
                ExtractShardId = extractShardId;
                AllocationStrategy = allocationStrategy;
                HandOffStopMessage = handOffStopMessage;
            }
        }

        /// <summary>
        /// A message send to the <see cref="ClusterShardingGuardian"/> in order to initialize
        /// a shard region proxy capabilities. While shard region proxies are not able to host
        /// shards, they can participate in routing messages to given shard entities and can
        /// preserve them during shard migrations.
        /// 
        /// This message is not serializable and should not be send to remote actor systems.
        /// </summary>
        public sealed class StartProxy : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// Type name of a given shard region, current proxy is routing to.
            /// </summary>
            public readonly string TypeName;

            /// <summary>
            /// A data center, in context of which proxy is routing. If null, then proxy is
            /// expected to work for shard region living in the same data center as a current node.
            /// </summary>
            public readonly string DataCenter;

            /// <summary>
            /// Cluster sharding settings used to configure specific details of cluster sharding communication.
            /// </summary>
            public readonly ClusterShardingSettings Settings;

            /// <summary>
            /// A delegate used to extract entity id from incoming messages.
            /// </summary>
            public readonly ExtractEntityId ExtractEntityId;

            /// <summary>
            /// A delegate used to extract shard id from incoming messages.
            /// </summary>
            public readonly ExtractShardId ExtractShardId;

            /// <summary>
            /// Initializes a new instance of a <see cref="StartProxy"/> class, used to initialize
            /// cluster sharding proxying capabilities over regions identified by <paramref name="typeName"/>
            /// in context of a given <paramref name="dataCenter"/>.
            /// </summary>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="typeName"/> is undefined.
            /// </exception>
            public StartProxy(string typeName, string dataCenter, ClusterShardingSettings settings,
                ExtractEntityId extractEntityId, ExtractShardId extractShardId)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName), "ClusterSharding start proxy requires type name to be provided");

                TypeName = typeName;
                DataCenter = dataCenter;
                Settings = settings;
                ExtractEntityId = extractEntityId;
                ExtractShardId = extractShardId;
            }
        }

        #endregion

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ClusterSharding _sharding = ClusterSharding.Get(Context.System);

        private readonly int _majorityMinCap = Context.System.Settings.Config.GetInt("akka.cluster.sharding.distributed-data.majority-min-cap");
        private readonly ReplicatorSettings _replicatorSettings = ReplicatorSettings.Create(Context.System.Settings.Config.GetConfig("akka.cluster.sharding.distributed-data"));
        private ImmutableDictionary<string, IActorRef> _replicatorsByRole = ImmutableDictionary<string, IActorRef>.Empty;

        /// <summary>
        /// TBD
        /// </summary>
        public ClusterShardingGuardian()
        {
            Receive<Start>(start =>
            {
                try
                {
                    var settings = start.Settings;
                    var encName = Uri.EscapeDataString(start.TypeName);
                    var coordinatorSingletonManagerName = CoordinatorSingletonManagerName(encName);
                    var coordinatorPath = CoordinatorPath(encName);
                    var shardRegion = Context.Child(encName);
                    var replicator = Replicator(settings);

                    if (Equals(shardRegion, ActorRefs.Nobody))
                    {
                        if (Equals(Context.Child(coordinatorSingletonManagerName), ActorRefs.Nobody))
                        {
                            var minBackoff = settings.TunningParameters.CoordinatorFailureBackoff;
                            var maxBackoff = new TimeSpan(minBackoff.Ticks * 5);
                            var coordinatorProps = settings.StateStoreMode == StateStoreMode.Persistence
                                ? PersistentShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy)
                                : DDataShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy, replicator, _majorityMinCap, settings.RememberEntities);

                            var singletonProps = BackoffSupervisor.Props(coordinatorProps, "coordinator", minBackoff, maxBackoff, 0.2).WithDeploy(Deploy.Local);
                            var singletonSettings = settings.CoordinatorSingletonSettings.WithSingletonName("singleton").WithRole(settings.Role);
                            Context.ActorOf(ClusterSingletonManager.Props(singletonProps, PoisonPill.Instance, singletonSettings).WithDispatcher(Context.Props.Dispatcher), coordinatorSingletonManagerName);
                        }
                        shardRegion = Context.ActorOf(ShardRegion.Props(
                            typeName: start.TypeName,
                            entityProps: start.EntityProps,
                            settings: settings,
                            coordinatorPath: coordinatorPath,
                            extractEntityId: start.ExtractEntityId,
                            extractShardId: start.ExtractShardId,
                            handOffStopMessage: start.HandOffStopMessage,
                            replicator: replicator,
                            majorityMinCap: _majorityMinCap).WithDispatcher(Context.Props.Dispatcher), encName);
                    }

                    Sender.Tell(new Started(shardRegion));
                }
                catch (Exception ex)
                {
                    //TODO: JVM version matches NonFatal. Can / should we do something similar?
                    // don't restart
                    // could be invalid ReplicatorSettings, or InvalidActorNameException
                    // if it has already been started
                    Sender.Tell(new Status.Failure(ex));
                }
            });

            Receive<StartProxy>(startProxy =>
            {
                try
                {
                    var settings = startProxy.Settings;
                    var coordinatorPath = CoordinatorPath(startProxy.TypeName);

                    // it must be possible to start several proxies, one per data center
                    var actorName = string.IsNullOrEmpty(startProxy.DataCenter)
                        ? Uri.EscapeDataString(startProxy.TypeName + "Proxy")
                        : Uri.EscapeDataString(startProxy.TypeName + "-" + startProxy.DataCenter);

                    var shardRegion = Context.Child(actorName);
                    var replicator = Replicator(settings);

                    if (Equals(shardRegion, ActorRefs.Nobody))
                    {

                        shardRegion = Context.ActorOf(ShardRegion.ProxyProps(
                            typeName: startProxy.TypeName,
                            dataCenter: startProxy.DataCenter,
                            settings: settings,
                            coordinatorPath: coordinatorPath,
                            extractEntityId: startProxy.ExtractEntityId,
                            extractShardId: startProxy.ExtractShardId,
                            replicator: replicator,
                            majorityMinCap: _majorityMinCap).WithDispatcher(Context.Props.Dispatcher), actorName);
                    }

                    Sender.Tell(new Started(shardRegion));
                }
                catch (Exception ex)
                {
                    //TODO: JVM version matches NonFatal. Can / should we do something similar?
                    // don't restart
                    // could be InvalidActorNameException if it has already been started
                    Sender.Tell(new Status.Failure(ex));
                }
            });
        }

        private IActorRef Replicator(ClusterShardingSettings settings)
        {
            if (settings.StateStoreMode == StateStoreMode.DData)
            {
                // one replicator per role
                var role = settings.Role ?? string.Empty;
                if (_replicatorsByRole.TryGetValue(role, out var aref)) return aref;
                else
                {
                    var name = string.IsNullOrEmpty(settings.Role) ? "replicator" : Uri.EscapeDataString(settings.Role) + "Replicator";
                    var replicatorRef = Context.ActorOf(DistributedData.Replicator.Props(_replicatorSettings.WithRole(settings.Role)), name);

                    _replicatorsByRole = _replicatorsByRole.SetItem(role, replicatorRef);
                    return replicatorRef;
                }
            }
            else
                return Context.System.DeadLetters;
        }

        private string CoordinatorPath(string encName)
        {
            return (Self.Path / CoordinatorSingletonManagerName(encName) / "singleton" / "coordinator").ToStringWithoutAddress();
        }

        private static string CoordinatorSingletonManagerName(string encName)
        {
            return encName + "Coordinator";
        }
    }
}
