//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Sharding.Serialization.Proto.Msg;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Serialization;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using ActorRefMessage = Akka.Remote.Serialization.Proto.Msg.ActorRefData;

namespace Akka.Cluster.Sharding.Serialization
{
    /// <summary>
    /// INTERNAL API: Protobuf serializer of Cluster.Sharding messages.
    /// </summary>
    public class ClusterShardingMessageSerializer : SerializerWithStringManifest
    {
        private static readonly byte[] Empty = new byte[0];

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

        private const string StartEntityManifest = "EA";
        private const string StartEntityAckManifest = "EB";

        private const string GetShardStatsManifest = "DA";
        private const string ShardStatsManifest = "DB";
        private const string GetShardRegionStatsManifest = "DC";
        private const string ShardRegionStatsManifest = "DD";

        private const string GetClusterShardingStatsManifest = "GS";
        private const string ClusterShardingStatsManifest = "CS";

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
                {EntityStateManifest, bytes => EntityStateFromBinary(bytes) },
                {EntityStartedManifest, bytes => EntityStartedFromBinary(bytes) },
                {EntityStoppedManifest, bytes => EntityStoppedFromBinary(bytes) },

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
                {ShardHomeManifest, bytes => ShardHomeFromBinary(bytes) },
                {HostShardManifest, bytes => new PersistentShardCoordinator.HostShard(ShardIdMessageFromBinary(bytes)) },
                {ShardStartedManifest, bytes => new PersistentShardCoordinator.ShardStarted(ShardIdMessageFromBinary(bytes)) },
                {BeginHandOffManifest, bytes => new PersistentShardCoordinator.BeginHandOff(ShardIdMessageFromBinary(bytes)) },
                {BeginHandOffAckManifest, bytes => new PersistentShardCoordinator.BeginHandOffAck(ShardIdMessageFromBinary(bytes)) },
                {HandOffManifest, bytes => new PersistentShardCoordinator.HandOff(ShardIdMessageFromBinary(bytes)) },
                {ShardStoppedManifest, bytes => new PersistentShardCoordinator.ShardStopped(ShardIdMessageFromBinary(bytes)) },
                {GracefulShutdownReqManifest, bytes => new PersistentShardCoordinator.GracefulShutdownRequest(ActorRefMessageFromBinary(bytes)) },

                {GetShardStatsManifest, bytes => Shard.GetShardStats.Instance },
                {ShardStatsManifest, bytes => ShardStatsFromBinary(bytes) },
                { GetShardRegionStatsManifest, bytes => GetShardRegionStats.Instance },
                { ShardRegionStatsManifest, bytes => ShardRegionStatsFromBinary(bytes) },
                { GetClusterShardingStatsManifest, bytes => GetClusterShardingStatsFromBinary(bytes) },
                { ClusterShardingStatsManifest, bytes => ClusterShardingStatsFromBinary(bytes) },

