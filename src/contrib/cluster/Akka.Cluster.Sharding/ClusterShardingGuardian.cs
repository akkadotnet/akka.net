//-----------------------------------------------------------------------
// <copyright file="ClusterShardingGuardian.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Pattern;

namespace Akka.Cluster.Sharding
{
    /// <summary>
    /// INTERNAL API: <see cref="ShardRegion"/> and <see cref="PersistentShardCoordinator"/> actors are createad as children of this actor.
    /// </summary>
    internal sealed class ClusterShardingGuardian : ReceiveActor
    {
        #region messages

        [Serializable]
        public sealed class Started : INoSerializationVerificationNeeded
        {
            public readonly IActorRef ShardRegion;

            public Started(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }
        }

        [Serializable]
        public sealed class Start : INoSerializationVerificationNeeded
        {
            public readonly string TypeName;
            public readonly Props EntityProps;
            public readonly ClusterShardingSettings Settings;
            public readonly IdExtractor IdExtractor;
            public readonly ShardResolver ShardResolver;
            public readonly IShardAllocationStrategy AllocationStrategy;
            public readonly object HandOffStopMessage;

            public Start(string typeName, Props entityProps, ClusterShardingSettings settings,
                IdExtractor idIdExtractor, ShardResolver shardResolver, IShardAllocationStrategy allocationStrategy, object handOffStopMessage)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException("typeName", "ClusterSharding start requires type name to be provided");
                if (entityProps == null) throw new ArgumentNullException("entityProps", string.Format("ClusterSharding start requires Props for [{0}] to be provided", typeName));

                TypeName = typeName;
                EntityProps = entityProps;
                Settings = settings;
                IdExtractor = idIdExtractor;
                ShardResolver = shardResolver;
                AllocationStrategy = allocationStrategy;
                HandOffStopMessage = handOffStopMessage;
            }
        }

        [Serializable]
        public sealed class StartProxy : INoSerializationVerificationNeeded
        {
            public readonly string TypeName;
            public readonly ClusterShardingSettings Settings;
            public readonly IdExtractor ExtractEntityId;
            public readonly ShardResolver ExtractShardId;

            public StartProxy(string typeName, ClusterShardingSettings settings, IdExtractor extractEntityId, ShardResolver extractShardId)
            {
                if (string.IsNullOrEmpty(typeName)) throw new ArgumentNullException("typeName", "ClusterSharding start proxy requires type name to be provided");

                TypeName = typeName;
                Settings = settings;
                ExtractEntityId = extractEntityId;
                ExtractShardId = extractShardId;
            }
        }

        #endregion

        private readonly Cluster _cluster = Cluster.Get(Context.System);
        private readonly ClusterSharding _sharding = ClusterSharding.Get(Context.System);

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
                    var singletonProps = Props.Create(() => new BackoffSupervisor(coordinatorProps, "coordinator", minBackoff, maxBackoff, 0.2)).WithDeploy(Deploy.Local);
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