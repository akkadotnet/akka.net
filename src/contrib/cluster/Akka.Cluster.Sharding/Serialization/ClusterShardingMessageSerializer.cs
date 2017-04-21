//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Google.Protobuf;
using ActorRefMessage = Akka.Remote.Serialization.Proto.Msg.ActorRefData;

namespace Akka.Cluster.Sharding.Serialization
{
    /// <summary>
    /// TBD
    /// </summary>
    public class ClusterShardingMessageSerializer : SerializerWithStringManifest
    {
        #region manifests

        private const string CoordinatorStateManifest = "AA";
        private const string ShardRegionRegisteredManifest = "AB";
        private const string ShardRegionProxyRegisteredManifest = "AC";
        private const string ShardRegionTerminatedManifest = "AD";
        private const string ShardRegionProxyTerminatedManifest = "AE";
        private const string ShardHomeAllocatedManifest = "AF";
        private const string ShardHomeDeallocatedManifest = "AG";

        private const string RegisterManifest = "BA";
        private const string RegisterProxyManifest = "BB";
        private const string RegisterAckManifest = "BC";
        private const string GetShardHomeManifest = "BD";
        private const string ShardHomeManifest = "BE";
        private const string HostShardManifest = "BF";
        private const string ShardStartedManifest = "BG";
        private const string BeginHandOffManifest = "BH";
        private const string BeginHandOffAckManifest = "BI";
        private const string HandOffManifest = "BJ";
        private const string ShardStoppedManifest = "BK";
        private const string GracefulShutdownReqManifest = "BL";

        private const string EntityStateManifest = "CA";
        private const string EntityStartedManifest = "CB";
        private const string EntityStoppedManifest = "CD";

        private const string GetShardStatsManifest = "DA";
        private const string ShardStatsManifest = "DB";

        #endregion

