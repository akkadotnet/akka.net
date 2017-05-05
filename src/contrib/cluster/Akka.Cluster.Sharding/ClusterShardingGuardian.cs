//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGuardian.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
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
            public readonly Props EntityProps;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ClusterShardingSettings Settings;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IdExtractor IdExtractor;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardResolver ShardResolver;
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
            /// <param name="idIdExtractor">TBD</param>
            /// <param name="shardResolver">TBD</param>
            /// <param name="allocationStrategy">TBD</param>
            /// <param name="handOffStopMessage">TBD</param>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="typeName"/> or <paramref name="entityProps"/> is undefined.
            /// </exception>
            public Start(string typeName, Props entityProps, ClusterShardingSettings settings,
                IdExtractor idIdExtractor, ShardResolver shardResolver, IShardAllocationStrategy allocationStrategy, object handOffStopMessage)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException(nameof(typeName), "ClusterSharding start requires type name to be provided");
                if (entityProps == null) throw new ArgumentNullException(nameof(entityProps), $"ClusterSharding start requires Props for [{typeName}] to be provided");

                TypeName = typeName;
                EntityProps = entityProps;
                Settings = settings;
                IdExtractor = idIdExtractor;
                ShardResolver = shardResolver;
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
            public readonly IdExtractor ExtractEntityId;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardResolver ExtractShardId;

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
            public StartProxy(string typeName, ClusterShardingSettings settings, IdExtractor extractEntityId, ShardResolver extractShardId)
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

        /// <summary>
        /// TBD
        /// </summary>
        public ClusterShardingGuardian()
        {
            Receive<Start>(start =>
            {
                var settings = start.Settings;
                var encName = Uri.EscapeDataString(start.TypeName);
                var coordinatorSingletonManagerName = CoordinatorSingletonManagerName(encName);
                var coordinatorPath = CoordinatorPath(encName);
                var shardRegion = Context.Child(encName);

                if (Equals(shardRegion, ActorRefs.Nobody))
                {
                    var minBackoff = settings.TunningParameters.CoordinatorFailureBackoff;
                    var maxBackoff = new TimeSpan(minBackoff.Ticks * 5);
                    var coordinatorProps = PersistentShardCoordinator.Props(start.TypeName, settings, start.AllocationStrategy);
                    var singletonProps = BackoffSupervisor.Props(coordinatorProps, "coordinator", minBackoff, maxBackoff, 0.2).WithDeploy(Deploy.Local);
                    var singletonSettings = settings.CoordinatorSingletonSettings.WithSingletonName("singleton").WithRole(settings.Role);
                    var shardCoordinatorSingleton = Context.ActorOf(ClusterSingletonManager.Props(singletonProps, PoisonPill.Instance, singletonSettings), coordinatorSingletonManagerName);
                }

                shardRegion = Context.ActorOf(ShardRegion.Props(
                    typeName: start.TypeName,
                    entityProps: start.EntityProps,
                    settings: settings,
                    coordinatorPath: coordinatorPath,
                    extractEntityId: start.IdExtractor,
                    extractShardId: start.ShardResolver,
                    handOffStopMessage: start.HandOffStopMessage), encName);

                Sender.Tell(new Started(shardRegion));
            });

            Receive<StartProxy>(startProxy =>
            {
                var settings = startProxy.Settings;
                var encName = Uri.EscapeDataString(startProxy.TypeName);
                var coordinatorSingletonManagerName = CoordinatorSingletonManagerName(encName);
                var coordinatorPath = CoordinatorPath(encName);

                var shardRegion = Context.ActorOf(ShardRegion.ProxyProps(
                    typeName: startProxy.TypeName,
                    settings: settings,
                    coordinatorPath: coordinatorPath,
                    extractEntityId: startProxy.ExtractEntityId,
                    extractShardId: startProxy.ExtractShardId), encName);

                Sender.Tell(new Started(shardRegion));
            });
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