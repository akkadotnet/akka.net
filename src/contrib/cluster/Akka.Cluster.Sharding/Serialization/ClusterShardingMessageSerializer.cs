//-----------------------------------------------------------------------
// <copyright file="ClusterShardingMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Sharding.Internal;
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
        private const string EntitiesStartedManifest = "CE";
        private const string EntitiesStoppedManifest = "CF";

        private const string StartEntityManifest = "EA";
        private const string StartEntityAckManifest = "EB";

        private const string GetShardStatsManifest = "DA";
        private const string ShardStatsManifest = "DB";
        private const string GetShardRegionStatsManifest = "DC";
        private const string ShardRegionStatsManifest = "DD";

        private const string GetClusterShardingStatsManifest = "GS"; //"DE" in akka
        private const string ClusterShardingStatsManifest = "CS"; //"DF" in akka
        private const string GetCurrentRegionsManifest = "DG";
        private const string CurrentRegionsManifest = "DH";

        private const string GetCurrentShardStateManifest = "FA";
        private const string CurrentShardStateManifest = "FB";
        private const string GetShardRegionStateManifest = "FC";
        private const string ShardStateManifest = "FD";
        private const string CurrentShardRegionStateManifest = "FE";

        private const string EventSourcedRememberShardsMigrationMarkerManifest = "SM";
        private const string EventSourcedRememberShardsState = "SS";

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
                { EntityStateManifest, bytes => EntityStateFromBinary(bytes) },
                { EntityStartedManifest, bytes => EntityStartedFromBinary(bytes) },
                { EntitiesStartedManifest, bytes => EntitiesStartedFromBinary(bytes) },
                { EntityStoppedManifest, bytes => EntityStoppedFromBinary(bytes) },
                { EntitiesStoppedManifest, bytes => EntitiesStoppedFromBinary(bytes) },

                { CoordinatorStateManifest, CoordinatorStateFromBinary},
                { ShardRegionRegisteredManifest, bytes => new ShardCoordinator.ShardRegionRegistered(ActorRefMessageFromBinary(bytes)) },
                { ShardRegionProxyRegisteredManifest, bytes => new ShardCoordinator.ShardRegionProxyRegistered(ActorRefMessageFromBinary(bytes)) },
                { ShardRegionTerminatedManifest, bytes => new ShardCoordinator.ShardRegionTerminated(ActorRefMessageFromBinary(bytes)) },
                { ShardRegionProxyTerminatedManifest, bytes => new ShardCoordinator.ShardRegionProxyTerminated(ActorRefMessageFromBinary(bytes)) },
                { ShardHomeAllocatedManifest, ShardHomeAllocatedFromBinary},
                { ShardHomeDeallocatedManifest, bytes => new ShardCoordinator.ShardHomeDeallocated(ShardIdMessageFromBinary(bytes)) },

                { RegisterManifest, bytes => new ShardCoordinator.Register(ActorRefMessageFromBinary(bytes)) },
                { RegisterProxyManifest, bytes => new ShardCoordinator.RegisterProxy(ActorRefMessageFromBinary(bytes)) },
                { RegisterAckManifest, bytes => new ShardCoordinator.RegisterAck(ActorRefMessageFromBinary(bytes)) },
                { GetShardHomeManifest, bytes => new ShardCoordinator.GetShardHome(ShardIdMessageFromBinary(bytes)) },
                { ShardHomeManifest, bytes => ShardHomeFromBinary(bytes) },
                { HostShardManifest, bytes => new ShardCoordinator.HostShard(ShardIdMessageFromBinary(bytes)) },
                { ShardStartedManifest, bytes => new ShardCoordinator.ShardStarted(ShardIdMessageFromBinary(bytes)) },
                { BeginHandOffManifest, bytes => new ShardCoordinator.BeginHandOff(ShardIdMessageFromBinary(bytes)) },
                { BeginHandOffAckManifest, bytes => new ShardCoordinator.BeginHandOffAck(ShardIdMessageFromBinary(bytes)) },
                { HandOffManifest, bytes => new ShardCoordinator.HandOff(ShardIdMessageFromBinary(bytes)) },
                { ShardStoppedManifest, bytes => new ShardCoordinator.ShardStopped(ShardIdMessageFromBinary(bytes)) },
                { GracefulShutdownReqManifest, bytes => new ShardCoordinator.GracefulShutdownRequest(ActorRefMessageFromBinary(bytes)) },

                { GetShardStatsManifest, bytes => Shard.GetShardStats.Instance },
                { ShardStatsManifest, bytes => ShardStatsFromBinary(bytes) },
                { GetShardRegionStatsManifest, bytes => GetShardRegionStats.Instance },
                { ShardRegionStatsManifest, bytes => ShardRegionStatsFromBinary(bytes) },

                { GetClusterShardingStatsManifest, bytes => GetClusterShardingStatsFromBinary(bytes) },
                { ClusterShardingStatsManifest, bytes => ClusterShardingStatsFromBinary(bytes) },

                { GetCurrentRegionsManifest, bytes => GetCurrentRegions.Instance },
                { CurrentRegionsManifest, bytes => CurrentRegionsFromBinary(bytes) },

                { StartEntityManifest, bytes => StartEntityFromBinary(bytes) },
                { StartEntityAckManifest, bytes => StartEntityAckFromBinary(bytes) },

                //{ GetCurrentShardStateManifest, bytes => GetCurrentShardState },
                //{ CurrentShardStateManifest, bytes => CurrentShardStateFromBinary(bytes) },
                { GetShardRegionStateManifest, bytes => GetShardRegionState.Instance },
                { ShardStateManifest, bytes => ShardStateFromBinary(bytes) },
                { CurrentShardRegionStateManifest, bytes => CurrentShardRegionStateFromBinary(bytes) },


                { EventSourcedRememberShardsMigrationMarkerManifest, bytes => EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker.Instance},
                { EventSourcedRememberShardsState, bytes => RememberShardsStateFromBinary(bytes) }
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
                case ShardCoordinator.CoordinatorState o: return CoordinatorStateToProto(o).ToByteArray();
                case ShardCoordinator.ShardRegionRegistered o: return ActorRefMessageToProto(o.Region).ToByteArray();
                case ShardCoordinator.ShardRegionProxyRegistered o: return ActorRefMessageToProto(o.RegionProxy).ToByteArray();
                case ShardCoordinator.ShardRegionTerminated o: return ActorRefMessageToProto(o.Region).ToByteArray();
                case ShardCoordinator.ShardRegionProxyTerminated o: return ActorRefMessageToProto(o.RegionProxy).ToByteArray();
                case ShardCoordinator.ShardHomeAllocated o: return ShardHomeAllocatedToProto(o).ToByteArray();
                case ShardCoordinator.ShardHomeDeallocated o: return ShardIdMessageToProto(o.Shard).ToByteArray();

                case ShardCoordinator.Register o: return ActorRefMessageToProto(o.ShardRegion).ToByteArray();
                case ShardCoordinator.RegisterProxy o: return ActorRefMessageToProto(o.ShardRegionProxy).ToByteArray();
                case ShardCoordinator.RegisterAck o: return ActorRefMessageToProto(o.Coordinator).ToByteArray();
                case ShardCoordinator.GetShardHome o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.ShardHome o: return ShardHomeToProto(o).ToByteArray();
                case ShardCoordinator.HostShard o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.ShardStarted o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.BeginHandOff o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.BeginHandOffAck o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.HandOff o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.ShardStopped o: return ShardIdMessageToProto(o.Shard).ToByteArray();
                case ShardCoordinator.GracefulShutdownRequest o: return ActorRefMessageToProto(o.ShardRegion).ToByteArray();

                case EventSourcedRememberEntitiesShardStore.State o: return EntityStateToProto(o).ToByteArray();
                case EventSourcedRememberEntitiesShardStore.EntitiesStarted o: return EntitiesStartedToProto(o).ToByteArray();
                case EventSourcedRememberEntitiesShardStore.EntitiesStopped o: return EntitiesStoppedToProto(o).ToByteArray();

                case ShardRegion.StartEntity o: return StartEntityToProto(o).ToByteArray();
                case ShardRegion.StartEntityAck o: return StartEntityAckToProto(o).ToByteArray();

                case Shard.GetShardStats o: return Empty;
                case Shard.ShardStats o: return ShardStatsToProto(o).ToByteArray();
                case GetShardRegionStats o: return Empty;
                case ShardRegionStats o: return ShardRegionStatsToProto(o).ToByteArray();
                case GetClusterShardingStats o: return GetClusterShardingStatsToProto(o).ToByteArray();
                case ClusterShardingStats o: return ClusterShardingStatsToProto(o).ToByteArray();
                case GetCurrentRegions o: return Empty;
                case CurrentRegions o: return CurrentRegionsToProto(o).ToByteArray();

                //case GetCurrentShardState o: return Empty;
                //case CurrentShardState o: return CurrentShardStateToProto(o).ToByteArray();
                case GetShardRegionState o: return Empty;
                case ShardState o: return ShardStateToProto(o).ToByteArray();
                case CurrentShardRegionState o: return CurrentShardRegionStateToProto(o).ToByteArray();

                case EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker _: return Empty;
                case EventSourcedRememberEntitiesCoordinatorStore.State o: return RememberShardsStateToProto(o).ToByteArray();
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
                case EventSourcedRememberEntitiesShardStore.State _: return EntityStateManifest;
                case EventSourcedRememberEntitiesShardStore.EntitiesStarted _: return EntitiesStartedManifest;
                case EventSourcedRememberEntitiesShardStore.EntitiesStopped _: return EntitiesStoppedManifest;

                case ShardCoordinator.CoordinatorState _: return CoordinatorStateManifest;
                case ShardCoordinator.ShardRegionRegistered _: return ShardRegionRegisteredManifest;
                case ShardCoordinator.ShardRegionProxyRegistered _: return ShardRegionProxyRegisteredManifest;
                case ShardCoordinator.ShardRegionTerminated _: return ShardRegionTerminatedManifest;
                case ShardCoordinator.ShardRegionProxyTerminated _: return ShardRegionProxyTerminatedManifest;
                case ShardCoordinator.ShardHomeAllocated _: return ShardHomeAllocatedManifest;
                case ShardCoordinator.ShardHomeDeallocated _: return ShardHomeDeallocatedManifest;

                case ShardCoordinator.Register _: return RegisterManifest;
                case ShardCoordinator.RegisterProxy _: return RegisterProxyManifest;
                case ShardCoordinator.RegisterAck _: return RegisterAckManifest;
                case ShardCoordinator.GetShardHome _: return GetShardHomeManifest;
                case ShardCoordinator.ShardHome _: return ShardHomeManifest;
                case ShardCoordinator.HostShard _: return HostShardManifest;
                case ShardCoordinator.ShardStarted _: return ShardStartedManifest;
                case ShardCoordinator.BeginHandOff _: return BeginHandOffManifest;
                case ShardCoordinator.BeginHandOffAck _: return BeginHandOffAckManifest;
                case ShardCoordinator.HandOff _: return HandOffManifest;
                case ShardCoordinator.ShardStopped _: return ShardStoppedManifest;
                case ShardCoordinator.GracefulShutdownRequest _: return GracefulShutdownReqManifest;

                case ShardRegion.StartEntity _: return StartEntityManifest;
                case ShardRegion.StartEntityAck _: return StartEntityAckManifest;

                case Shard.GetShardStats _: return GetShardStatsManifest;
                case Shard.ShardStats _: return ShardStatsManifest;
                case GetShardRegionStats _: return GetShardRegionStatsManifest;
                case ShardRegionStats _: return ShardRegionStatsManifest;
                case GetClusterShardingStats _: return GetClusterShardingStatsManifest;
                case ClusterShardingStats _: return ClusterShardingStatsManifest;
                case GetCurrentRegions _: return GetCurrentRegionsManifest;
                case CurrentRegions _: return CurrentRegionsManifest;

                //case GetCurrentShardState _: return GetCurrentShardStateManifest;
                //case CurrentShardState _: return CurrentShardStateManifest;
                case GetShardRegionState _: return GetShardRegionStateManifest;
                case ShardState _: return ShardStateManifest;
                case CurrentShardRegionState _: return CurrentShardRegionStateManifest;

                case EventSourcedRememberEntitiesCoordinatorStore.MigrationMarker _: return EventSourcedRememberShardsMigrationMarkerManifest;
                case EventSourcedRememberEntitiesCoordinatorStore.State _: return EventSourcedRememberShardsState;
            }
            throw new ArgumentException($"Can't serialize object of type [{o.GetType()}] in [{this.GetType()}]");
        }

        //
        // EventSourcedRememberEntitiesCoordinatorStore.State
        //
        private static Proto.Msg.RememberedShardState RememberShardsStateToProto(EventSourcedRememberEntitiesCoordinatorStore.State state)
        {
            var message = new Proto.Msg.RememberedShardState();
            message.ShardId.AddRange(state.Shards);
            message.Marker = state.WrittenMigrationMarker;
            return message;
        }

        private static EventSourcedRememberEntitiesCoordinatorStore.State RememberShardsStateFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.RememberedShardState.Parser.ParseFrom(bytes);
            return new EventSourcedRememberEntitiesCoordinatorStore.State(message.ShardId.ToImmutableHashSet(), message.Marker);
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
        private static EventSourcedRememberEntitiesShardStore.EntitiesStarted EntityStartedFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.EntityStarted.Parser.ParseFrom(bytes);
            return new EventSourcedRememberEntitiesShardStore.EntitiesStarted(ImmutableHashSet.Create(message.EntityId));
        }

        //
        // EntitiesStarted
        //
        private static Proto.Msg.EntitiesStarted EntitiesStartedToProto(EventSourcedRememberEntitiesShardStore.EntitiesStarted entitiesStarted)
        {
            var message = new Proto.Msg.EntitiesStarted();
            message.EntityId.AddRange(entitiesStarted.Entities);
            return message;
        }

        private static EventSourcedRememberEntitiesShardStore.EntitiesStarted EntitiesStartedFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.EntitiesStarted.Parser.ParseFrom(bytes);
            return new EventSourcedRememberEntitiesShardStore.EntitiesStarted(message.EntityId.ToImmutableHashSet());
        }

        //
        // EntityStopped
        //
        private static EventSourcedRememberEntitiesShardStore.EntitiesStopped EntityStoppedFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.EntityStopped.Parser.ParseFrom(bytes);
            return new EventSourcedRememberEntitiesShardStore.EntitiesStopped(ImmutableHashSet.Create(message.EntityId));
        }

        //
        // EntityStopped
        //
        private static Proto.Msg.EntitiesStopped EntitiesStoppedToProto(EventSourcedRememberEntitiesShardStore.EntitiesStopped entitiesStopped)
        {
            var message = new Proto.Msg.EntitiesStopped();
            message.EntityId.AddRange(entitiesStopped.Entities);
            return message;
        }

        private static EventSourcedRememberEntitiesShardStore.EntitiesStopped EntitiesStoppedFromBinary(byte[] bytes)
        {
            var message = Proto.Msg.EntitiesStopped.Parser.ParseFrom(bytes);
            return new EventSourcedRememberEntitiesShardStore.EntitiesStopped(message.EntityId.ToImmutableHashSet());
        }

        //
        // ShardCoordinator.State
        //
        private static Proto.Msg.CoordinatorState CoordinatorStateToProto(ShardCoordinator.CoordinatorState state)
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

        private ShardCoordinator.CoordinatorState CoordinatorStateFromBinary(byte[] bytes)
        {
            var state = Proto.Msg.CoordinatorState.Parser.ParseFrom(bytes);
            var shards = ImmutableDictionary.CreateRange(state.Shards.Select(entry => new KeyValuePair<string, IActorRef>(entry.ShardId, ResolveActorRef(entry.RegionRef))));
            var regionsZero = ImmutableDictionary.CreateRange(state.Regions.Select(region => new KeyValuePair<IActorRef, IImmutableList<string>>(ResolveActorRef(region), ImmutableList<string>.Empty)));
            var regions = shards.Aggregate(regionsZero, (acc, entry) => acc.SetItem(entry.Value, acc[entry.Value].Add(entry.Key)));
            var proxies = state.RegionProxies.Select(ResolveActorRef).ToImmutableHashSet();
            var unallocatedShards = state.UnallocatedShards.ToImmutableHashSet();

            return new ShardCoordinator.CoordinatorState(
                shards: shards,
                regions: regions,
                regionProxies: proxies,
                unallocatedShards: unallocatedShards);
        }

        //
        // ShardCoordinator.ShardHomeAllocated
        //
        private static Proto.Msg.ShardHomeAllocated ShardHomeAllocatedToProto(ShardCoordinator.ShardHomeAllocated shardHomeAllocated)
        {
            var message = new Proto.Msg.ShardHomeAllocated();
            message.Shard = shardHomeAllocated.Shard;
            message.Region = Akka.Serialization.Serialization.SerializedActorPath(shardHomeAllocated.Region);
            return message;
        }

        private ShardCoordinator.ShardHomeAllocated ShardHomeAllocatedFromBinary(byte[] bytes)
        {
            var msg = Proto.Msg.ShardHomeAllocated.Parser.ParseFrom(bytes);
            return new ShardCoordinator.ShardHomeAllocated(msg.Shard, ResolveActorRef(msg.Region));
        }

        //
        // ShardCoordinator.ShardHome
        //
        private static Proto.Msg.ShardHome ShardHomeToProto(ShardCoordinator.ShardHome shardHome)
        {
            var message = new Proto.Msg.ShardHome();
            message.Shard = shardHome.Shard;
            message.Region = Akka.Serialization.Serialization.SerializedActorPath(shardHome.Ref);
            return message;
        }

        private ShardCoordinator.ShardHome ShardHomeFromBinary(byte[] bytes)
        {
            var msg = Proto.Msg.ShardHome.Parser.ParseFrom(bytes);
            return new ShardCoordinator.ShardHome(msg.Shard, ResolveActorRef(msg.Region));
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
        // EventSourcedRememberEntitiesShardStore.State
        //
        private static Proto.Msg.EntityState EntityStateToProto(EventSourcedRememberEntitiesShardStore.State entityState)
        {
            var message = new Proto.Msg.EntityState();
            message.Entities.AddRange(entityState.Entities);
            return message;
        }

        private static EventSourcedRememberEntitiesShardStore.State EntityStateFromBinary(byte[] bytes)
        {
            var msg = Proto.Msg.EntityState.Parser.ParseFrom(bytes);
            return new EventSourcedRememberEntitiesShardStore.State(msg.Entities.ToImmutableHashSet());
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
            message.Stats.Add((IDictionary<string, int>)s.Stats);
            message.Failed.Add(s.Failed);
            return message;
        }

        private static ShardRegionStats ShardRegionStatsFromBinary(byte[] b)
        {
            var p = Proto.Msg.ShardRegionStats.Parser.ParseFrom(b);
            return new ShardRegionStats(p.Stats.ToImmutableDictionary(), p.Failed.ToImmutableHashSet());
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
                p.Stats.Add(new ClusterShardingStatsEntry() { Address = AddressToProto(s.Key), Stats = ShardRegionStatsToProto(s.Value) });
            }

            return p;
        }

        private static ClusterShardingStats ClusterShardingStatsFromBinary(byte[] b)
        {
            var p = Proto.Msg.ClusterShardingStats.Parser.ParseFrom(b);
            var dict = new Dictionary<Address, ShardRegionStats>();
            foreach (var s in p.Stats)
            {
                dict[AddressFrom(s.Address)] = new ShardRegionStats(s.Stats.Stats.ToImmutableDictionary(), s.Stats.Failed.ToImmutableHashSet());
            }
            return new ClusterShardingStats(dict.ToImmutableDictionary());
        }

        //CurrentRegions
        private Proto.Msg.CurrentRegions CurrentRegionsToProto(CurrentRegions evt)
        {
            var p = new Proto.Msg.CurrentRegions();
            p.Regions.AddRange(evt.Regions.Select(AddressToProto));
            return p;
        }

        private CurrentRegions CurrentRegionsFromBinary(byte[] bytes)
        {
            var p = Proto.Msg.CurrentRegions.Parser.ParseFrom(bytes);
            return new CurrentRegions(p.Regions.Select(AddressFrom).ToImmutableHashSet());
        }

        //ShardState
        private Proto.Msg.ShardState ShardStateToProto(ShardState evt)
        {
            var p = new Proto.Msg.ShardState();
            p.ShardId = evt.ShardId;
            p.EntityIds.AddRange(evt.EntityIds);
            return p;
        }

        private ShardState ShardStateFromProto(Proto.Msg.ShardState parsed)
        {
            return new ShardState(parsed.ShardId, parsed.EntityIds.ToImmutableHashSet());
        }

        private ShardState ShardStateFromBinary(byte[] bytes)
        {
            var p = Proto.Msg.ShardState.Parser.ParseFrom(bytes);
            return new ShardState(p.ShardId, p.EntityIds.ToImmutableHashSet());
        }

        //CurrentShardRegionState
        private Proto.Msg.CurrentShardRegionState CurrentShardRegionStateToProto(CurrentShardRegionState evt)
        {
            var p = new Proto.Msg.CurrentShardRegionState();
            p.Shards.AddRange(evt.Shards.Select(ShardStateToProto));
            p.Failed.AddRange(evt.Failed);
            return p;
        }

        private CurrentShardRegionState CurrentShardRegionStateFromBinary(byte[] bytes)
        {
            var p = Proto.Msg.CurrentShardRegionState.Parser.ParseFrom(bytes);
            return new CurrentShardRegionState(p.Shards.Select(ShardStateFromProto).ToImmutableHashSet(), p.Failed.ToImmutableHashSet());
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
