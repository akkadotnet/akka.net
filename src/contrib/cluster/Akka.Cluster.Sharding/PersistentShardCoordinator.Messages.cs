//-----------------------------------------------------------------------
// <copyright file="PersistentShardCoordinator.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    partial class PersistentShardCoordinator
    {
        #region Message types

        /// <summary>
        /// Messages sent to the coordinator.
        /// </summary>
        public interface ICoordinatorCommand : IClusterShardingSerializable { }

        /// <summary>
        /// Messages sent from the coordinator.
        /// </summary>
        public interface ICoordinatorMessage : IClusterShardingSerializable { }

        /// <summary>
        /// <see cref="Sharding.ShardRegion"/> registers to <see cref="PersistentShardCoordinator"/>, until it receives <see cref="RegisterAck"/>.
        /// </summary>
        [Serializable]
        public sealed class Register : ICoordinatorCommand
        {
            public readonly IActorRef ShardRegion;

            public Register(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }
        }

        /// <summary>
        /// <see cref="ShardRegion"/> in proxy only mode registers to <see cref="PersistentShardCoordinator"/>, until it receives <see cref="RegisterAck"/>.
        /// </summary>
        [Serializable]
        public sealed class RegisterProxy : ICoordinatorCommand
        {
            public readonly IActorRef ShardRegionProxy;

            public RegisterProxy(IActorRef shardRegionProxy)
            {
                ShardRegionProxy = shardRegionProxy;
            }
        }

        /// <summary>
        /// Acknowledgement from <see cref="PersistentShardCoordinator"/> that <see cref="Register"/> or <see cref="RegisterProxy"/> was sucessful.
        /// </summary>
        public sealed class RegisterAck : ICoordinatorMessage
        {
            public readonly IActorRef Coordinator;

            public RegisterAck(IActorRef coordinator)
            {
                Coordinator = coordinator;
            }
        }

        /// <summary>
        /// <see cref="ShardRegion"/> requests the location of a shard by sending this message
        /// to the <see cref="PersistentShardCoordinator"/>.
        /// </summary>
        [Serializable]
        public sealed class GetShardHome : ICoordinatorCommand
        {
            public readonly ShardId Shard;

            public GetShardHome(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// <see cref="PersistentShardCoordinator"/> replies with this message for <see cref="GetShardHome"/> requests.
        /// </summary>
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

        /// <summary>
        /// <see cref="PersistentShardCoordinator"/> informs a <see cref="ShardRegion"/> that it is hosting this shard
        /// </summary>
        [Serializable]
        public sealed class HostShard : ICoordinatorMessage
        {
            public readonly ShardId Shard;

            public HostShard(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// <see cref="ShardRegion"/> replies with this message for <see cref="HostShard"/> requests which lead to it hosting the shard
        /// </summary>
        [Serializable]
        public sealed class ShardStarted : ICoordinatorMessage
        {
            public readonly ShardId Shard;

            public ShardStarted(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// <see cref="PersistentShardCoordinator"/> initiates rebalancing process by sending this message
        /// to all registered <see cref="ShardRegion"/> actors (including proxy only). They are
        /// supposed to discard their known location of the shard, i.e. start buffering
        /// incoming messages for the shard. They reply with <see cref="BeginHandOffAck"/>.
        /// When all have replied the <see cref="PersistentShardCoordinator"/> continues by sending
        /// <see cref="HandOff"/> to the <see cref="ShardRegion"/> responsible for the shard.
        /// </summary>
        [Serializable]
        public sealed class BeginHandOff : ICoordinatorMessage
        {
            public readonly ShardId Shard;

            public BeginHandOff(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// Acknowledgement of <see cref="BeginHandOff"/>
        /// </summary>
        [Serializable]
        public sealed class BeginHandOffAck : ICoordinatorCommand
        {
            public readonly ShardId Shard;

            public BeginHandOffAck(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// When all <see cref="ShardRegion"/> actors have acknoledged the <see cref="BeginHandOff"/> the
        /// <see cref="PersistentShardCoordinator"/> sends this message to the <see cref="ShardRegion"/> responsible for the
        /// shard. The <see cref="ShardRegion"/> is supposed to stop all entries in that shard and when
        /// all entries have terminated reply with <see cref="ShardStopped"/> to the <see cref="PersistentShardCoordinator"/>.
        /// </summary>
        [Serializable]
        public sealed class HandOff : ICoordinatorMessage
        {
            public readonly ShardId Shard;

            public HandOff(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// Reply to <see cref="HandOff"/> when all entries in the shard have been terminated.
        /// </summary>
        [Serializable]
        public sealed class ShardStopped : ICoordinatorCommand
        {
            public readonly ShardId Shard;

            public ShardStopped(string shard)
            {
                Shard = shard;
            }
        }

        /// <summary>
        /// Result of <see cref="PersistentShardCoordinator.AllocateShard"/> is piped to self with this message.
        /// </summary>
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

        /// <summary>
        /// Result of `rebalance` is piped to self with this message.
        /// </summary>
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
        /// <see cref="Sharding.ShardRegion"/> requests full handoff to be able to shutdown gracefully.
        /// </summary>
        [Serializable]
        public sealed class GracefulShutdownRequest : ICoordinatorCommand
        {
            public readonly IActorRef ShardRegion;
            public GracefulShutdownRequest(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }
        }

        /// <summary>
        /// DomainEvents for the persistent state of the event sourced PersistentShardCoordinator
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

        [Serializable]
        public sealed class StateInitialized
        {
            public static readonly StateInitialized Instance = new StateInitialized();
            private StateInitialized() { }
        }

        #endregion

    }
}
