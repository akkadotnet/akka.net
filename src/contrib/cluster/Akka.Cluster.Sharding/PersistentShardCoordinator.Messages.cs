//-----------------------------------------------------------------------
// <copyright file="PersistentShardCoordinator.Messages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /// <summary>
    /// TBD
    /// </summary>
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
        public sealed class Register : ICoordinatorCommand, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ShardRegion;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardRegion">TBD</param>
            public Register(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as Register;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardRegion.Equals(other.ShardRegion);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ShardRegion?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="ShardRegion"/> in proxy only mode registers to <see cref="PersistentShardCoordinator"/>, until it receives <see cref="RegisterAck"/>.
        /// </summary>
        [Serializable]
        public sealed class RegisterProxy : ICoordinatorCommand, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ShardRegionProxy;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardRegionProxy">TBD</param>
            public RegisterProxy(IActorRef shardRegionProxy)
            {
                ShardRegionProxy = shardRegionProxy;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as RegisterProxy;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardRegionProxy.Equals(other.ShardRegionProxy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ShardRegionProxy?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// Acknowledgement from <see cref="PersistentShardCoordinator"/> that <see cref="Register"/> or <see cref="RegisterProxy"/> was successful.
        /// </summary>
        public sealed class RegisterAck : ICoordinatorMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Coordinator;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="coordinator">TBD</param>
            public RegisterAck(IActorRef coordinator)
            {
                Coordinator = coordinator;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as RegisterAck;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Coordinator.Equals(other.Coordinator);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Coordinator?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="ShardRegion"/> requests the location of a shard by sending this message
        /// to the <see cref="PersistentShardCoordinator"/>.
        /// </summary>
        [Serializable]
        public sealed class GetShardHome : ICoordinatorCommand, IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public GetShardHome(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as GetShardHome;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="PersistentShardCoordinator"/> replies with this message for <see cref="GetShardHome"/> requests.
        /// </summary>
        [Serializable]
        public sealed class ShardHome : ICoordinatorMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Ref;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            /// <param name="ref">TBD</param>
            public ShardHome(string shard, IActorRef @ref)
            {
                Shard = shard;
                Ref = @ref;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardHome;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard)
                    && Ref.Equals(other.Ref);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Shard?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (Ref?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="PersistentShardCoordinator"/> informs a <see cref="ShardRegion"/> that it is hosting this shard
        /// </summary>
        [Serializable]
        public sealed class HostShard : ICoordinatorMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public HostShard(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as HostShard;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="ShardRegion"/> replies with this message for <see cref="HostShard"/> requests which lead to it hosting the shard
        /// </summary>
        [Serializable]
        public sealed class ShardStarted : ICoordinatorMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public ShardStarted(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardStarted;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// <see cref="PersistentShardCoordinator" /> initiates rebalancing process by sending this message
        /// to all registered <see cref="ShardRegion" /> actors (including proxy only). They are
        /// supposed to discard their known location of the shard, i.e. start buffering
        /// incoming messages for the shard. They reply with <see cref="BeginHandOffAck" />.
        /// When all have replied the <see cref="PersistentShardCoordinator" /> continues by sending
        /// <see cref="HandOff" /> to the <see cref="ShardRegion" /> responsible for the shard.
        /// </summary>
        [Serializable]
        public sealed class BeginHandOff : ICoordinatorMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public BeginHandOff(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as BeginHandOff;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// Acknowledgement of <see cref="BeginHandOff"/>
        /// </summary>
        [Serializable]
        public sealed class BeginHandOffAck : ICoordinatorCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public BeginHandOffAck(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as BeginHandOffAck;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// When all <see cref="ShardRegion"/> actors have acknowledged the <see cref="BeginHandOff"/> the
        /// <see cref="PersistentShardCoordinator"/> sends this message to the <see cref="ShardRegion"/> responsible for the
        /// shard. The <see cref="ShardRegion"/> is supposed to stop all entries in that shard and when
        /// all entries have terminated reply with <see cref="ShardStopped"/> to the <see cref="PersistentShardCoordinator"/>.
        /// </summary>
        [Serializable]
        public sealed class HandOff : ICoordinatorMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public HandOff(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as HandOff;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// Reply to <see cref="HandOff"/> when all entries in the shard have been terminated.
        /// </summary>
        [Serializable]
        public sealed class ShardStopped : ICoordinatorCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public ShardStopped(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardStopped;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// Result of <see cref="PersistentShardCoordinator.AllocateShard"/> is piped to self with this message.
        /// </summary>
        [Serializable]
        public sealed class AllocateShardResult : ICoordinatorCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ShardRegion; // option
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef GetShardHomeSender;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            /// <param name="shardRegion">TBD</param>
            /// <param name="getShardHomeSender">TBD</param>
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
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IEnumerable<ShardId> Shards;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shards">TBD</param>
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
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ShardRegion;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shardRegion">TBD</param>
            public GracefulShutdownRequest(IActorRef shardRegion)
            {
                ShardRegion = shardRegion;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as GracefulShutdownRequest;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardRegion.Equals(other.ShardRegion);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ShardRegion?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// DomainEvents for the persistent state of the event sourced PersistentShardCoordinator
        /// </summary>
        public interface IDomainEvent: IClusterShardingSerializable { }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public class ShardRegionRegistered : IDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Region;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="region">TBD</param>
            public ShardRegionRegistered(IActorRef region)
            {
                Region = region;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardRegionRegistered;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Region.Equals(other.Region);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Region?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public class ShardRegionProxyRegistered : IDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef RegionProxy;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="regionProxy">TBD</param>
            public ShardRegionProxyRegistered(IActorRef regionProxy)
            {
                RegionProxy = regionProxy;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardRegionProxyRegistered;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return RegionProxy.Equals(other.RegionProxy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return RegionProxy?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public class ShardRegionTerminated : IDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Region;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="region">TBD</param>
            public ShardRegionTerminated(IActorRef region)
            {
                Region = region;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardRegionTerminated;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Region.Equals(other.Region);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Region?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public class ShardRegionProxyTerminated : IDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef RegionProxy;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="regionProxy">TBD</param>
            public ShardRegionProxyTerminated(IActorRef regionProxy)
            {
                RegionProxy = regionProxy;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardRegionProxyTerminated;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return RegionProxy.Equals(other.RegionProxy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return RegionProxy?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public class ShardHomeAllocated : IDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Region;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            /// <param name="region">TBD</param>
            public ShardHomeAllocated(string shard, IActorRef region)
            {
                Shard = shard;
                Region = region;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardHomeAllocated;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard)
                    && Region.Equals(other.Region);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Shard?.GetHashCode() ?? 0;
                    hashCode = (hashCode * 397) ^ (Region?.GetHashCode() ?? 0);
                    return hashCode;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public class ShardHomeDeallocated : IDomainEvent
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public ShardHomeDeallocated(string shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                var other = obj as ShardHomeDeallocated;

                if (ReferenceEquals(other, null)) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return Shard?.GetHashCode() ?? 0;
                }
            }

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class StateInitialized
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly StateInitialized Instance = new StateInitialized();
            private StateInitialized() { }
        }

        #endregion

    }
}