        private readonly Dictionary<string, Func<byte[], object>> _fromBinaryMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterShardingMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public ClusterShardingMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _fromBinaryMap = new Dictionary<string, Func<byte[], object>>
            {
                {EntityStateManifest, EntityStateFromBinary},
                {EntityStartedManifest, EntityStartedFromBinary},
                {EntityStoppedManifest, EntityStoppedFromBinary},

                {CoordinatorStateManifest, CoordinatorStateFromBinary},
                {ShardRegionRegisteredManifest, bytes => new PersistentShardCoordinator.ShardRegionRegistered(ActorRefMessageFromBinary(bytes)) },
                {ShardRegionProxyRegisteredManifest, bytes => new PersistentShardCoordinator.ShardRegionProxyRegistered(ActorRefMessageFromBinary(bytes)) },
                {ShardRegionTerminatedManifest, bytes => new PersistentShardCoordinator.ShardRegionTerminated(ActorRefMessageFromBinary(bytes)) },
                {ShardRegionProxyTerminatedManifest, bytes => new PersistentShardCoordinator.ShardRegionProxyTerminated(ActorRefMessageFromBinary(bytes)) },
                {ShardHomeAllocatedManifest, ShardHomeAllocatedFromBinary},
                {ShardHomeDeallocatedManifest, bytes => new PersistentShardCoordinator.ShardHomeDeallocated(ShardIdMessageFromBinary(bytes)) },

                {RegisterManifest, bytes => new PersistentShardCoordinator.Register(ActorRefMessageFromBinary(bytes)) },
                {RegisterProxyManifest, bytes => new PersistentShardCoordinator.RegisterProxy(ActorRefMessageFromBinary(bytes)) },
                {RegisterAckManifest, bytes => new PersistentShardCoordinator.RegisterAck(ActorRefMessageFromBinary(bytes)) },
                {GetShardHomeManifest, bytes => new PersistentShardCoordinator.GetShardHome(ShardIdMessageFromBinary(bytes)) },
                {ShardHomeManifest, ShardHomeFromBinary},
                {HostShardManifest, bytes => new PersistentShardCoordinator.HostShard(ShardIdMessageFromBinary(bytes)) },
                {ShardStartedManifest, bytes => new PersistentShardCoordinator.ShardStarted(ShardIdMessageFromBinary(bytes)) },
                {BeginHandOffManifest, bytes => new PersistentShardCoordinator.BeginHandOff(ShardIdMessageFromBinary(bytes)) },
                {BeginHandOffAckManifest, bytes => new PersistentShardCoordinator.BeginHandOffAck(ShardIdMessageFromBinary(bytes)) },
                {HandOffManifest, bytes => new PersistentShardCoordinator.HandOff(ShardIdMessageFromBinary(bytes)) },
                {ShardStoppedManifest, bytes => new PersistentShardCoordinator.ShardStopped(ShardIdMessageFromBinary(bytes)) },
                {GracefulShutdownReqManifest, bytes => new PersistentShardCoordinator.GracefulShutdownRequest(ActorRefMessageFromBinary(bytes)) },

                {GetShardStatsManifest, bytes => Shard.GetShardStats.Instance },
                {ShardStatsManifest, ShardStatsFromBinary}
            };
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="obj"/> is of an unknown type.
        /// </exception>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj is PersistentShardCoordinator.State) return CoordinatorStateToProto((PersistentShardCoordinator.State)obj).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardRegionRegistered) return ActorRefMessageToProto(((PersistentShardCoordinator.ShardRegionRegistered)obj).Region).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardRegionProxyRegistered) return ActorRefMessageToProto(((PersistentShardCoordinator.ShardRegionProxyRegistered)obj).RegionProxy).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardRegionTerminated) return ActorRefMessageToProto(((PersistentShardCoordinator.ShardRegionTerminated)obj).Region).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardRegionProxyTerminated) return ActorRefMessageToProto(((PersistentShardCoordinator.ShardRegionProxyTerminated)obj).RegionProxy).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardHomeAllocated) return ShardHomeAllocatedToProto((PersistentShardCoordinator.ShardHomeAllocated)obj).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardHomeDeallocated) return ShardIdMessageToProto(((PersistentShardCoordinator.ShardHomeDeallocated)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.Register) return ActorRefMessageToProto(((PersistentShardCoordinator.Register)obj).ShardRegion).ToByteArray();
            if (obj is PersistentShardCoordinator.RegisterProxy) return ActorRefMessageToProto(((PersistentShardCoordinator.RegisterProxy)obj).ShardRegionProxy).ToByteArray();
            if (obj is PersistentShardCoordinator.RegisterAck) return ActorRefMessageToProto(((PersistentShardCoordinator.RegisterAck)obj).Coordinator).ToByteArray();
            if (obj is PersistentShardCoordinator.GetShardHome) return ShardIdMessageToProto(((PersistentShardCoordinator.GetShardHome)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardHome) return ShardHomeToProto((PersistentShardCoordinator.ShardHome)obj).ToByteArray();
            if (obj is PersistentShardCoordinator.HostShard) return ShardIdMessageToProto(((PersistentShardCoordinator.HostShard)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardStarted) return ShardIdMessageToProto(((PersistentShardCoordinator.ShardStarted)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.BeginHandOff) return ShardIdMessageToProto(((PersistentShardCoordinator.BeginHandOff)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.BeginHandOffAck) return ShardIdMessageToProto(((PersistentShardCoordinator.BeginHandOffAck)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.HandOff) return ShardIdMessageToProto(((PersistentShardCoordinator.HandOff)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.ShardStopped) return ShardIdMessageToProto(((PersistentShardCoordinator.ShardStopped)obj).Shard).ToByteArray();
            if (obj is PersistentShardCoordinator.GracefulShutdownRequest) return ActorRefMessageToProto(((PersistentShardCoordinator.GracefulShutdownRequest)obj).ShardRegion).ToByteArray();
            if (obj is Shard.ShardState) return EntityStateToProto((Shard.ShardState)obj).ToByteArray();
            if (obj is Shard.EntityStarted) return EntityStartedToProto((Shard.EntityStarted)obj).ToByteArray();
            if (obj is Shard.EntityStopped) return EntityStoppedToProto((Shard.EntityStopped)obj).ToByteArray();
            if (obj is Shard.GetShardStats) return new byte[0];
            if (obj is Shard.ShardStats) return ShardStatsToProto((Shard.ShardStats)obj).ToByteArray();

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{this.GetType()}]");
        }

        /// <summary>
        /// Deserializes a byte array into an object using an optional <paramref name="manifest" /> (type hint).
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="manifest">The type hint used to deserialize the object contained in the array.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="bytes"/>cannot be deserialized using the specified <paramref name="manifest"/>.
        /// </exception>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            Func<byte[], object> factory;
            if (_fromBinaryMap.TryGetValue(manifest, out factory))
            {
                return factory(bytes);
            }

            throw new ArgumentException($"Unimplemented deserialization of message with manifest [{manifest}] in [{this.GetType()}]");
        }

        /// <summary>
        /// Returns the manifest (type hint) that will be provided in the <see cref="FromBinary(System.Byte[],System.String)" /> method.
        /// <note>
        /// This method returns <see cref="String.Empty" /> if a manifest is not needed.
        /// </note>
        /// </summary>
        /// <param name="o">The object for which the manifest is needed.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="o"/> does not have an associated manifest.
        /// </exception>
        /// <returns>The manifest needed for the deserialization of the specified <paramref name="o" />.</returns>
        public override string Manifest(object o)
        {
            if (o is Shard.ShardState) return EntityStateManifest;
            if (o is Shard.EntityStarted) return EntityStartedManifest;
            if (o is Shard.EntityStopped) return EntityStoppedManifest;
            if (o is PersistentShardCoordinator.State) return CoordinatorStateManifest;
            if (o is PersistentShardCoordinator.ShardRegionRegistered) return ShardRegionRegisteredManifest;
            if (o is PersistentShardCoordinator.ShardRegionProxyRegistered) return ShardRegionProxyRegisteredManifest;
            if (o is PersistentShardCoordinator.ShardRegionTerminated) return ShardRegionTerminatedManifest;
            if (o is PersistentShardCoordinator.ShardRegionProxyTerminated) return ShardRegionProxyTerminatedManifest;
            if (o is PersistentShardCoordinator.ShardHomeAllocated) return ShardHomeAllocatedManifest;
            if (o is PersistentShardCoordinator.ShardHomeDeallocated) return ShardHomeDeallocatedManifest;
            if (o is PersistentShardCoordinator.Register) return RegisterManifest;
            if (o is PersistentShardCoordinator.RegisterProxy) return RegisterProxyManifest;
            if (o is PersistentShardCoordinator.RegisterAck) return RegisterAckManifest;
            if (o is PersistentShardCoordinator.GetShardHome) return GetShardHomeManifest;
            if (o is PersistentShardCoordinator.ShardHome) return ShardHomeManifest;
            if (o is PersistentShardCoordinator.HostShard) return HostShardManifest;
            if (o is PersistentShardCoordinator.ShardStarted) return ShardStartedManifest;
            if (o is PersistentShardCoordinator.BeginHandOff) return BeginHandOffManifest;
            if (o is PersistentShardCoordinator.BeginHandOffAck) return BeginHandOffAckManifest;
            if (o is PersistentShardCoordinator.HandOff) return HandOffManifest;
            if (o is PersistentShardCoordinator.ShardStopped) return ShardStoppedManifest;
            if (o is PersistentShardCoordinator.GracefulShutdownRequest) return GracefulShutdownReqManifest;
            if (o is Shard.GetShardStats) return GetShardStatsManifest;
            if (o is Shard.ShardStats) return ShardStatsManifest;

            throw new ArgumentException($"Can't serialize object of type [{o.GetType()}] in [{this.GetType()}]");
        }

        //
        // ShardStats
        //
        private static Proto.Msg.ShardStats ShardStatsToProto(Shard.ShardStats shardStats)
        {
            var message = new Proto.Msg.ShardStats();
            message.Shard = shardStats.ShardId;
            message.EntityCount = shardStats.EntityCount;
            return message;
        }

        private static Shard.ShardStats ShardStatsFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.ShardStats.Parser.ParseFrom(bytes);
            return new Shard.ShardStats(message.Shard, message.EntityCount);
        }

        //
        // EntityStarted
        //
        private static Proto.Msg.EntityStarted EntityStartedToProto(Shard.EntityStarted entityStarted)
        {
            var message = new Proto.Msg.EntityStarted();
            message.EntityId = entityStarted.EntityId;
            return message;
        }

        private static Shard.EntityStarted EntityStartedFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.EntityStarted.Parser.ParseFrom(bytes);
            return new Shard.EntityStarted(message.EntityId);
        }

        //
        // EntityStopped
        //
        private static Proto.Msg.EntityStopped EntityStoppedToProto(Shard.EntityStopped entityStopped)
        {
            var message = new Proto.Msg.EntityStopped();
            message.EntityId = entityStopped.EntityId;
            return message;
        }

        private static Shard.EntityStopped EntityStoppedFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.EntityStopped.Parser.ParseFrom(bytes);
            return new Shard.EntityStopped(message.EntityId);
        }