                {StartEntityManifest, bytes => StartEntityFromBinary(bytes) },
                {StartEntityAckManifest, bytes => StartEntityAckFromBinary(bytes) }
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
            switch (obj)
            {
                case PersistentShardCoordinator.State o: return CoordinatorStateToProto(o).ToByteArray();
                case PersistentShardCoordinator.ShardRegionRegistered o: return ActorRefMessageToProto(o.Region).ToByteArray();
                case PersistentShardCoordinator.ShardRegionProxyRegistered o: return ActorRefMessageToProto(o.RegionProxy).ToByteArray();
                case PersistentShardCoordinator.ShardRegionTerminated o: return ActorRefMessageToProto(o.Region).ToByteArray();
                case PersistentShardCoordinator.ShardRegionProxyTerminated o: return ActorRefMessageToProto(o.RegionProxy).ToByteArray();
                case PersistentShardCoordinator.ShardHomeAllocated o: return ShardHomeAllocatedToProto(o).ToByteArray();
                case PersistentShardCoordinator.ShardHomeDeallocated o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.Register o: return ActorRefMessageToProto(o.ShardRegion).ToByteArray();
                case PersistentShardCoordinator.RegisterProxy o: return ActorRefMessageToProto(o.ShardRegionProxy).ToByteArray();
                case PersistentShardCoordinator.RegisterAck o: return ActorRefMessageToProto(o.Coordinator).ToByteArray();
                case PersistentShardCoordinator.GetShardHome o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.ShardHome o: return ShardHomeToProto(o).ToByteArray();
                case PersistentShardCoordinator.HostShard o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.ShardStarted o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.BeginHandOff o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.BeginHandOffAck o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.HandOff o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.ShardStopped o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case PersistentShardCoordinator.GracefulShutdownRequest o: return ActorRefMessageToProto(o.ShardRegion).ToByteArray();
                case Shard.ShardState o: return EntityStateToProto(o).ToByteArray();
                case Shard.EntityStarted o: return EntityStartedToProto(o).ToByteArray();
                case Shard.EntityStopped o: return EntityStoppedToProto(o).ToByteArray();
                case ShardRegion.StartEntity o: return StartEntityToProto(o).ToByteArray();
                case ShardRegion.StartEntityAck o: return StartEntityAckToProto(o).ToByteArray();
                case Shard.GetShardStats o: return Empty;
                case Shard.ShardStats o: return ShardStatsToProto(o).ToByteArray();
                case GetShardRegionStats o: return Empty;
                case ShardRegionStats o: return ShardRegionStatsToProto(o).ToByteArray();
                case GetClusterShardingStats o: return GetClusterShardingStatsToProto(o).ToByteArray();
                case ClusterShardingStats o: return ClusterShardingStatsToProto(o).ToByteArray();
            }
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
            if (_fromBinaryMap.TryGetValue(manifest, out var factory))
                return factory(bytes);

            throw new SerializationException($"Unimplemented deserialization of message with manifest [{manifest}] in [{this.GetType()}]");
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
            switch (o)
            {
                case Shard.ShardState _: return EntityStateManifest;
                case Shard.EntityStarted _: return EntityStartedManifest;
                case Shard.EntityStopped _: return EntityStoppedManifest;
                case PersistentShardCoordinator.State _: return CoordinatorStateManifest;
                case PersistentShardCoordinator.ShardRegionRegistered _: return ShardRegionRegisteredManifest;
                case PersistentShardCoordinator.ShardRegionProxyRegistered _: return ShardRegionProxyRegisteredManifest;
                case PersistentShardCoordinator.ShardRegionTerminated _: return ShardRegionTerminatedManifest;
                case PersistentShardCoordinator.ShardRegionProxyTerminated _: return ShardRegionProxyTerminatedManifest;
                case PersistentShardCoordinator.ShardHomeAllocated _: return ShardHomeAllocatedManifest;
                case PersistentShardCoordinator.ShardHomeDeallocated _: return ShardHomeDeallocatedManifest;
                case PersistentShardCoordinator.Register _: return RegisterManifest;
                case PersistentShardCoordinator.RegisterProxy _: return RegisterProxyManifest;
                case PersistentShardCoordinator.RegisterAck _: return RegisterAckManifest;
                case PersistentShardCoordinator.GetShardHome _: return GetShardHomeManifest;
                case PersistentShardCoordinator.ShardHome _: return ShardHomeManifest;
                case PersistentShardCoordinator.HostShard _: return HostShardManifest;
                case PersistentShardCoordinator.ShardStarted _: return ShardStartedManifest;
                case PersistentShardCoordinator.BeginHandOff _: return BeginHandOffManifest;
                case PersistentShardCoordinator.BeginHandOffAck _: return BeginHandOffAckManifest;
                case PersistentShardCoordinator.HandOff _: return HandOffManifest;
                case PersistentShardCoordinator.ShardStopped _: return ShardStoppedManifest;
                case PersistentShardCoordinator.GracefulShutdownRequest _: return GracefulShutdownReqManifest;
                case ShardRegion.StartEntity _: return StartEntityManifest;
                case ShardRegion.StartEntityAck _: return StartEntityAckManifest;
                case Shard.GetShardStats _: return GetShardStatsManifest;
                case Shard.ShardStats _: return ShardStatsManifest;
                case GetShardRegionStats _: return GetShardRegionStatsManifest;
                case ShardRegionStats _: return ShardRegionStatsManifest;
                case GetClusterShardingStats _: return GetClusterShardingStatsManifest;
                case ClusterShardingStats _: return ClusterShardingStatsManifest;
            }
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
        // ShardRegion.StartEntity
        //
        private static Proto.Msg.StartEntity StartEntityToProto(ShardRegion.StartEntity startEntity)
        {
            var message = new Proto.Msg.StartEntity();
            message.EntityId = startEntity.EntityId;
            return message;
        }

