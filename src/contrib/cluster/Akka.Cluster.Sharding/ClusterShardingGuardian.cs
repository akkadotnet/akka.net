﻿//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGuardian.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
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
            
            public readonly IMessageExtractor MessageExtractor;
            
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
            /// <param name="extractor"></param>
            /// <param name="allocationStrategy">TBD</param>
            /// <param name="handOffStopMessage">TBD</param>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="typeName"/> or <paramref name="entityProps"/> is undefined.
            /// </exception>
            public Start(
                string typeName,
                Func<string, Props> entityProps,
                ClusterShardingSettings settings,
                IMessageExtractor extractor,
                IShardAllocationStrategy allocationStrategy,
                object handOffStopMessage)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName), "ClusterSharding start requires type name to be provided");
                if (entityProps == null) throw new ArgumentNullException(nameof(entityProps), $"ClusterSharding start requires Props for [{typeName}] to be provided");

                TypeName = typeName;
                EntityProps = entityProps;
                Settings = settings;
                MessageExtractor = extractor;
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

            public IMessageExtractor MessageExtractor;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="settings">TBD</param>
            /// <param name="messageExtractor"></param>
            /// <exception cref="ArgumentException">
            /// This exception is thrown when the specified <paramref name="typeName"/> is undefined.
            /// </exception>
            public StartProxy(
                string typeName,
                ClusterShardingSettings settings,
               IMessageExtractor messageExtractor)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName), "ClusterSharding start proxy requires type name to be provided");

                TypeName = typeName;
                Settings = settings;
                MessageExtractor = messageExtractor;
            }
        }

        #endregion

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ClusterSharding _sharding = ClusterSharding.Get(Context.System);

        private readonly int _majorityMinCap = Context.System.Settings.Config.GetInt("akka.cluster.sharding.distributed-data.majority-min-cap", 0);
        private ImmutableDictionary<string, IActorRef> _replicatorsByRole = ImmutableDictionary<string, IActorRef>.Empty;

        private readonly ConcurrentDictionary<string, IActorRef> _regions;
        private readonly ConcurrentDictionary<string, IActorRef> _proxies;
        private readonly ConcurrentDictionary<IActorRef, string> _typeLookup = new();

        /// <summary>
        /// TBD
        /// </summary>
        public ClusterShardingGuardian(
            ConcurrentDictionary<string, IActorRef> regions,
            ConcurrentDictionary<string, IActorRef> proxies)
        {
            _regions = regions;
            _proxies = proxies;
            
            Receive<Start>(start =>
            {
                try
                {
                    var settings = start.Settings;
                    var replicator = Replicator(settings);

                    IRememberEntitiesProvider rememberEntitiesStoreProvider = null;
                    if (settings.RememberEntities)
                    {
                        // with the deprecated persistence state store mode we always use the event sourced provider for shard regions
                        // and no store for coordinator (the coordinator is a PersistentActor in that case)
                        var rememberEntitiesProvider =
                          (settings.StateStoreMode == StateStoreMode.Persistence) ?
                            RememberEntitiesStore.Eventsourced : settings.RememberEntitiesStore;

                        switch (rememberEntitiesProvider)
                        {
                            case RememberEntitiesStore.DData:
                                rememberEntitiesStoreProvider = new DDataRememberEntitiesProvider(start.TypeName, settings, _majorityMinCap, replicator);
                                break;
                            case RememberEntitiesStore.Eventsourced:
                                rememberEntitiesStoreProvider = new EventSourcedRememberEntitiesProvider(start.TypeName, settings);
                                break;
                            case RememberEntitiesStore.Custom:
                                rememberEntitiesStoreProvider = new CustomStateStoreModeProvider(start.TypeName, Context.System, settings);
                                break;
                        }
                    }

                    var encName = Uri.EscapeDataString(start.TypeName);
                    var coordinatorSingletonManagerName = CoordinatorSingletonManagerName(encName);
                    var coordinatorPath = CoordinatorPath(encName);

                    var shardRegion = Context.Child(encName).GetOrElse(() =>
                    {
                        if (Equals(Context.Child(coordinatorSingletonManagerName), ActorRefs.Nobody))
                        {
                            var minBackoff = settings.TuningParameters.CoordinatorFailureBackoff;
                            var maxBackoff = new TimeSpan(minBackoff.Ticks * 5);
                            var coordinatorProps = settings.StateStoreMode == StateStoreMode.Persistence
                                ? PersistentShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy)
                                : DDataShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy, replicator, _majorityMinCap, rememberEntitiesStoreProvider);

                            var singletonProps = BackoffSupervisor.Props(
                                Backoff.OnStop(
                                    childProps: coordinatorProps,
                                    childName: "coordinator",
                                    minBackoff: minBackoff,
                                    maxBackoff: maxBackoff,
                                    randomFactor: 0.2,
                                    maxNrOfRetries: -1)
                                .WithFinalStopMessage(m => m is ShardCoordinator.Terminate))
                                .WithDeploy(Deploy.Local);

                            var singletonSettings = settings.CoordinatorSingletonSettings.WithSingletonName("singleton").WithRole(settings.Role);
                            Context.ActorOf(ClusterSingletonManager.Props(singletonProps, ShardCoordinator.Terminate.Instance, singletonSettings).WithDispatcher(Context.Props.Dispatcher), coordinatorSingletonManagerName);
                        }
                        return Context.ActorOf(ShardRegion.Props(
                            typeName: start.TypeName,
                            entityProps: start.EntityProps,
                            settings: settings,
                            coordinatorPath: coordinatorPath,
                            messageExtractor: start.MessageExtractor,
                            handOffStopMessage: start.HandOffStopMessage,
                            rememberEntitiesStoreProvider)
                            .WithDispatcher(Context.Props.Dispatcher), encName);
                    });

                    _regions.TryAdd(start.TypeName, shardRegion);
                    _typeLookup.TryAdd(shardRegion, start.TypeName);
                    Context.Watch(shardRegion);
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
                        messageExtractor: startProxy.MessageExtractor)
                        .WithDispatcher(Context.Props.Dispatcher), encName));

                    _proxies.TryAdd(startProxy.TypeName, shardRegion);
                    _typeLookup.TryAdd(shardRegion, startProxy.TypeName);
                    Context.Watch(shardRegion);
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

            Receive<Terminated>(msg =>
            {
                if (!_typeLookup.TryGetValue(msg.ActorRef, out var typeName)) 
                    return;
                
                _typeLookup.TryRemove(msg.ActorRef, out _);

                if (_regions.TryGetValue(typeName, out var regionActor) && regionActor.Equals(msg.ActorRef))
                {
                    _regions.TryRemove(typeName, out _);
                    return;
                }
                
                if(_proxies.TryGetValue(typeName, out var proxyActor) && proxyActor.Equals(msg.ActorRef))
                    _proxies.TryRemove(typeName, out _);
            });
        }

        internal static ReplicatorSettings GetReplicatorSettings(ClusterShardingSettings shardingSettings)
        {
            var config = Context.System.Settings.Config.GetConfig("akka.cluster.sharding.distributed-data")
                .WithFallback(Context.System.Settings.Config.GetConfig("akka.cluster.distributed-data"));
            var configuredSettings = ReplicatorSettings.Create(config);
            var settingsWithRoles = configuredSettings.WithRole(shardingSettings.Role);
            if (shardingSettings.RememberEntities)
                return settingsWithRoles;
            else
                return settingsWithRoles.WithDurableKeys(ImmutableHashSet<string>.Empty);
        }

        private IActorRef Replicator(ClusterShardingSettings settings)
        {
            if (settings.StateStoreMode is StateStoreMode.DData or StateStoreMode.Custom)
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