        //
        // PersistentShardCoordinator.State
        //
        private static Proto.Msg.CoordinatorState CoordinatorStateToProto(PersistentShardCoordinator.State state)
        {
            var message = new Proto.Msg.CoordinatorState();
            message.Shards.AddRange(state.Shards.Select(entry =>
            {
                var coordinatorState = new Proto.Msg.CoordinatorState.Types.ShardEntry();
                coordinatorState.ShardId = entry.Key;
                coordinatorState.RegionRef = Akka.Serialization.Serialization.SerializedActorPath(entry.Value);
                return coordinatorState;
            }));

            message.Regions.AddRange(state.Regions.Keys.Select(Akka.Serialization.Serialization.SerializedActorPath));
            message.RegionProxies.AddRange(state.RegionProxies.Select(Akka.Serialization.Serialization.SerializedActorPath));
            message.UnallocatedShards.AddRange(state.UnallocatedShards);

            return message;
        }

        private PersistentShardCoordinator.State CoordinatorStateFromBinary(byte[] bytes)
        {
            var state = Proto.Msg.CoordinatorState.Parser.ParseFrom(bytes);
            var shards = ImmutableDictionary.CreateRange(state.Shards.Select(entry => new KeyValuePair<string, IActorRef>(entry.ShardId, ResolveActorRef(entry.RegionRef))));
            var regionsZero = ImmutableDictionary.CreateRange(state.Regions.Select(region => new KeyValuePair<IActorRef, IImmutableList<string>>(ResolveActorRef(region), ImmutableList<string>.Empty)));
            var regions = shards.Aggregate(regionsZero, (acc, entry) => acc.SetItem(entry.Value, acc[entry.Value].Add(entry.Key)));
            var proxies = state.RegionProxies.Select(ResolveActorRef).ToImmutableHashSet();
            var unallocatedShards = state.UnallocatedShards.ToImmutableHashSet();

            return new PersistentShardCoordinator.State(
                shards: shards,
                regions: regions,
                regionProxies: proxies,
                unallocatedShards: unallocatedShards);
        }

