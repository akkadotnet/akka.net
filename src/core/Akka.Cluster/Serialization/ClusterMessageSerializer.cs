//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Serialization.Proto.Msg;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;
using AddressData = Akka.Remote.Serialization.Proto.Msg.AddressData;
using ClusterRouterPool = Akka.Cluster.Routing.ClusterRouterPool;
using ClusterRouterPoolSettings = Akka.Cluster.Routing.ClusterRouterPoolSettings;

namespace Akka.Cluster.Serialization
{
    public class ClusterMessageSerializer : Serializer
    {
        private readonly Dictionary<Type, Func<byte[], object>> _fromBinaryMap;

        public ClusterMessageSerializer(ExtendedActorSystem system) : base(system)
        {

            _fromBinaryMap = new Dictionary<Type, Func<byte[], object>>
            {
                [typeof(ClusterHeartbeatSender.Heartbeat)] = bytes => new ClusterHeartbeatSender.Heartbeat(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(ClusterHeartbeatSender.HeartbeatRsp)] = bytes => new ClusterHeartbeatSender.HeartbeatRsp(UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes))),
                [typeof(GossipEnvelope)] = GossipEnvelopeFrom,
                [typeof(GossipStatus)] = GossipStatusFrom,
                [typeof(InternalClusterAction.Join)] = JoinFrom,
                [typeof(InternalClusterAction.Welcome)] = WelcomeFrom,
                [typeof(ClusterUserAction.Leave)] = bytes => new ClusterUserAction.Leave(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(ClusterUserAction.Down)] = bytes => new ClusterUserAction.Down(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(InternalClusterAction.InitJoin)] = bytes => InternalClusterAction.InitJoin.Instance,
                [typeof(InternalClusterAction.InitJoinAck)] = bytes => new InternalClusterAction.InitJoinAck(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(InternalClusterAction.InitJoinNack)] = bytes => new InternalClusterAction.InitJoinNack(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(InternalClusterAction.ExitingConfirmed)] = bytes => new InternalClusterAction.ExitingConfirmed(UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes))),
                [typeof(ClusterRouterPool)] = ClusterRouterPoolFrom
            };
        }

        public override bool IncludeManifest => true;

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case ClusterHeartbeatSender.Heartbeat heartbeat:
                    return AddressToProto(heartbeat.From).ToByteArray();
                case ClusterHeartbeatSender.HeartbeatRsp heartbeatRsp:
                    return UniqueAddressToProto(heartbeatRsp.From).ToByteArray();
                case GossipEnvelope gossipEnvelope:
                    return GossipEnvelopeToProto(gossipEnvelope);
                case GossipStatus gossipStatus:
                    return GossipStatusToProto(gossipStatus);
                case InternalClusterAction.Join @join:
                    return JoinToByteArray(@join);
                case InternalClusterAction.Welcome welcome:
                    return WelcomeMessageBuilder(welcome);
                case ClusterUserAction.Leave leave:
                    return AddressToProto(leave.Address).ToByteArray();
                case ClusterUserAction.Down down:
                    return AddressToProto(down.Address).ToByteArray();
                case InternalClusterAction.InitJoin _:
                    return new Google.Protobuf.WellKnownTypes.Empty().ToByteArray();
                case InternalClusterAction.InitJoinAck initJoinAck:
                    return AddressToProto(initJoinAck.Address).ToByteArray();
                case InternalClusterAction.InitJoinNack initJoinNack:
                    return AddressToProto(initJoinNack.Address).ToByteArray();
                case InternalClusterAction.ExitingConfirmed exitingConfirmed:
                    return UniqueAddressToProto(exitingConfirmed.Address).ToByteArray();
                case ClusterRouterPool pool:
                    return ClusterRouterPoolToByteArray(pool);
                default:
                    throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof(ClusterMessageSerializer)}]");
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (_fromBinaryMap.TryGetValue(type, out var factory))
                return factory(bytes);

            throw new SerializationException($"{nameof(ClusterMessageSerializer)} cannot deserialize object of type {type}");
        }

        //
        // Internal Cluster Action Messages
        //
        private static byte[] JoinToByteArray(InternalClusterAction.Join join)
        {
            var message = new Proto.Msg.Join();
            message.Node = UniqueAddressToProto(join.Node);
            message.Roles.AddRange(join.Roles);
            return message.ToByteArray();
        }

        private static InternalClusterAction.Join JoinFrom(byte[] bytes)
        {
            var join = Proto.Msg.Join.Parser.ParseFrom(bytes);
            return new InternalClusterAction.Join(UniqueAddressFrom(join.Node), join.Roles.ToImmutableHashSet());
        }

        private static byte[] WelcomeMessageBuilder(InternalClusterAction.Welcome welcome)
        {
            var welcomeProto = new Proto.Msg.Welcome();
            welcomeProto.From = UniqueAddressToProto(welcome.From);
            welcomeProto.Gossip = GossipToProto(welcome.Gossip);
            return welcomeProto.ToByteArray();
        }

        private static InternalClusterAction.Welcome WelcomeFrom(byte[] bytes)
        {
            var welcomeProto = Proto.Msg.Welcome.Parser.ParseFrom(bytes);
            return new InternalClusterAction.Welcome(UniqueAddressFrom(welcomeProto.From), GossipFrom(welcomeProto.Gossip));
        }

        //
        // Cluster Gossip Messages
        //
        private static byte[] GossipEnvelopeToProto(GossipEnvelope gossipEnvelope)
        {
            var message = new Proto.Msg.GossipEnvelope();

            message.From = UniqueAddressToProto(gossipEnvelope.From);
            message.To = UniqueAddressToProto(gossipEnvelope.To);
            message.SerializedGossip = ByteString.CopyFrom(GossipToProto(gossipEnvelope.Gossip).ToByteArray());

            return message.ToByteArray();
        }

        private static GossipEnvelope GossipEnvelopeFrom(byte[] bytes)
        {
            var gossipEnvelopeProto = Proto.Msg.GossipEnvelope.Parser.ParseFrom(bytes);

            return new GossipEnvelope(
                UniqueAddressFrom(gossipEnvelopeProto.From),
                UniqueAddressFrom(gossipEnvelopeProto.To),
                GossipFrom(Proto.Msg.Gossip.Parser.ParseFrom(gossipEnvelopeProto.SerializedGossip)));
        }

        private static byte[] GossipStatusToProto(GossipStatus gossipStatus)
        {
            var allHashes = gossipStatus.Version.Versions.Keys.Select(x => x.ToString()).ToList();
            var hashMapping = allHashes.ZipWithIndex();

            var message = new Proto.Msg.GossipStatus();
            message.From = UniqueAddressToProto(gossipStatus.From);
            message.AllHashes.AddRange(allHashes);
            message.Version = VectorClockToProto(gossipStatus.Version, hashMapping);
            return message.ToByteArray();
        }

        private static GossipStatus GossipStatusFrom(byte[] bytes)
        {
            var gossipStatusProto = Proto.Msg.GossipStatus.Parser.ParseFrom(bytes);
            return new GossipStatus(UniqueAddressFrom(gossipStatusProto.From), VectorClockFrom(gossipStatusProto.Version, gossipStatusProto.AllHashes));
        }

        //
        // Cluster routing
        //

        private byte[] ClusterRouterPoolToByteArray(ClusterRouterPool clusterRouterPool)
        {
            var message = new Proto.Msg.ClusterRouterPool();
            message.Pool = PoolToProto(clusterRouterPool.Local);
            message.Settings = ClusterRouterPoolSettingsToProto(clusterRouterPool.Settings);
            return message.ToByteArray();
        }

        private ClusterRouterPool ClusterRouterPoolFrom(byte[] bytes)
        {
            var clusterRouterPool = Proto.Msg.ClusterRouterPool.Parser.ParseFrom(bytes);
            return new ClusterRouterPool(PoolFrom(clusterRouterPool.Pool), ClusterRouterPoolSettingsFrom(clusterRouterPool.Settings));
        }

        private Proto.Msg.Pool PoolToProto(Akka.Routing.Pool pool)
        {
            var message = new Proto.Msg.Pool();
            var serializer = system.Serialization.FindSerializerFor(pool);
            message.SerializerId = (uint)serializer.Identifier;
            message.Data = ByteString.CopyFrom(serializer.ToBinary(pool));
            message.Manifest = GetObjectManifest(serializer, pool);
            return message;
        }

        private Akka.Routing.Pool PoolFrom(Proto.Msg.Pool poolProto)
        {
            return (Akka.Routing.Pool)system.Serialization.Deserialize(poolProto.Data.ToByteArray(), (int)poolProto.SerializerId, poolProto.Manifest);
        }

        private static Proto.Msg.ClusterRouterPoolSettings ClusterRouterPoolSettingsToProto(ClusterRouterPoolSettings clusterRouterPoolSettings)
        {
            var message = new Proto.Msg.ClusterRouterPoolSettings();
            message.TotalInstances = (uint)clusterRouterPoolSettings.TotalInstances;
            message.MaxInstancesPerNode = (uint)clusterRouterPoolSettings.MaxInstancesPerNode;
            message.AllowLocalRoutees = clusterRouterPoolSettings.AllowLocalRoutees;
            message.UseRole = clusterRouterPoolSettings.UseRole ?? string.Empty;
            return message;
        }

        private static ClusterRouterPoolSettings ClusterRouterPoolSettingsFrom(Proto.Msg.ClusterRouterPoolSettings clusterRouterPoolSettingsProto)
        {
            return new ClusterRouterPoolSettings(
                (int)clusterRouterPoolSettingsProto.TotalInstances,
                (int)clusterRouterPoolSettingsProto.MaxInstancesPerNode,
                clusterRouterPoolSettingsProto.AllowLocalRoutees,
                clusterRouterPoolSettingsProto.UseRole == string.Empty ? null : clusterRouterPoolSettingsProto.UseRole);
        }

        //
        // Gossip
        //

        private static Proto.Msg.Gossip GossipToProto(Gossip gossip)
        {
            var allMembers = gossip.Members.ToArray();
            var allAddresses = gossip.Members
                .Select(x => x.UniqueAddress)
                .Union(gossip.Tombstones.Select(x => x.Key))
                .ToImmutableHashSet();
            var addressMapping = allAddresses.ZipWithIndex();
            var allRoles = allMembers.SelectMany(m => m.Roles).ToImmutableHashSet();
            var roleMapping = allRoles.ZipWithIndex();
            var allHashes = gossip.Version.Versions.Keys.Select(x => x.ToString()).ToList();
            var hashMapping = allHashes.ZipWithIndex();

            int MapUniqueAddress(UniqueAddress address) => MapWithErrorMessage(addressMapping, address, "address");

            int MapRole(string role) => MapWithErrorMessage(roleMapping, role, "role");

            Proto.Msg.Member MemberToProto(Member m)
            {
                var protoMember = new Proto.Msg.Member();
                protoMember.AddressIndex = MapUniqueAddress(m.UniqueAddress);
                protoMember.UpNumber = m.UpNumber;
                protoMember.Status = (Proto.Msg.Member.Types.MemberStatus)m.Status;
                protoMember.RolesIndexes.AddRange(m.Roles.Select(MapRole));
                return protoMember;
            }

            IEnumerable<Proto.Msg.ObserverReachability> ReachabilityToProto(Reachability reachability)
            {
                foreach (var entry in reachability.Versions)
                {
                    var observer = entry.Key;
                    var version = entry.Value;
                    var subjectReachability = reachability
                        .RecordsFrom(observer)
                        .Select(record => new Proto.Msg.SubjectReachability
                        {
                            AddressIndex = MapUniqueAddress(record.Subject),
                            Status = (Proto.Msg.SubjectReachability.Types.ReachabilityStatus)record.Status,
                            Version = record.Version
                        });

                    var observerReachability = new ObserverReachability
                    {
                        AddressIndex = MapUniqueAddress(observer),
                        Version = version
                    };
                    observerReachability.SubjectReachability.AddRange(subjectReachability);
                    yield return observerReachability;
                }
            }

            Proto.Msg.Tombstone TombstoneToProto(KeyValuePair<UniqueAddress, DateTime> kv) => 
                new Tombstone
                {
                    AddressIndex = MapUniqueAddress(kv.Key),
                    Timestamp = kv.Value.Ticks / TimeSpan.TicksPerMillisecond // we operate in milliseconds
                };

            var protoReachability = ReachabilityToProto(gossip.Overview.Reachability).ToArray();
            var members = gossip.Members.Select(MemberToProto).ToArray();
            var seen = gossip.Overview.Seen.Select(MapUniqueAddress).ToArray();

            var overview = new Proto.Msg.GossipOverview{ };
            overview.Seen.AddRange(seen);
            overview.ObserverReachability.AddRange(protoReachability);

            var protoAllAddresses = allAddresses.Select(UniqueAddressToProto).ToArray();
            var result = new Proto.Msg.Gossip
            {
                Overview = overview,
                Version = VectorClockToProto(gossip.Version, hashMapping),
            };
            result.AllAddresses.AddRange(protoAllAddresses);
            result.AllRoles.AddRange(allRoles);
            result.AllHashes.AddRange(allHashes);
            result.Members.AddRange(members);
            result.Tombstones.AddRange(gossip.Tombstones.Select(TombstoneToProto));

            return result;
        }

        private static Gossip GossipFrom(Proto.Msg.Gossip gossip)
        {
            var addressMapping = gossip.AllAddresses.Select(UniqueAddressFrom).ToList();
            var roleMapping = gossip.AllRoles.ToList();
            var hashMapping = gossip.AllHashes.ToList();

            Member MemberFromProto(Proto.Msg.Member member) =>
                Member.Create(
                    addressMapping[member.AddressIndex],
                    member.UpNumber,
                    (MemberStatus)member.Status,
                    member.RolesIndexes.Select(x => roleMapping[x]).ToImmutableHashSet());

            var members = gossip.Members.Select(MemberFromProto).ToImmutableSortedSet(Member.Ordering);
            var reachability = ReachabilityFromProto(gossip.Overview.ObserverReachability, addressMapping);
            var seen = gossip.Overview.Seen.Select(x => addressMapping[x]).ToImmutableHashSet();
            var overview = new GossipOverview(seen, reachability);
            var tombstones = gossip.Tombstones.Select(t =>
            {
                var addr = addressMapping[t.AddressIndex];
                var date = new DateTime(t.Timestamp * TimeSpan.TicksPerMillisecond);
                return new KeyValuePair<UniqueAddress,DateTime>(addr, date);
            }).ToImmutableDictionary();

            return new Gossip(members, overview, VectorClockFrom(gossip.Version, hashMapping), tombstones);
        }
        
        private static Reachability ReachabilityFromProto(IEnumerable<Proto.Msg.ObserverReachability> reachabilityProto, List<UniqueAddress> addressMapping)
        {
            var recordBuilder = ImmutableList.CreateBuilder<Reachability.Record>();
            var versionsBuilder = ImmutableDictionary.CreateBuilder<UniqueAddress, long>();
            foreach (var o in reachabilityProto)
            {
                var observer = addressMapping[o.AddressIndex];
                versionsBuilder.Add(observer, o.Version);
                foreach (var s in o.SubjectReachability)
                {
                    var subject = addressMapping[s.AddressIndex];
                    var record = new Reachability.Record(observer, subject, (Reachability.ReachabilityStatus)s.Status,
                        s.Version);
                    recordBuilder.Add(record);
                }
            }

            return new Reachability(recordBuilder.ToImmutable(), versionsBuilder.ToImmutable());
        }

        private static Proto.Msg.VectorClock VectorClockToProto(VectorClock vectorClock, Dictionary<string, int> hashMapping)
        {
            var message = new Proto.Msg.VectorClock();

            foreach (var clock in vectorClock.Versions)
            {
                var version = new Proto.Msg.VectorClock.Types.Version();
                version.HashIndex = MapWithErrorMessage(hashMapping, clock.Key.ToString(), "hash");
                version.Timestamp = clock.Value;
                message.Versions.Add(version);
            }
            message.Timestamp = 0L;

            return message;
        }

        private static VectorClock VectorClockFrom(Proto.Msg.VectorClock version, IList<string> hashMapping)
        {
            return VectorClock.Create(version.Versions.ToImmutableSortedDictionary(version1 =>
                    VectorClock.Node.FromHash(hashMapping[version1.HashIndex]), version1 => version1.Timestamp));
        }

        private static int MapWithErrorMessage<T>(Dictionary<T, int> map, T value, string unknown)
        {
            if (map.TryGetValue(value, out int mapIndex))
                return mapIndex;

            var sb = new StringBuilder("{");
            foreach (var kv in map)
            {
                sb.Append(kv.Key).Append(':').Append(kv.Value).Append(",");
            }

            sb.Append('}');

            throw new ArgumentException($"Unknown {unknown} [{value}] in cluster message. Available entries are: {sb.ToString()}");
        }

        //
        // Address
        //

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static AddressData AddressToProto(Address address)
        {
            var message = new AddressData();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Address AddressFrom(AddressData addressProto)
        {
            return new Address(
                addressProto.Protocol,
                addressProto.System,
                addressProto.Hostname,
                addressProto.Port == 0 ? null : (int?)addressProto.Port);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Proto.Msg.UniqueAddress UniqueAddressToProto(UniqueAddress uniqueAddress)
        {
            var message = new Proto.Msg.UniqueAddress();
            message.Address = AddressToProto(uniqueAddress.Address);
            message.Uid = (uint)uniqueAddress.Uid;
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static UniqueAddress UniqueAddressFrom(Proto.Msg.UniqueAddress uniqueAddressProto)
        {
            return new UniqueAddress(AddressFrom(uniqueAddressProto.Address), (int)uniqueAddressProto.Uid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetObjectManifest(Serializer serializer, object obj)
        {
            var manifestSerializer = serializer as SerializerWithStringManifest;
            if (manifestSerializer != null)
            {
                return manifestSerializer.Manifest(obj);
            }

            return obj.GetType().TypeQualifiedName();
        }
    }
}
