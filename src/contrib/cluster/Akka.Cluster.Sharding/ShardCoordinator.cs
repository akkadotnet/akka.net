//-----------------------------------------------------------------------
// <copyright file="ShardCoordinator.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Sharding
{
    using ShardId = String;

    /// <summary>
    /// Singleton coordinator that decides where to allocate shards.
    /// </summary>
    internal class ShardCoordinator
    {
        #region Message types internal

        /// <summary>
        /// Used as a special termination message from <see cref="ClusterSharding"/>
        /// </summary>
        internal sealed class Terminate : IDeadLetterSuppression
        {
            public static readonly Terminate Instance = new Terminate();

            private Terminate()
            {
            }
        }

        /// <summary>
        /// Messages sent to the coordinator.
        /// </summary>
        internal interface ICoordinatorCommand : IClusterShardingSerializable { }

        /// <summary>
        /// Messages sent from the coordinator.
        /// </summary>
        internal interface ICoordinatorMessage : IClusterShardingSerializable { }

        /// <summary>
        /// <see cref="Sharding.ShardRegion"/> registers to <see cref="ShardCoordinator"/>, until it receives <see cref="RegisterAck"/>.
        /// </summary>
        [Serializable]
        internal sealed class Register : ICoordinatorCommand, IDeadLetterSuppression, IEquatable<Register>
        {
            /// <summary>
            /// Reference to a shard region actor.
            /// </summary>
            public readonly IActorRef ShardRegion;

            /// <summary>
            /// Creates a new <see cref="Register"/> request for a given <paramref name="shardRegion"/>.
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
                return Equals(obj as Register);
            }

            public bool Equals(Register other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardRegion.Equals(other.ShardRegion);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return ShardRegion.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"Register({ShardRegion})";

            #endregion
        }

        /// <summary>
        /// <see cref="ShardRegion"/> in proxy only mode registers to <see cref="ShardCoordinator"/>, until it receives <see cref="RegisterAck"/>.
        /// </summary>
        [Serializable]
        internal sealed class RegisterProxy : ICoordinatorCommand, IDeadLetterSuppression, IEquatable<RegisterProxy>
        {
            /// <summary>
            /// Reference to a shard region proxy actor.
            /// </summary>
            public readonly IActorRef ShardRegionProxy;

            /// <summary>
            /// Creates a new <see cref="RegisterProxy"/> request for a given <paramref name="shardRegionProxy"/>.
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
                return Equals(obj as RegisterProxy);
            }

            public bool Equals(RegisterProxy other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardRegionProxy.Equals(other.ShardRegionProxy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return ShardRegionProxy.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"RegisterProxy({ShardRegionProxy})";

            #endregion
        }

        /// <summary>
        /// Acknowledgement from <see cref="ShardCoordinator"/> that <see cref="Register"/> or <see cref="RegisterProxy"/> was successful.
        /// </summary>
        internal sealed class RegisterAck : ICoordinatorMessage, IEquatable<RegisterAck>
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
                return Equals(obj as RegisterAck);
            }

            public bool Equals(RegisterAck other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Coordinator.Equals(other.Coordinator);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Coordinator.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"RegisterAck({Coordinator})";

            #endregion
        }


        /// <summary>
        /// <see cref="ShardRegion"/> requests the location of a shard by sending this message
        /// to the <see cref="ShardCoordinator"/>.
        /// </summary>
        [Serializable]
        internal sealed class GetShardHome : ICoordinatorCommand, IDeadLetterSuppression, IEquatable<GetShardHome>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public GetShardHome(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as GetShardHome);
            }

            public bool Equals(GetShardHome other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"GetShardHome({Shard})";

            #endregion
        }

        /// <summary>
        /// <see cref="ShardCoordinator"/> replies with this message for <see cref="GetShardHome"/> requests.
        /// </summary>
        [Serializable]
        internal sealed class ShardHome : ICoordinatorMessage, IEquatable<ShardHome>
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
            public ShardHome(ShardId shard, IActorRef @ref)
            {
                Shard = shard;
                Ref = @ref;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardHome);
            }

            public bool Equals(ShardHome other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard)
                    && Ref.Equals(other.Ref);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Shard.GetHashCode();
                    hashCode = (hashCode * 397) ^ Ref.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardHome(shardId:{Shard}, ref:{Ref})";

            #endregion
        }

        /// <summary>
        /// <see cref="ShardCoordinator"/> informs a <see cref="ShardRegion"/> that it is hosting this shard
        /// </summary>
        [Serializable]
        internal sealed class HostShard : ICoordinatorMessage, IEquatable<HostShard>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public HostShard(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as HostShard);
            }

            public bool Equals(HostShard other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"HostShard({Shard})";

            #endregion
        }

        /// <summary>
        /// <see cref="ShardRegion"/> replies with this message for <see cref="HostShard"/> requests which lead to it hosting the shard
        /// </summary>
        [Serializable]
        internal sealed class ShardStarted : ICoordinatorMessage, IEquatable<ShardStarted>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public ShardStarted(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardStarted);
            }

            public bool Equals(ShardStarted other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardStarted({Shard})";

            #endregion
        }

        /// <summary>
        /// <see cref="ShardCoordinator" /> initiates rebalancing process by sending this message
        /// to all registered <see cref="ShardRegion" /> actors (including proxy only). They are
        /// supposed to discard their known location of the shard, i.e. start buffering
        /// incoming messages for the shard. They reply with <see cref="BeginHandOffAck" />.
        /// When all have replied the <see cref="ShardCoordinator" /> continues by sending
        /// <see cref="HandOff" /> to the <see cref="ShardRegion" /> responsible for the shard.
        /// </summary>
        [Serializable]
        internal sealed class BeginHandOff : ICoordinatorMessage, IEquatable<BeginHandOff>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public BeginHandOff(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as BeginHandOff);
            }

            public bool Equals(BeginHandOff other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"BeginHandOff({Shard})";

            #endregion
        }


        /// <summary>
        /// Acknowledgement of <see cref="BeginHandOff"/>
        /// </summary>
        [Serializable]
        internal sealed class BeginHandOffAck : ICoordinatorCommand, IEquatable<BeginHandOffAck>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public BeginHandOffAck(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as BeginHandOffAck);
            }

            public bool Equals(BeginHandOffAck other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"BeginHandOffAck({Shard})";

            #endregion
        }

        /// <summary>
        /// When all <see cref="ShardRegion"/> actors have acknowledged the <see cref="BeginHandOff"/> the
        /// <see cref="ShardCoordinator"/> sends this message to the <see cref="ShardRegion"/> responsible for the
        /// shard. The <see cref="ShardRegion"/> is supposed to stop all entities in that shard and when
        /// all entities have terminated reply with <see cref="ShardStopped"/> to the <see cref="ShardCoordinator"/>.
        /// </summary>
        [Serializable]
        internal sealed class HandOff : ICoordinatorMessage, IEquatable<HandOff>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public HandOff(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as HandOff);
            }

            public bool Equals(HandOff other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"HandOff({Shard})";

            #endregion
        }

        /// <summary>
        /// Reply to <see cref="HandOff"/> when all entities in the shard have been terminated.
        /// </summary>
        [Serializable]
        internal sealed class ShardStopped : ICoordinatorCommand, IEquatable<ShardStopped>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public ShardStopped(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardStopped);
            }

            public bool Equals(ShardStopped other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardStopped({Shard})";

            #endregion
        }

        /// <summary>
        /// <see cref="Sharding.ShardRegion"/> requests full handoff to be able to shutdown gracefully.
        /// </summary>
        [Serializable]
        internal sealed class GracefulShutdownRequest : ICoordinatorCommand, IDeadLetterSuppression, IEquatable<GracefulShutdownRequest>
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
                return Equals(obj as GracefulShutdownRequest);
            }

            public bool Equals(GracefulShutdownRequest other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return ShardRegion.Equals(other.ShardRegion);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return ShardRegion.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"GracefulShutdownRequest({ShardRegion})";

            #endregion
        }

        /// <summary>
        /// DomainEvents for the persistent state of the event sourced ShardCoordinator
        /// </summary>
        public interface IDomainEvent : IClusterShardingSerializable { }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardRegionRegistered : IDomainEvent, IEquatable<ShardRegionRegistered>
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
                return Equals(obj as ShardRegionRegistered);
            }

            public bool Equals(ShardRegionRegistered other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Region.Equals(other.Region);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Region.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardRegionRegistered({Region})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardRegionProxyRegistered : IDomainEvent, IEquatable<ShardRegionProxyRegistered>
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
                return Equals(obj as ShardRegionProxyRegistered);
            }

            public bool Equals(ShardRegionProxyRegistered other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return RegionProxy.Equals(other.RegionProxy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return RegionProxy.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardRegionProxyRegistered({RegionProxy})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardRegionTerminated : IDomainEvent, IEquatable<ShardRegionTerminated>
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
                return Equals(obj as ShardRegionTerminated);
            }

            public bool Equals(ShardRegionTerminated other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Region.Equals(other.Region);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Region.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardRegionTerminated({Region})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardRegionProxyTerminated : IDomainEvent, IEquatable<ShardRegionProxyTerminated>
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
                return Equals(obj as ShardRegionProxyTerminated);
            }

            public bool Equals(ShardRegionProxyTerminated other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return RegionProxy.Equals(other.RegionProxy);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return RegionProxy.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardRegionProxyTerminated({RegionProxy})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardHomeAllocated : IDomainEvent, IEquatable<ShardHomeAllocated>
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
            public ShardHomeAllocated(ShardId shard, IActorRef region)
            {
                Shard = shard;
                Region = region;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardHomeAllocated);
            }

            public bool Equals(ShardHomeAllocated other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard)
                    && Region.Equals(other.Region);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = Shard.GetHashCode();
                    hashCode = (hashCode * 397) ^ Region.GetHashCode();
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardHomeAllocated(shard:{Shard}, region:{Region})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardHomeDeallocated : IDomainEvent, IEquatable<ShardHomeDeallocated>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            public ShardHomeDeallocated(ShardId shard)
            {
                Shard = shard;
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardHomeDeallocated);
            }

            public bool Equals(ShardHomeDeallocated other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shard.Equals(other.Shard);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return Shard.GetHashCode();
            }

            /// <inheritdoc/>
            public override string ToString() => $"ShardHomeDeallocated({Shard})";

            #endregion
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class ShardCoordinatorInitialized : IDomainEvent, IEquatable<ShardCoordinatorInitialized>
        {
            public static readonly ShardCoordinatorInitialized Instance = new ShardCoordinatorInitialized();

            private ShardCoordinatorInitialized()
            {
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as ShardCoordinatorInitialized);
            }

            public bool Equals(ShardCoordinatorInitialized other)
            {
                if (other is null) return false;
                return true;
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                return 0;
            }

            /// <inheritdoc/>
            public override string ToString() => "ShardCoordinatorInitialized";

            #endregion
        }


        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        internal sealed class StateInitialized
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly StateInitialized Instance = new StateInitialized();

            private StateInitialized() { }
        }

        /// <summary>
        /// State of the shard coordinator.
        /// Was also used as the persistent state in the old persistent coordinator.
        /// </summary>
        [Serializable]
        internal sealed class CoordinatorState : IClusterShardingSerializable, IEquatable<CoordinatorState>
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly CoordinatorState Empty = new CoordinatorState();

            /// <summary>
            /// Region for each shard.
            /// </summary>
            public readonly IImmutableDictionary<ShardId, IActorRef> Shards;

            /// <summary>
            /// Shards for each region.
            /// </summary>
            public readonly IImmutableDictionary<IActorRef, IImmutableList<ShardId>> Regions;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<IActorRef> RegionProxies;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<ShardId> UnallocatedShards;

            public readonly bool RememberEntities;

            private CoordinatorState() : this(
                shards: ImmutableDictionary<ShardId, IActorRef>.Empty,
                regions: ImmutableDictionary<IActorRef, IImmutableList<ShardId>>.Empty,
                regionProxies: ImmutableHashSet<IActorRef>.Empty,
                unallocatedShards: ImmutableHashSet<ShardId>.Empty,
                rememberEntities: false)
            { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shards">TBD</param>
            /// <param name="regions">TBD</param>
            /// <param name="regionProxies">TBD</param>
            /// <param name="unallocatedShards">TBD</param>
            /// <param name="rememberEntities">TBD</param>
            public CoordinatorState(
                IImmutableDictionary<ShardId, IActorRef> shards,
                IImmutableDictionary<IActorRef, IImmutableList<ShardId>> regions,
                IImmutableSet<IActorRef> regionProxies,
                IImmutableSet<ShardId> unallocatedShards,
                bool rememberEntities = false)
            {
                Shards = shards;
                Regions = regions;
                RegionProxies = regionProxies;
                UnallocatedShards = unallocatedShards;
                RememberEntities = rememberEntities;
            }

            public CoordinatorState WithRememberEntities(bool enabled)
            {
                if (enabled)
                    return Copy(rememberEntities: enabled);
                else
                    return Copy(unallocatedShards: ImmutableHashSet<ShardId>.Empty, rememberEntities: enabled);
            }

            public bool IsEmpty
            {
                get
                {
                    return Shards.Count == 0 && Regions.Count == 0 && RegionProxies.Count == 0;
                }
            }

            public IImmutableSet<ShardId> AllShards
            {
                get
                {
                    return Shards.Keys.Union(UnallocatedShards).ToImmutableHashSet();
                }
            }

            public override string ToString()
            {
                return $"CoordinatorState({string.Join("", Shards.Select(i => $"[{i.Key}: {i.Value}]"))})";
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="e">TBD</param>
            /// <exception cref="ArgumentException">TBD</exception>
            /// <returns>TBD</returns>
            public CoordinatorState Updated(IDomainEvent e)
            {
                switch (e)
                {
                    case ShardRegionRegistered message:
                        if (Regions.ContainsKey(message.Region))
                            throw new ArgumentException($"Region {message.Region} already registered: {this}", nameof(e));

                        return Copy(regions: Regions.SetItem(message.Region, ImmutableList<ShardId>.Empty));

                    case ShardRegionProxyRegistered message:
                        if (RegionProxies.Contains(message.RegionProxy))
                            throw new ArgumentException($"Region proxy {message.RegionProxy} already registered: {this}", nameof(e));

                        return Copy(regionProxies: RegionProxies.Add(message.RegionProxy));

                    case ShardRegionTerminated message:
                        {
                            if (!Regions.TryGetValue(message.Region, out var shardRegions))
                                throw new ArgumentException($"Terminated region {message.Region} not registered: {this}", nameof(e));

                            var newUnallocatedShards = RememberEntities ? UnallocatedShards.Union(shardRegions) : UnallocatedShards;
                            return Copy(
                                regions: Regions.Remove(message.Region),
                                shards: Shards.RemoveRange(shardRegions),
                                unallocatedShards: newUnallocatedShards);
                        }

                    case ShardRegionProxyTerminated message:
                        if (!RegionProxies.Contains(message.RegionProxy))
                            throw new ArgumentException($"Terminated region proxy {message.RegionProxy} not registered: {this}", nameof(e));

                        return Copy(regionProxies: RegionProxies.Remove(message.RegionProxy));

                    case ShardHomeAllocated message:
                        {
                            if (!Regions.TryGetValue(message.Region, out var shardRegions))
                                throw new ArgumentException($"Region {message.Region} not registered: {this}", nameof(e));
                            if (Shards.ContainsKey(message.Shard))
                                throw new ArgumentException($"Shard {message.Shard} already allocated: {this}", nameof(e));

                            var newUnallocatedShards = RememberEntities ? UnallocatedShards.Remove(message.Shard) : UnallocatedShards;
                            return Copy(
                                shards: Shards.SetItem(message.Shard, message.Region),
                                regions: Regions.SetItem(message.Region, shardRegions.Add(message.Shard)),
                                unallocatedShards: newUnallocatedShards);
                        }
                    case ShardHomeDeallocated message:
                        {
                            if (!Shards.TryGetValue(message.Shard, out var region))
                                throw new ArgumentException($"Shard {message.Shard} not allocated: {this}", nameof(e));
                            if (!Regions.TryGetValue(region, out var shardRegions))
                                throw new ArgumentException($"Region {region} for shard {message.Shard} not registered: {this}", nameof(e));

                            var newUnallocatedShards = RememberEntities ? UnallocatedShards.Add(message.Shard) : UnallocatedShards;
                            return Copy(
                                shards: Shards.Remove(message.Shard),
                                regions: Regions.SetItem(region, shardRegions.Where(s => s != message.Shard).ToImmutableList()),
                                unallocatedShards: newUnallocatedShards);
                        }
                    case ShardCoordinatorInitialized _:
                        return this;
                }

                return this;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shards">TBD</param>
            /// <param name="regions">TBD</param>
            /// <param name="regionProxies">TBD</param>
            /// <param name="unallocatedShards">TBD</param>
            /// <param name="rememberEntities">TBD</param>
            /// <returns>TBD</returns>
            public CoordinatorState Copy(
                IImmutableDictionary<ShardId, IActorRef> shards = null,
                IImmutableDictionary<IActorRef, IImmutableList<ShardId>> regions = null,
                IImmutableSet<IActorRef> regionProxies = null,
                IImmutableSet<ShardId> unallocatedShards = null,
                bool? rememberEntities = null)
            {
                if (shards == null && regions == null && regionProxies == null && unallocatedShards == null && rememberEntities == null) return this;

                return new CoordinatorState(
                    shards ?? Shards,
                    regions ?? Regions,
                    regionProxies ?? RegionProxies,
                    unallocatedShards ?? UnallocatedShards,
                    rememberEntities ?? RememberEntities);
            }

            #region Equals

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                return Equals(obj as CoordinatorState);
            }

            public bool Equals(CoordinatorState other)
            {
                if (other is null) return false;
                if (ReferenceEquals(other, this)) return true;

                return Shards.SequenceEqual(other.Shards)
                    && Regions.Keys.SequenceEqual(other.Regions.Keys)
                    && RegionProxies.SequenceEqual(other.RegionProxies)
                    && UnallocatedShards.SequenceEqual(other.UnallocatedShards)
                    && RememberEntities.Equals(other.RememberEntities);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = 13;

                    foreach (var v in Shards)
                        hashCode = (hashCode * 397) ^ v.Key.GetHashCode();

                    foreach (var v in Regions)
                        hashCode = (hashCode * 397) ^ v.Key.GetHashCode();

                    foreach (var v in RegionProxies)
                        hashCode = (hashCode * 397) ^ v.GetHashCode();

                    foreach (var v in UnallocatedShards)
                        hashCode = (hashCode * 397) ^ v.GetHashCode();

                    return hashCode;
                }
            }

            #endregion
        }


        #endregion

        #region Message types

        /// <summary>
        /// Periodic message to trigger rebalance.
        /// </summary>
        private sealed class RebalanceTick
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly RebalanceTick Instance = new RebalanceTick();

            private RebalanceTick() { }
        }

        /// <summary>
        /// End of rebalance process performed by <see cref="RebalanceWorker"/>.
        /// </summary>
        private sealed class RebalanceDone
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly bool Ok;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shard">TBD</param>
            /// <param name="ok">TBD</param>
            public RebalanceDone(ShardId shard, bool ok)
            {
                Shard = shard;
                Ok = ok;
            }
        }

        /// <summary>
        /// Check if we've received a shard start request.
        /// </summary>
        [Serializable]
        private sealed class ResendShardHost
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
            public ResendShardHost(ShardId shard, IActorRef region)
            {
                Shard = shard;
                Region = region;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        private sealed class DelayedShardRegionTerminated
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Region;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="region">TBD</param>
            public DelayedShardRegionTerminated(IActorRef region)
            {
                Region = region;
            }
        }

        /// <summary>
        /// Result of <see cref="IShardAllocationStrategy.AllocateShard"/> is piped to self with this message.
        /// </summary>
        [Serializable]
        private sealed class AllocateShardResult
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ShardId Shard;
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef ShardRegion;
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
            public AllocateShardResult(ShardId shard, IActorRef shardRegion, IActorRef getShardHomeSender)
            {
                Shard = shard;
                ShardRegion = shardRegion;
                GetShardHomeSender = getShardHomeSender;
            }

            /// <inheritdoc/>
            public override string ToString() => $"AllocateShardResult(shard:{Shard}, shardRegion:{ShardRegion}, sender:{GetShardHomeSender})";
        }

        /// <summary>
        /// Result of <see cref="IShardAllocationStrategy.Rebalance"/> is piped to self with this message.
        /// </summary>
        [Serializable]
        private sealed class RebalanceResult
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly IImmutableSet<ShardId> Shards;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="shards">TBD</param>
            public RebalanceResult(IImmutableSet<ShardId> shards)
            {
                Shards = shards;
            }

            /// <inheritdoc/>
            public override string ToString() => $"RebalanceResult({string.Join(", ", Shards)})";
        }

        #endregion

        /// <summary>
        /// INTERNAL API. Rebalancing process is performed by this actor.
        /// It sends <see cref="BeginHandOff"/> to all <see cref="ShardRegion"/> actors followed
        /// by <see cref="HandOff"/> to the <see cref="ShardRegion"/> responsible for
        /// the shard. When the handoff is completed it sends <see cref="RebalanceDone"/> to its parent
        /// <see cref="ShardCoordinator"/>. If the process takes longer than the `handOffTimeout` it
        /// also sends <see cref="RebalanceDone"/>.
        /// </summary>
        internal class RebalanceWorker : ActorBase, IWithTimers
        {
            public sealed class ShardRegionTerminated
            {
                public ShardRegionTerminated(IActorRef region)
                {
                    Region = region;
                }

                public IActorRef Region { get; }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="shard">TBD</param>
            /// <param name="shardRegionFrom">TBD</param>
            /// <param name="handOffTimeout">TBD</param>
            /// <param name="regions">TBD</param>
            /// <param name="isRebalance">TBD</param>
            /// <returns>TBD</returns>
            public static Props Props(
                string typeName,
                string shard,
                IActorRef shardRegionFrom,
                TimeSpan handOffTimeout,
                IEnumerable<IActorRef> regions,
                bool isRebalance) =>
                Actor.Props.Create(() => new RebalanceWorker(typeName, shard, shardRegionFrom, handOffTimeout, regions, isRebalance));

            private readonly string _typeName;
            private readonly ShardId _shard;
            private readonly IActorRef _shardRegionFrom;
            private readonly ISet<IActorRef> _remaining;
            private readonly bool _isRebalance;
            private ILoggingAdapter _log;

            private ILoggingAdapter Log { get { return _log ?? (_log = Context.GetLogger()); } }

            public ITimerScheduler Timers { get; set; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="typeName">TBD</param>
            /// <param name="shard">TBD</param>
            /// <param name="shardRegionFrom">TBD</param>
            /// <param name="handOffTimeout">TBD</param>
            /// <param name="regions">TBD</param>
            /// <param name="isRebalance">TBD</param>
            public RebalanceWorker(
                string typeName,
                string shard,
                IActorRef shardRegionFrom,
                TimeSpan handOffTimeout,
                IEnumerable<IActorRef> regions,
                bool isRebalance)
            {
                _typeName = typeName;
                _shard = shard;
                _shardRegionFrom = shardRegionFrom;
                _isRebalance = isRebalance;

                _remaining = new HashSet<IActorRef>(regions);
                foreach (var region in _remaining)
                {
                    region.Tell(new BeginHandOff(shard));
                }

                if (isRebalance)
                    Log.Debug("{0}: Rebalance [{1}] from [{2}] regions", typeName, shard, regions.Count());
                else
                    Log.Debug("{0}: Shutting down shard [{1}] from region [{2}]. Asking [{3}] region(s) to hand-off shard",
                        _typeName,
                        shard,
                        shardRegionFrom,
                        regions.Count());

                Timers.StartSingleTimer("hand-off-timeout", ReceiveTimeout.Instance, handOffTimeout);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="message">TBD</param>
            /// <returns>TBD</returns>
            protected override bool Receive(object message)
            {
                switch (message)
                {
                    case BeginHandOffAck hoa when _shard == hoa.Shard:
                        Log.Debug("{0}: BeginHandOffAck for shard [{1}] received from [{2}].", _typeName, _shard, Sender);
                        Acked(Sender);
                        return true;
                    case ShardRegionTerminated t:
                        if (_remaining.Contains(t.Region))
                        {
                            Log.Debug("{0}: ShardRegion [{1}] terminated while waiting for BeginHandOffAck for shard [{2}].", _typeName, t.Region, _shard);
                        }
                        Acked(t.Region);
                        return true;
                    case ReceiveTimeout _:
                        if (_isRebalance)
                            Log.Debug("{0}: Rebalance of [{1}] from [{2}] timed out", _typeName, _shard, _shardRegionFrom);
                        else
                            Log.Debug("{0}: Shutting down [{1}] shard from [{2}] timed out", _typeName, _shard, _shardRegionFrom);
                        Done(false);
                        return true;
                }
                return false;
            }

            private void Acked(IActorRef shardRegion)
            {
                _remaining.Remove(shardRegion);
                if (_remaining.Count == 0)
                {
                    Log.Debug("{0}: All shard regions acked, handing off shard [{0}].", _typeName, _shard);
                    _shardRegionFrom.Tell(new HandOff(_shard));
                    Context.Become(StoppingShard);
                }
                else
                {
                    Log.Debug("{0}: Remaining shard regions: {1}", _typeName, _remaining.Count);
                }
            }

            private bool StoppingShard(object message)
            {
                switch (message)
                {
                    case ShardStopped ms when _shard == ms.Shard:
                        Done(true);
                        return true;
                    case ReceiveTimeout _:
                        Done(false);
                        return true;
                    case ShardRegionTerminated t when t.Region.Equals(_shardRegionFrom):
                        Log.Debug("{0}: ShardRegion [{1}] terminated while waiting for ShardStopped for shard [{2}].", _typeName, t.Region, _shard);
                        Done(true);
                        return true;
                }
                return false;
            }

            private void Done(bool ok)
            {
                Context.Parent.Tell(new RebalanceDone(_shard, ok));
                Context.Stop(Self);
            }
        }

        private readonly IActorContext context;
        private readonly Action<IDomainEvent, Action<IDomainEvent>> update;
        private readonly Action unstashOneGetShardHomeRequest;

        private readonly IActorRef ignoreRef;
        private readonly Cluster cluster;
        private readonly TimeSpan removalMargin;
        private readonly int minMembers;
        private bool allRegionsRegistered = false;

        // rebalanceInProgress for the ShardId keys, pending GetShardHome requests by the ActorRef values
        private ImmutableDictionary<string, ImmutableHashSet<IActorRef>> rebalanceInProgress = ImmutableDictionary<string, ImmutableHashSet<IActorRef>>.Empty;
        private ImmutableHashSet<IActorRef> rebalanceWorkers = ImmutableHashSet<IActorRef>.Empty;
        private ImmutableDictionary<string, ICancelable> unAckedHostShards = ImmutableDictionary<string, ICancelable>.Empty;
        // regions that have requested handoff, for graceful shutdown
        private ImmutableHashSet<IActorRef> gracefulShutdownInProgress = ImmutableHashSet<IActorRef>.Empty;
        private ImmutableHashSet<IActorRef> aliveRegions = ImmutableHashSet<IActorRef>.Empty;
        private ImmutableHashSet<IActorRef> regionTerminationInProgress = ImmutableHashSet<IActorRef>.Empty;
        private readonly ICancelable rebalanceTask;

        internal string TypeName { get; }
        internal ClusterShardingSettings Settings { get; }
        internal IShardAllocationStrategy AllocationStrategy { get; }
        internal ILoggingAdapter Log { get; }
        internal bool VerboseDebug { get; }
        internal CoordinatorState State { get; set; }


        public ShardCoordinator(
            string typeName,
            ClusterShardingSettings settings,
            IShardAllocationStrategy allocationStrategy,

            IActorContext context,
            ILoggingAdapter log,
            bool verboseDebug,
            Action<IDomainEvent, Action<IDomainEvent>> update,
            Action unstashOneGetShardHomeRequest

            )
        {
            TypeName = typeName;
            Settings = settings;
            AllocationStrategy = allocationStrategy;
            this.context = context;
            Log = log;
            VerboseDebug = verboseDebug;
            this.update = update;
            this.unstashOneGetShardHomeRequest = unstashOneGetShardHomeRequest;

            ignoreRef = context.System.IgnoreRef;

            cluster = Cluster.Get(context.System);
            removalMargin = cluster.DowningProvider.DownRemovalMargin;

            if (string.IsNullOrEmpty(settings.Role))
                minMembers = cluster.Settings.MinNrOfMembers;
            else
                minMembers = cluster.Settings.MinNrOfMembersOfRole.GetValueOrDefault(settings.Role, 1);

            State = CoordinatorState.Empty.WithRememberEntities(settings.RememberEntities);

            rebalanceTask = context.System.Scheduler.ScheduleTellRepeatedlyCancelable(Settings.TuningParameters.RebalanceInterval, Settings.TuningParameters.RebalanceInterval, context.Self, RebalanceTick.Instance, ActorRefs.NoSender);

            cluster.Subscribe(context.Self, ClusterEvent.SubscriptionInitialStateMode.InitialStateAsEvents, new[] { typeof(ClusterEvent.ClusterShuttingDown) });
        }


        internal void PreStart()
        {
            switch (AllocationStrategy)
            {
                case IStartableAllocationStrategy strategy:
                    strategy.Start();
                    break;
                case IActorSystemDependentAllocationStrategy strategy:
                    strategy.Start(context.System);
                    break;
            }
        }

        internal void PostStop()
        {
            rebalanceTask.Cancel();
            cluster.Unsubscribe(context.Self);
        }

        internal bool IsMember(IActorRef region)
        {
            var regionAddress = region.Path.Address;
            return regionAddress.Equals(context.Self.Path.Address) || cluster.State.IsMemberUp(regionAddress);
        }

        internal bool Active(object message)
        {
            switch (message)
            {
                case Register m:
                    if (IsMember(m.ShardRegion))
                    {
                        Log.Debug("{0}: ShardRegion registered: [{1}]", TypeName, m.ShardRegion);
                        aliveRegions = aliveRegions.Add(m.ShardRegion);
                        if (State.Regions.ContainsKey(m.ShardRegion))
                        {
                            m.ShardRegion.Tell(new RegisterAck(context.Self));
                            AllocateShardHomesForRememberEntities();
                        }
                        else
                        {
                            gracefulShutdownInProgress = gracefulShutdownInProgress.Remove(m.ShardRegion);
                            Update(new ShardRegionRegistered(m.ShardRegion), evt =>
                            {
                                State = State.Updated(evt);
                                context.Watch(m.ShardRegion);
                                m.ShardRegion.Tell(new RegisterAck(context.Self));
                                AllocateShardHomesForRememberEntities();
                            });
                        }
                    }
                    else
                    {
                        Log.Debug("{0}: ShardRegion [{1}] was not registered since the coordinator currently does not know about a node of that region",
                            TypeName,
                            m.ShardRegion);
                    }
                    return true;
                case RegisterProxy m:
                    if (IsMember(m.ShardRegionProxy))
                    {
                        Log.Debug("{0}: ShardRegion proxy registered: [{1}]", TypeName, m.ShardRegionProxy);
                        if (State.RegionProxies.Contains(m.ShardRegionProxy))
                            m.ShardRegionProxy.Tell(new RegisterAck(context.Self));
                        else
                        {
                            Update(new ShardRegionProxyRegistered(m.ShardRegionProxy), evt =>
                            {
                                State = State.Updated(evt);
                                context.Watch(m.ShardRegionProxy);
                                m.ShardRegionProxy.Tell(new RegisterAck(context.Self));
                            });
                        }
                    }
                    return true;
                case GetShardHome m:
                    if (!HandleGetShardHome(m.Shard))
                    {
                        // location not know, yet
                        var activeRegions = State.Regions.RemoveRange(gracefulShutdownInProgress).RemoveRange(regionTerminationInProgress);
                        if (activeRegions.Count > 0)
                        {
                            var getShardHomeSender = context.Sender;
                            var regionTask = AllocationStrategy.AllocateShard(getShardHomeSender, m.Shard, activeRegions);

                            // if task completed immediately, just continue
                            if (regionTask.IsCompleted && !regionTask.IsFaulted)
                                ContinueGetShardHome(m.Shard, regionTask.Result, getShardHomeSender);
                            else
                            {
                                regionTask.PipeTo(context.Self,
                                    success: region => new AllocateShardResult(m.Shard, region, getShardHomeSender),
                                    failure: e =>
                                    {
                                        Log.Error(e, "{0}: Shard [{1}] allocation failed.", TypeName, m.Shard);
                                        return new AllocateShardResult(m.Shard, null, getShardHomeSender);
                                    });
                            }
                        }
                    }
                    return true;

                case AllocateShardResult m when m.ShardRegion == null:
                    Log.Debug("{0}: Shard [{1}] allocation failed. It will be retried.", TypeName, m.Shard);
                    return true;

                case AllocateShardResult m when m.ShardRegion != null:
                    ContinueGetShardHome(m.Shard, m.ShardRegion, m.GetShardHomeSender);
                    return true;

                case ShardStarted m:
                    if (unAckedHostShards.TryGetValue(m.Shard, out var cancel))
                    {
                        cancel.Cancel();
                        unAckedHostShards = unAckedHostShards.Remove(m.Shard);
                    }
                    return true;

                case ResendShardHost m:
                    {
                        if (State.Shards.TryGetValue(m.Shard, out var region) && region.Equals(region))
                            SendHostShardMsg(m.Shard, region);
                        else
                        {
                            //Reallocated to another region
                        }
                    }
                    return true;

                case RebalanceTick _:
                    // optimisation: don't rebalance with un-acked shards, will cause unnecessary rebalance
                    if (State.Regions.Count > 0 && unAckedHostShards.IsEmpty)
                    {
                        var shardsTask = AllocationStrategy.Rebalance(State.Regions, rebalanceInProgress.Keys.ToImmutableHashSet());
                        if (shardsTask.IsCompleted && !shardsTask.IsFaulted)
                            ContinueRebalance(shardsTask.Result);
                        else
                        {
                            shardsTask.PipeTo(context.Self,
                                success: shards => new RebalanceResult(shards),
                                failure: e => new RebalanceResult(ImmutableHashSet<ShardId>.Empty));
                        }
                    }
                    return true;

                case RebalanceResult m:
                    ContinueRebalance(m.Shards);
                    return true;

                case RebalanceDone m:
                    rebalanceWorkers = rebalanceWorkers.Remove(context.Sender);

                    if (m.Ok)
                    {
                        Log.Debug("{0}: Shard [{1}] deallocation completed successfully.", TypeName, m.Shard);

                        // The shard could have been removed by ShardRegionTerminated
                        if (State.Shards.ContainsKey(m.Shard))
                        {
                            Update(new ShardHomeDeallocated(m.Shard), evt =>
                            {
                                Log.Debug("{0}: Shard [{1}] deallocated after", TypeName, m.Shard);
                                State = State.Updated(evt);
                                ClearRebalanceInProgress(m.Shard);
                                AllocateShardHomesForRememberEntities();
                                context.Self.Tell(new GetShardHome(m.Shard), ignoreRef);
                            });
                        }
                        else
                        {
                            ClearRebalanceInProgress(m.Shard);
                        }
                    }
                    else
                    {
                        Log.Warning("{0}: Shard [{1}] deallocation didn't complete within [{2}].",
                            TypeName,
                            m.Shard,
                            Settings.TuningParameters.HandOffTimeout);

                        // was that due to a graceful region shutdown?
                        // if so, consider the region as still alive and let it retry to gracefully shutdown later
                        if (State.Shards.TryGetValue(m.Shard, out var region))
                        {
                            gracefulShutdownInProgress = gracefulShutdownInProgress.Remove(region);
                        }

                        ClearRebalanceInProgress(m.Shard);
                    }
                    return true;

                case GracefulShutdownRequest m:
                    if (!gracefulShutdownInProgress.Contains(m.ShardRegion))
                    {
                        if (State.Regions.TryGetValue(m.ShardRegion, out var shards))
                        {
                            if (Log.IsDebugEnabled)
                            {
                                if (VerboseDebug)
                                    Log.Debug("{0}: Graceful shutdown of region [{1}] with [{2}] shards [{3}]",
                                        TypeName,
                                        m.ShardRegion,
                                        shards.Count,
                                        string.Join(", ", shards));
                                else
                                    Log.Debug("{0}: Graceful shutdown of region [{1}] with [{2}] shards", TypeName, m.ShardRegion, shards.Count);
                            }
                            gracefulShutdownInProgress = gracefulShutdownInProgress.Add(m.ShardRegion);
                            ShutdownShards(m.ShardRegion, shards.ToImmutableHashSet());
                        }
                        else
                        {
                            Log.Debug("{0}: Unknown region requested graceful shutdown [{1}]", TypeName, m.ShardRegion);
                        }
                    }
                    return true;

                case GetClusterShardingStats m:

                    var tasks = aliveRegions.Select(regionActor => (Region: regionActor, Task: regionActor.Ask<ShardRegionStats>(GetShardRegionStats.Instance, m.Timeout))).ToImmutableList();
                    Task.WhenAll(tasks.Select(i => i.Task)).ContinueWith(ps =>
                    {
                        if (ps.IsFaulted || ps.IsCanceled)
                            return new ClusterShardingStats(ImmutableDictionary<Address, ShardRegionStats>.Empty);

                        var regions = tasks.Where(i => !i.Task.IsCanceled && !i.Task.IsFaulted).ToImmutableDictionary(i =>
                        {
                            Address regionAddress = i.Region.Path.Address;
                            Address address = (regionAddress.HasLocalScope && regionAddress.System == cluster.SelfAddress.System) ? cluster.SelfAddress : regionAddress;
                            return address;
                        }, j => j.Task.Result);

                        return new ClusterShardingStats(regions);
                    }).PipeTo(context.Sender);
                    return true;

                case ClusterEvent.ClusterShuttingDown _:
                    Log.Debug("{0}: Shutting down ShardCoordinator", TypeName);
                    // can't stop because supervisor will start it again,
                    // it will soon be stopped when singleton is stopped
                    context.Become(ShuttingDown);
                    return true;

                case GetCurrentRegions _:
                    var reply = new CurrentRegions(State.Regions.Keys.Select(r =>
                        string.IsNullOrEmpty(r.Path.Address.Host) ? cluster.SelfAddress : r.Path.Address).ToImmutableHashSet());
                    context.Sender.Tell(reply);
                    return true;

                case Terminate _:
                    if (rebalanceInProgress.Count == 0)
                        Log.Debug("{0}: Received termination message.", TypeName);
                    else if (Log.IsDebugEnabled)
                    {
                        if (VerboseDebug)
                            Log.Debug("{0}: Received termination message. Rebalance in progress of [{1}] shards [{2}].",
                                TypeName,
                                rebalanceInProgress.Count,
                                string.Join(", ", rebalanceInProgress.Keys));
                        else
                            Log.Debug("{0}: Received termination message. Rebalance in progress of [{1}] shards.",
                                TypeName,
                                rebalanceInProgress.Count);
                    }
                    context.Stop(context.Self);
                    return true;
            }

            return ReceiveTerminated(message);
        }

        private void ClearRebalanceInProgress(string shard)
        {
            if (rebalanceInProgress.TryGetValue(shard, out var pendingGetShardHome))
            {
                var msg = new GetShardHome(shard);
                foreach (var getShardHomeSender in pendingGetShardHome)
                {
                    context.Self.Tell(msg, getShardHomeSender);
                }
                rebalanceInProgress = rebalanceInProgress.Remove(shard);
            }
        }

        private void DeferGetShardHomeRequest(ShardId shard, IActorRef from)
        {
            Log.Debug(
                "{0}: GetShardHome [{1}] request from [{2}] deferred, because rebalance is in progress for this shard. " +
                "It will be handled when rebalance is done.",
                TypeName,
                shard,
                from);
            rebalanceInProgress = rebalanceInProgress.SetItem(shard, rebalanceInProgress.GetValueOrDefault(shard, ImmutableHashSet<IActorRef>.Empty).Add(from));
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="shard"></param>
        /// <returns>`true` if the message could be handled without state update, i.e.
        ///     the shard location was known or the request was deferred or ignored</returns>
        internal bool HandleGetShardHome(ShardId shard)
        {
            if (rebalanceInProgress.ContainsKey(shard))
            {
                DeferGetShardHomeRequest(shard, context.Sender);
                unstashOneGetShardHomeRequest(); // continue unstashing
                return true;
            }
            else if (!HasAllRegionsRegistered())
            {
                Log.Debug("{0}: GetShardHome [{1}] request from [{2}] ignored, because not all regions have registered yet.",
                    TypeName,
                    shard,
                    context.Sender);
                return true;
            }
            else
            {
                if (State.Shards.TryGetValue(shard, out var shardRegionRef))
                {
                    if (regionTerminationInProgress.Contains(shardRegionRef))
                        Log.Debug("{0}: GetShardHome [{1}] request ignored, due to region [{2}] termination in progress.",
                            TypeName,
                            shard,
                            shardRegionRef);
                    else
                        context.Sender.Tell(new ShardHome(shard, shardRegionRef));

                    unstashOneGetShardHomeRequest(); // continue unstashing
                    return true;
                }
                else
                {
                    return false; // location not known, yet, caller will handle allocation
                }
            }
        }

        internal bool ReceiveTerminated(object message)
        {
            switch (message)
            {
                case Terminated t:
                    if (State.Regions.ContainsKey(t.ActorRef))
                    {
                        if (removalMargin != TimeSpan.Zero && t.AddressTerminated && aliveRegions.Contains(t.ActorRef))
                        {
                            context.System.Scheduler.ScheduleTellOnce(removalMargin, context.Self, new DelayedShardRegionTerminated(t.ActorRef), ActorRefs.NoSender);
                            regionTerminationInProgress = regionTerminationInProgress.Add(t.ActorRef);
                        }
                        else
                            RegionTerminated(t.ActorRef);
                    }
                    else if (State.RegionProxies.Contains(t.ActorRef))
                    {
                        RegionProxyTerminated(t.ActorRef);
                    }
                    return true;

                case DelayedShardRegionTerminated t:
                    RegionTerminated(t.Region);
                    return true;
            }
            return false;
        }

        private void Update<TEvent>(TEvent e, Action<TEvent> handler) where TEvent : IDomainEvent
        {
            update(e, evn => handler((TEvent)evn));
        }

        internal void WatchStateActors()
        {

            // Optimization:
            // Consider regions that don't belong to the current cluster to be terminated.
            // This is an optimization that makes it operationally faster and reduces the
            // amount of lost messages during startup.
            var nodes = cluster.State.Members.Select(i => i.Address).ToImmutableHashSet();
            foreach (var i in State.Regions)
            {
                //case (ref, _) =>
                var a = i.Key.Path.Address;
                if (a.HasLocalScope || nodes.Contains(a))
                    context.Watch(i.Key);
                else
                    RegionTerminated(i.Key); // not part of cluster
            }
            foreach (var @ref in State.RegionProxies)
            {
                var a = @ref.Path.Address;
                if (a.HasLocalScope || nodes.Contains(a))
                    context.Watch(@ref);
                else
                    RegionProxyTerminated(@ref); // not part of cluster
            }

            // Let the quick (those not involving failure detection) Terminated messages
            // be processed before starting to reply to GetShardHome.
            // This is an optimization that makes it operational faster and reduces the
            // amount of lost messages during startup.
            context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(500), context.Self, StateInitialized.Instance, ActorRefs.NoSender);
        }

        internal void ReceiveStateInitialized()
        {
            foreach (var i in State.Shards)
                SendHostShardMsg(i.Key, i.Value);
            AllocateShardHomesForRememberEntities();
        }

        private bool HasAllRegionsRegistered()
        {
            // the check is only for startup, i.e. once all have registered we don't check more
            if (allRegionsRegistered)
                return true;
            else
            {
                allRegionsRegistered = aliveRegions.Count >= minMembers;
                return allRegionsRegistered;
            }
        }

        private void RegionTerminated(IActorRef @ref)
        {
            foreach (var rw in rebalanceWorkers)
                rw.Tell(new RebalanceWorker.ShardRegionTerminated(@ref));
            if (State.Regions.TryGetValue(@ref, out var shards))
            {
                Log.Debug("{0}: ShardRegion terminated: [{1}]", TypeName, @ref);
                regionTerminationInProgress = regionTerminationInProgress.Add(@ref);
                foreach (var s in shards)
                {
                    context.Self.Tell(new GetShardHome(s), ignoreRef);
                }

                Update(new ShardRegionTerminated(@ref), evt =>
                {
                    State = State.Updated(evt);
                    gracefulShutdownInProgress = gracefulShutdownInProgress.Remove(@ref);
                    regionTerminationInProgress = regionTerminationInProgress.Remove(@ref);
                    aliveRegions = aliveRegions.Remove(@ref);
                    AllocateShardHomesForRememberEntities();
                });
            }
        }

        private void RegionProxyTerminated(IActorRef @ref)
        {
            foreach (var rw in rebalanceWorkers)
                rw.Tell(new RebalanceWorker.ShardRegionTerminated(@ref));
            if (State.RegionProxies.Contains(@ref))
            {
                Log.Debug("{0}: ShardRegion proxy terminated: [{1}]", TypeName, @ref);
                Update(new ShardRegionProxyTerminated(@ref), evt =>
                {
                    State = State.Updated(evt);
                });
            }
        }

        private bool ShuttingDown(object message)
        {
            // ignore all
            return true;
        }

        private void SendHostShardMsg(ShardId shard, IActorRef region)
        {
            region.Tell(new HostShard(shard));
            var cancel = context.System.Scheduler.ScheduleTellOnceCancelable(Settings.TuningParameters.ShardStartTimeout, context.Self, new ResendShardHost(shard, region), ActorRefs.NoSender);
            unAckedHostShards = unAckedHostShards.SetItem(shard, cancel);
        }

        internal void AllocateShardHomesForRememberEntities()
        {
            if (Settings.RememberEntities && State.UnallocatedShards.Count > 0)
            {
                foreach (var shard in State.UnallocatedShards)
                {
                    context.Self.Tell(new GetShardHome(shard), ignoreRef);
                }
            }
        }

        private void ContinueGetShardHome(ShardId shard, IActorRef region, IActorRef getShardHomeSender)
        {
            if (rebalanceInProgress.ContainsKey(shard))
            {
                DeferGetShardHomeRequest(shard, getShardHomeSender);
            }
            else
            {
                if (State.Shards.TryGetValue(shard, out var @ref))
                {
                    getShardHomeSender.Tell(new ShardHome(shard, @ref));
                }
                else
                {
                    if (State.Regions.ContainsKey(region) && !gracefulShutdownInProgress.Contains(region) && !regionTerminationInProgress.Contains(region))
                    {
                        Update(new ShardHomeAllocated(shard, region), evt =>
                        {
                            State = State.Updated(evt);
                            Log.Debug("{0}: Shard [{1}] allocated at [{2}]",
                              TypeName,
                              evt.Shard,
                              evt.Region);

                            SendHostShardMsg(evt.Shard, evt.Region);
                            getShardHomeSender.Tell(new ShardHome(evt.Shard, evt.Region));
                        });
                    }
                    else
                    {
                        if (VerboseDebug)
                            Log.Debug("{0}: Allocated region [{1}] for shard [{2}] is not (any longer) one of the registered regions: {3}",
                                TypeName,
                                region,
                                shard,
                                State);
                        else
                            Log.Debug("{0}: Allocated region [{1}] for shard [{2}] is not (any longer) one of the registered regions.",
                              TypeName,
                              region,
                              shard);
                    }
                }
            }
        }

        /// <summary>
        /// Start a RebalanceWorker to manage the shard rebalance.
        /// Does nothing if the shard is already in the process of being rebalanced.
        /// </summary>
        /// <param name="shard"></param>
        /// <param name="from"></param>
        /// <param name="handOffTimeout"></param>
        /// <param name="isRebalance"></param>
        private void StartShardRebalanceIfNeeded(
            ShardId shard,
            IActorRef from,
            TimeSpan handOffTimeout,
            bool isRebalance)
        {
            if (!rebalanceInProgress.ContainsKey(shard))
            {
                rebalanceInProgress = rebalanceInProgress.SetItem(shard, ImmutableHashSet<IActorRef>.Empty);
                rebalanceWorkers = rebalanceWorkers.Add(context.ActorOf(
                    RebalanceWorker.Props(
                        TypeName,
                        shard,
                        from,
                        handOffTimeout,
                        State.Regions.Keys.Union(State.RegionProxies),
                        isRebalance: isRebalance)
                    .WithDispatcher(context.Props.Dispatcher)));
            }
        }

        private void ContinueRebalance(IImmutableSet<ShardId> shards)
        {
            if (Log.IsInfoEnabled && (shards.Count > 0 || rebalanceInProgress.Count > 0))
            {
                Log.Info("{0}: Starting rebalance for shards [{1}]. Current shards rebalancing: [{2}]",
                    TypeName,
                    string.Join(", ", shards),
                    string.Join(", ", rebalanceInProgress.Keys));
            }
            foreach (var shard in shards)
            {
                // optimisation: check if not already in progress before fetching region
                if (!rebalanceInProgress.ContainsKey(shard))
                {
                    if (State.Shards.TryGetValue(shard, out var region))
                    {
                        Log.Debug("{0}: Rebalance shard [{1}] from [{2}]", TypeName, shard, region);
                        StartShardRebalanceIfNeeded(shard, region, Settings.TuningParameters.HandOffTimeout, isRebalance: true);
                    }
                    else
                    {
                        Log.Debug("{0}: Rebalance of non-existing shard [{1}] is ignored", TypeName, shard);
                    }
                }
            }
        }

        private void ShutdownShards(IActorRef shuttingDownRegion, IImmutableSet<ShardId> shards)
        {
            if (Log.IsInfoEnabled && shards.Count > 0)
            {
                Log.Info("{0}: Starting shutting down shards [{1}] due to region shutting down.",
                    TypeName, string.Join(", ", shards));
            }
            foreach (var shard in shards)
            {
                StartShardRebalanceIfNeeded(shard, shuttingDownRegion, Settings.TuningParameters.HandOffTimeout, isRebalance: false);
            }
        }
    }
}