        //
        // PersistentShardCoordinator.ShardHomeAllocated
        //
        private static Proto.Msg.ShardHomeAllocated ShardHomeAllocatedToProto(PersistentShardCoordinator.ShardHomeAllocated shardHomeAllocated)
        {
            var message = new Proto.Msg.ShardHomeAllocated();
            message.Shard = shardHomeAllocated.Shard;
            message.Region = Akka.Serialization.Serialization.SerializedActorPath(shardHomeAllocated.Region);
            return message;
        }

        private PersistentShardCoordinator.ShardHomeAllocated ShardHomeAllocatedFromBinary(byte[] bytes)
        {
            var msg = Proto.Msg.ShardHomeAllocated.Parser.ParseFrom(bytes);
            return new PersistentShardCoordinator.ShardHomeAllocated(msg.Shard, ResolveActorRef(msg.Region));
        }

        //
        // PersistentShardCoordinator.ShardHome
        //
        private static Proto.Msg.ShardHome ShardHomeToProto(PersistentShardCoordinator.ShardHome shardHome)
        {
            var message = new Proto.Msg.ShardHome();
            message.Shard = shardHome.Shard;
            message.Region = Akka.Serialization.Serialization.SerializedActorPath(shardHome.Ref);
            return message;
        }

        private PersistentShardCoordinator.ShardHome ShardHomeFromBinary(byte[] bytes)
        {
            var msg = Proto.Msg.ShardHome.Parser.ParseFrom(bytes);
            return new PersistentShardCoordinator.ShardHome(msg.Shard, ResolveActorRef(msg.Region));
        }

        //
        // ActorRefMessage
        //
        private ActorRefMessage ActorRefMessageToProto(IActorRef actorRef)
        {
            var message = new ActorRefMessage();
            message.Path = Akka.Serialization.Serialization.SerializedActorPath(actorRef);
            return message;
        }

        private IActorRef ActorRefMessageFromBinary(byte[] binary)
        {
            return ResolveActorRef(ActorRefMessage.Parser.ParseFrom(binary).Path);
        }

        //
        // Shard.ShardState
        //

        private Proto.Msg.EntityState EntityStateToProto(Shard.ShardState entityState)
        {
            var message = new Proto.Msg.EntityState();
            message.Entities.AddRange(entityState.Entries);
            return message;
        }

        private Shard.ShardState EntityStateFromBinary(byte[] bytes)
        {
            var msg = Proto.Msg.EntityState.Parser.ParseFrom(bytes);
            return new Shard.ShardState(msg.Entities.ToImmutableHashSet());
        }

        //
        // ShardIdMessage
        //
        private Proto.Msg.ShardIdMessage ShardIdMessageToProto(string shard)
        {
            var message = new Proto.Msg.ShardIdMessage();
            message.Shard = shard;
            return message;
        }

        private string ShardIdMessageFromBinary(byte[] bytes)
        {
            return Proto.Msg.ShardIdMessage.Parser.ParseFrom(bytes).Shard;
        }

        private IActorRef ResolveActorRef(string path)
        {
            return system.Provider.ResolveActorRef(path);
        }
    }
}