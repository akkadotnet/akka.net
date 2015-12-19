using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Google.ProtocolBuffers;

namespace Akka.Cluster.Sharding.Serialization
{
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

        public const int BufferSize = 1024 << 2;

        private readonly int _identifier;
        private ExtendedActorSystem _system;

        public ClusterShardingMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _system = system;
            _identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(this.GetType(), system);

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
                {ShardStatsManifest, bytes => ShardStatsFromBinary(bytes)}
            };
        }

        public override int Identifier { get { return _identifier; } }

        public override byte[] ToBinary(object obj)
        {
            if (obj is PersistentShardCoordinator.State) return Compress(CoordinatorStateToProto((PersistentShardCoordinator.State)obj));
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

            throw new ArgumentException(string.Format("Can't serialize object of type [{0}] in [{1}]", obj.GetType(), this.GetType()));
        }

        public override object FromBinary(byte[] binary, string manifest)
        {
            Func<byte[], object> factory;
            if (_fromBinaryMap.TryGetValue(manifest, out factory))
            {
                return factory(binary);
            }

            throw new ArgumentException(string.Format("Unimplemented deserialization of message with manifest [{0}] in [{1}]", manifest, this.GetType()));
        }

        public override string Manifest(object o)
        {
            if (o is EntityState) return EntityStateManifest;
            if (o is EntityStarted) return EntityStartedManifest;
            if (o is EntityStopped) return EntityStoppedManifest;
            if (o is PersistentShardCoordinator.State) return CoordinatorStateManifest;
            if (o is PersistentShardCoordinator.ShardRegionRegistered) return ShardRegionRegisteredManifest;
            if (o is PersistentShardCoordinator.ShardRegionProxyRegistered) return ShardRegionProxyRegisteredManifest;
            if (o is PersistentShardCoordinator.ShardRegionTerminated) return ShardRegionTerminatedManifest;
            if (o is PersistentShardCoordinator.ShardRegionProxyTerminated) return ShardRegionProxyTerminatedManifest;
            if (o is ShardHomeAllocated) return ShardHomeAllocatedManifest;
            if (o is PersistentShardCoordinator.ShardHomeDeallocated) return ShardHomeDeallocatedManifest;
            if (o is PersistentShardCoordinator.Register) return RegisterManifest;
            if (o is PersistentShardCoordinator.RegisterProxy) return RegisterProxyManifest;
            if (o is PersistentShardCoordinator.RegisterAck) return RegisterAckManifest;
            if (o is PersistentShardCoordinator.GetShardHome) return GetShardHomeManifest;
            if (o is ShardHome) return ShardHomeManifest;
            if (o is PersistentShardCoordinator.HostShard) return HostShardManifest;
            if (o is PersistentShardCoordinator.ShardStarted) return ShardStartedManifest;
            if (o is PersistentShardCoordinator.BeginHandOff) return BeginHandOffManifest;
            if (o is PersistentShardCoordinator.BeginHandOffAck) return BeginHandOffAckManifest;
            if (o is PersistentShardCoordinator.HandOff) return HandOffManifest;
            if (o is PersistentShardCoordinator.ShardStopped) return ShardStoppedManifest;
            if (o is PersistentShardCoordinator.GracefulShutdownRequest) return GracefulShutdownReqManifest;
            if (o is Shard.GetShardStats) return GetShardStatsManifest;
            if (o is Shard.ShardStats) return ShardStatsManifest;

            throw new ArgumentException(string.Format("Can't serialize object of type [{0}] in [{1}]", o.GetType(), this.GetType()));
        }

        private ShardStats ShardStatsToProto(Shard.ShardStats o)
        {
            return ShardStats.CreateBuilder().SetShard(o.ShardId).SetEntityCount(o.EntityCount).Build();
        }

        private EntityStopped EntityStoppedToProto(Shard.EntityStopped entityStopped)
        {
            return EntityStopped.CreateBuilder().SetEntityId(entityStopped.EntityId).Build();
        }

        private EntityStarted EntityStartedToProto(Shard.EntityStarted entityStarted)
        {
            return EntityStarted.CreateBuilder().SetEntityId(entityStarted.EntityId).Build();
        }

        private EntityState EntityStateToProto(Shard.ShardState entityState)
        {
            return EntityState.CreateBuilder().AddRangeEntities(entityState.Entries).Build();
        }

        private ShardHome ShardHomeToProto(PersistentShardCoordinator.ShardHome shardHome)
        {
            return ShardHome.CreateBuilder()
                .SetShard(shardHome.Shard)
                .SetRegion(Akka.Serialization.Serialization.SerializedActorPath(shardHome.Ref))
                .Build();
        }

        private ShardHomeAllocated ShardHomeAllocatedToProto(PersistentShardCoordinator.ShardHomeAllocated shardHomeAllocated)
        {
            return ShardHomeAllocated.CreateBuilder()
                .SetShard(shardHomeAllocated.Shard)
                .SetRegion(Akka.Serialization.Serialization.SerializedActorPath(shardHomeAllocated.Region))
                .Build();
        }

        private ShardIdMessage ShardIdMessageToProto(string shard)
        {
            return ShardIdMessage.CreateBuilder().SetShard(shard).Build();
        }

        private ActorRefMessage ActorRefMessageToProto(IActorRef actorRef)
        {
            return ActorRefMessage.CreateBuilder().SetRef(Akka.Serialization.Serialization.SerializedActorPath(actorRef)).Build();
        }

        private CoordinatorState CoordinatorStateToProto(PersistentShardCoordinator.State state)
        {
            var builder = CoordinatorState.CreateBuilder()
                .AddRangeShards(state.Shards.Select(entry => CoordinatorState.Types.ShardEntry.CreateBuilder()
                    .SetShardId(entry.Key)
                    .SetRegionRef(Akka.Serialization.Serialization.SerializedActorPath(entry.Value))
                    .Build()))
                .AddRangeRegions(state.Regions.Keys.Select(Akka.Serialization.Serialization.SerializedActorPath))
                .AddRangeRegionProxies(state.RegionProxies.Select(Akka.Serialization.Serialization.SerializedActorPath))
                .AddRangeUnallocatedShards(state.UnallocatedShards);

            return builder.Build();
        }

        private string ShardIdMessageFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                return ShardIdMessage.ParseFrom(stream).Shard;
            }
        }

        private IActorRef ActorRefMessageFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                return ResolveActorRef(ActorRefMessage.ParseFrom(stream).Ref);
            }
        }

        private object ShardStatsFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                return ResolveActorRef(ActorRefMessage.ParseFrom(stream).Ref);
            }
        }

        private object ShardHomeFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                var msg = ShardHome.ParseFrom(stream);
                return new PersistentShardCoordinator.ShardHome(msg.Shard, ResolveActorRef(msg.Region));
            }
        }

        private object ShardHomeAllocatedFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                var msg = ShardHomeAllocated.ParseFrom(stream);
                return new PersistentShardCoordinator.ShardHomeAllocated(msg.Shard, ResolveActorRef(msg.Region));
            }
        }

        private object EntityStoppedFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                var msg = EntityStopped.ParseFrom(stream);
                return new Shard.EntityStopped(msg.EntityId);
            }
        }

        private object EntityStartedFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                var msg = EntityStarted.ParseFrom(stream);
                return new Shard.EntityStarted(msg.EntityId);
            }
        }

        private object EntityStateFromBinary(byte[] binary)
        {
            using (var stream = new MemoryStream(binary, false))
            {
                var msg = EntityState.ParseFrom(stream);
                return new Shard.ShardState(ImmutableHashSet.CreateRange(msg.EntitiesList));
            }
        }

        private object CoordinatorStateFromBinary(byte[] binary)
        {
            using (var stream = Decompress(binary))
            {
                var state = CoordinatorState.ParseFrom(stream);
                var shards = ImmutableDictionary.CreateRange(state.ShardsList.Select(entry => new KeyValuePair<string, IActorRef>(entry.ShardId, ResolveActorRef(entry.RegionRef))));
                var regionsZero = ImmutableDictionary.CreateRange(state.RegionsList.Select(region => new KeyValuePair<IActorRef, IImmutableList<string>>(ResolveActorRef(region), ImmutableList<string>.Empty)));
                var regions = shards.Aggregate(regionsZero, (acc, entry) => acc.SetItem(entry.Value, acc[entry.Value].Add(entry.Key)));
                var proxies = state.RegionProxiesList.Select(ResolveActorRef).ToImmutableHashSet();
                var unallocatedShards = state.UnallocatedShardsList.ToImmutableHashSet();

                return new PersistentShardCoordinator.State(
                    shards: shards,
                    regions: regions,
                    regionProxies: proxies,
                    unallocatedShards: unallocatedShards);
            }
        }

        /// <summary>
        /// Compresses the protobuf message using GZIP compression
        /// </summary>
        private static byte[] Compress(IMessageLite message)
        {
            using (var bos = new MemoryStream(BufferSize))
            using (var gzipStream = new GZipStream(bos, CompressionMode.Compress))
            {
                message.WriteTo(gzipStream);
                gzipStream.Close();
                return bos.ToArray();
            }
        }

        /// <summary>
        /// Decompresses the protobuf message using GZIP compression
        /// </summary>
        private static Stream Decompress(byte[] bytes)
        {
            return new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
        }

        private IActorRef ResolveActorRef(string path)
        {
            return _system.Provider.ResolveActorRef(path);
        }
    }
}