        private static ShardRegion.StartEntity StartEntityFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.StartEntity.Parser.ParseFrom(bytes);
            return new ShardRegion.StartEntity(message.EntityId);
        }

        //
        // ShardRegion.StartEntityAck
        //
        private static Proto.Msg.StartEntityAck StartEntityAckToProto(ShardRegion.StartEntityAck startEntityAck)
        {
            var message = new Proto.Msg.StartEntityAck();
            message.EntityId = startEntityAck.EntityId;
            message.ShardId = startEntityAck.ShardId;
            return message;
        }

        private static ShardRegion.StartEntityAck StartEntityAckFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.StartEntityAck.Parser.ParseFrom(bytes);
            return new ShardRegion.StartEntityAck(message.EntityId, message.ShardId);
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

        private static Proto.Msg.EntityState EntityStateToProto(Shard.ShardState entityState)
        {
            var message = new Proto.Msg.EntityState();
            message.Entities.AddRange(entityState.Entries);
            return message;
        }

        private static Shard.ShardState EntityStateFromBinary(byte[] bytes)
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

        // ShardRegionStats
        private static Proto.Msg.ShardRegionStats ShardRegionStatsToProto(ShardRegionStats s)
        {
            var message = new Proto.Msg.ShardRegionStats();
            message.Stats.Add((IDictionary<string,int>)s.Stats);
            return message;
        }

        private static ShardRegionStats ShardRegionStatsFromBinary(byte[] b)
        {
            var p = Proto.Msg.ShardRegionStats.Parser.ParseFrom(b);
            return new ShardRegionStats(p.Stats.ToImmutableDictionary());
        }
        
        // GetClusterShardingStats
        private static Proto.Msg.GetClusterShardingStats GetClusterShardingStatsToProto(GetClusterShardingStats stats)
        {
            var p = new Proto.Msg.GetClusterShardingStats();
            p.Timeout = Duration.FromTimeSpan(stats.Timeout);
            return p;
        }

        private static GetClusterShardingStats GetClusterShardingStatsFromBinary(byte[] b)
        {
            var p = Proto.Msg.GetClusterShardingStats.Parser.ParseFrom(b);
            return new GetClusterShardingStats(p.Timeout.ToTimeSpan());
        }
        
        // ClusterShardingStats
        private static Proto.Msg.ClusterShardingStats ClusterShardingStatsToProto(ClusterShardingStats stats)
        {
            var p = new Proto.Msg.ClusterShardingStats();
            foreach (var s in stats.Regions)
            {
                p.Regions.Add(new ShardRegionWithAddress() { NodeAddress = AddressToProto(s.Key), Stats = ShardRegionStatsToProto(s.Value)});
            }

            return p;
        }

        private static ClusterShardingStats ClusterShardingStatsFromBinary(byte[] b)
        {
            var p = Proto.Msg.ClusterShardingStats.Parser.ParseFrom(b);
            var dict = new Dictionary<Address, ShardRegionStats>();
            foreach (var s in p.Regions)
            {
                dict[AddressFrom(s.NodeAddress)] = new ShardRegionStats(s.Stats.Stats.ToImmutableDictionary());
            }
            return new ClusterShardingStats(dict.ToImmutableDictionary());
        }
        
        private static AddressData AddressToProto(Address address)
        {
            var message = new AddressData();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }
        
        private static Address AddressFrom(AddressData addressProto)
        {
            return new Address(
                addressProto.Protocol,
                addressProto.System,
                addressProto.Hostname,
                addressProto.Port == 0 ? null : (int?)addressProto.Port);
        }

        private static string ShardIdMessageFromBinary(byte[] bytes)
        {
            return Proto.Msg.ShardIdMessage.Parser.ParseFrom(bytes).Shard;
        }

        private IActorRef ResolveActorRef(string path)
        {
            return system.Provider.ResolveActorRef(path);
        }
    }
}
