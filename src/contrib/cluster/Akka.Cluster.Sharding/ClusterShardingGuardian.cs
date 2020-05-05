//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGuardian.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
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
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class Start : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string TypeName;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Func<string, Props> EntityProps;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ClusterShardingSettings Settings;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ExtractEntityId ExtractEntityId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ExtractShardId ExtractShardId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IShardAllocationStrategy AllocationStrategy;
            /// <summary>
            /// TBD
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
            public Start(string typeName, Func<string, Props> entityProps, ClusterShardingSettings settings,
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
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class StartProxy : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly string TypeName;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ClusterShardingSettings Settings;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ExtractEntityId ExtractEntityId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ExtractShardId ExtractShardId;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="settings">TBD</param>
            /// <param name="extractEntityId">TBD</param>
            /// <param name="extractShardId">TBD</param>
            /// <exception cref="ArgumentException">
            /// This exception is thrown when the specified <paramref name="typeName"/> is undefined.
            /// </exception>
            public StartProxy(string typeName, ClusterShardingSettings settings, ExtractEntityId extractEntityId, ExtractShardId extractShardId)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName), "ClusterSharding start proxy requires type name to be provided");

                TypeName = typeName;
                Settings = settings;
                ExtractEntityId = extractEntityId;
                ExtractShardId = extractShardId;
            }
        }

        #endregion

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ClusterSharding _sharding = ClusterSharding.Get(Context.System);

        private readonly int _majorityMinCap = Context.System.Settings.Config.GetInt("akka.cluster.sharding.distributed-data.majority-min-cap", 0);
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
                    var replicator = Replicator(settings);

                    var shardRegion = Context.Child(encName).GetOrElse(() =>
                    {
                        if (Equals(Context.Child(coordinatorSingletonManagerName), ActorRefs.Nobody))
                        {
                            var minBackoff = settings.TunningParameters.CoordinatorFailureBackoff;
                            var maxBackoff = new TimeSpan(minBackoff.Ticks * 5);
                            var coordinatorProps = settings.StateStoreMode == StateStoreMode.Persistence
                                ? PersistentShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy)
                                : DDataShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy, replicator, _majorityMinCap, settings.RememberEntities);

                            var singletonProps = BackoffSupervisor.Props(
                                Backoff.OnStop(
                                    childProps: coordinatorProps,
                                    childName: "coordinator",
                                    minBackoff: minBackoff,
                                    maxBackoff: maxBackoff,
                                    randomFactor: 0.2,
                                    maxNrOfRetries: -1)
                                .WithFinalStopMessage(m => m is Terminate))
                                .WithDeploy(Deploy.Local);

                            var singletonSettings = settings.CoordinatorSingletonSettings.WithSingletonName("singleton").WithRole(settings.Role);
                            Context.ActorOf(ClusterSingletonManager.Props(singletonProps, Terminate.Instance, singletonSettings).WithDispatcher(Context.Props.Dispatcher), coordinatorSingletonManagerName);
                        }
                        return Context.ActorOf(ShardRegion.Props(
                            typeName: start.TypeName,
                            entityProps: start.EntityProps,
                            settings: settings,
                            coordinatorPath: coordinatorPath,
                            extractEntityId: start.ExtractEntityId,
                            extractShardId: start.ExtractShardId,
                            handOffStopMessage: start.HandOffStopMessage,
                            replicator: replicator,
                            majorityMinCap: _majorityMinCap).WithDispatcher(Context.Props.Dispatcher), encName);
                    });

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
                    var encName = Uri.EscapeDataString(startProxy.TypeName + "Proxy");
                    var coordinatorPath = CoordinatorPath(Uri.EscapeDataString(startProxy.TypeName));
                    var shardRegion = Context.Child(encName).GetOrElse(() => Context.ActorOf(ShardRegion.ProxyProps(
                        typeName: startProxy.TypeName,
                        settings: settings,
                        coordinatorPath: coordinatorPath,
                        extractEntityId: startProxy.ExtractEntityId,
                        extractShardId: startProxy.ExtractShardId,
                        replicator: Context.System.DeadLetters,
                        majorityMinCap: _majorityMinCap).WithDispatcher(Context.Props.Dispatcher), encName));

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

        private ReplicatorSettings GetReplicatorSettings(ClusterShardingSettings shardingSettings)
        {
            var configuredSettings = ReplicatorSettings.Create(Context.System.Settings.Config.GetConfig("akka.cluster.sharding.distributed-data"));
            var settingsWithRoles = configuredSettings.WithRole(shardingSettings.Role);
            if (shardingSettings.RememberEntities)
                return settingsWithRoles;
            else
                return settingsWithRoles.WithDurableKeys(ImmutableHashSet<string>.Empty);
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
                    var replicatorRef = Context.ActorOf(DistributedData.Replicator.Props(GetReplicatorSettings(settings)), name);

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
