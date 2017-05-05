//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.Protobuf;
using AddressData = Akka.Remote.Serialization.Proto.Msg.AddressData;

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
                [typeof(InternalClusterAction.InitJoin)] = bytes => new InternalClusterAction.InitJoin(),
                [typeof(InternalClusterAction.InitJoinAck)] = bytes => new InternalClusterAction.InitJoinAck(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(InternalClusterAction.InitJoinNack)] = bytes => new InternalClusterAction.InitJoinNack(AddressFrom(AddressData.Parser.ParseFrom(bytes))),
                [typeof(InternalClusterAction.ExitingConfirmed)] = bytes => new InternalClusterAction.ExitingConfirmed(UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes))),
                [typeof(ClusterRouterPool)] = ClusterRouterPoolFrom
            };
        }

        public override bool IncludeManifest => true;

        public override byte[] ToBinary(object obj)
        {
            var heartbeat = obj as ClusterHeartbeatSender.Heartbeat;
            if (heartbeat != null)
            {
                return AddressToProto(heartbeat.From).ToByteArray();
            }

            var heartbeatRsp = obj as ClusterHeartbeatSender.HeartbeatRsp;
            if (heartbeatRsp != null)
            {
                return UniqueAddressToProto(heartbeatRsp.From).ToByteArray();
            }

            var gossipEnvelope = obj as GossipEnvelope;
            if (gossipEnvelope != null)
            {
                return GossipEnvelopeToProto(gossipEnvelope);
            }

            var gossipStatus = obj as GossipStatus;
            if (gossipStatus != null)
            {
                return GossipStatusToProto(gossipStatus);
            }

            var join = obj as InternalClusterAction.Join;
            if (join != null)
            {
                return JoinToByteArray(join);
            }

            var welcome = obj as InternalClusterAction.Welcome;
            if (welcome != null)
            {
                return WelcomeMessageBuilder(welcome);
            }

            var leave = obj as ClusterUserAction.Leave;
            if (leave != null)
            {
                return AddressToProto(leave.Address).ToByteArray();
            }

            var down = obj as ClusterUserAction.Down;
            if (down != null)
            {
                return AddressToProto(down.Address).ToByteArray();
            }

            if (obj is InternalClusterAction.InitJoin)
            {
                return new Google.Protobuf.WellKnownTypes.Empty().ToByteArray();
            }

            var initJoinAck = obj as InternalClusterAction.InitJoinAck;
            if (initJoinAck != null)
            {
                return AddressToProto(initJoinAck.Address).ToByteArray();
            }

            var initJoinNack = obj as InternalClusterAction.InitJoinNack;
            if (initJoinNack != null)
            {
                return AddressToProto(initJoinNack.Address).ToByteArray();
            }

            var exitingConfirmed = obj as InternalClusterAction.ExitingConfirmed;
            if (exitingConfirmed != null)
            {
                return UniqueAddressToProto(exitingConfirmed.Address).ToByteArray();
            }

            var pool = obj as ClusterRouterPool;
            if (pool != null)
            {
                return ClusterRouterPoolToByteArray(pool);
            }

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{nameof(ClusterMessageSerializer)}]");
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            Func<byte[], object> factory;
            if (_fromBinaryMap.TryGetValue(type, out factory))
            {
                return factory(bytes);
            }

            throw new ArgumentException($"{nameof(ClusterMessageSerializer)} cannot deserialize object of type {type}");
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
            var allMembers = gossip.Members.ToList();
            var allAddresses = gossip.Members.Select(x => x.UniqueAddress).ToList();
            var addressMapping = allAddresses.ZipWithIndex();
            var allRoles = allMembers.Aggregate(ImmutableHashSet.Create<string>(), (set, member) => set.Union(member.Roles));
            var roleMapping = allRoles.ZipWithIndex();
            var allHashes = gossip.Version.Versions.Keys.Select(x => x.ToString()).ToList();
            var hashMapping = allHashes.ZipWithIndex();

            Func<UniqueAddress, int> mapUniqueAddress = address => MapWithErrorMessage(addressMapping, address, "address");

            Func<Member, Proto.Msg.Member> memberToProto = m =>
            {
                var protoMember = new Proto.Msg.Member();
                protoMember.AddressIndex = mapUniqueAddress(m.UniqueAddress);
                protoMember.UpNumber = m.UpNumber;
                protoMember.Status = (Proto.Msg.Member.Types.MemberStatus)m.Status;
                protoMember.RolesIndexes.AddRange(m.Roles.Select(s => MapWithErrorMessage(roleMapping, s, "role")));
                return protoMember;
            };

            var reachabilityProto = ReachabilityToProto(gossip.Overview.Reachability, addressMapping);
            var membersProtos = gossip.Members.Select(memberToProto);
            var seenProtos = gossip.Overview.Seen.Select(mapUniqueAddress);

            var overview = new Proto.Msg.GossipOverview();
            overview.Seen.AddRange(seenProtos);
            overview.ObserverReachability.AddRange(reachabilityProto);

            var message = new Proto.Msg.Gossip();
            message.AllAddresses.AddRange(allAddresses.Select(UniqueAddressToProto));
            message.AllRoles.AddRange(allRoles);
            message.AllHashes.AddRange(allHashes);
            message.Members.AddRange(membersProtos);
            message.Overview = overview;
            message.Version = VectorClockToProto(gossip.Version, hashMapping);
            return message;
        }

        private static Gossip GossipFrom(Proto.Msg.Gossip gossip)
        {
            var addressMapping = gossip.AllAddresses.Select(UniqueAddressFrom).ToList();
            var roleMapping = gossip.AllRoles.ToList();
            var hashMapping = gossip.AllHashes.ToList();

            Func<Proto.Msg.Member, Member> memberFromProto = member =>
            {
                return Member.Create(
                    addressMapping[member.AddressIndex],
                    member.UpNumber,
                    (MemberStatus) member.Status,
                    member.RolesIndexes.Select(x => roleMapping[x]).ToImmutableHashSet());
            };

            var members = gossip.Members.Select(memberFromProto).ToImmutableSortedSet(Member.Ordering);
            var reachability = ReachabilityFromProto(gossip.Overview.ObserverReachability, addressMapping);
            var seen = gossip.Overview.Seen.Select(x => addressMapping[x]).ToImmutableHashSet();
            var overview = new GossipOverview(seen, reachability);

            return new Gossip(members, overview, VectorClockFrom(gossip.Version, hashMapping));
        }

        private static IEnumerable<Proto.Msg.ObserverReachability> ReachabilityToProto(Reachability reachability, Dictionary<UniqueAddress, int> addressMapping)
        {
            var builderList = new List<Proto.Msg.ObserverReachability>();
            foreach (var version in reachability.Versions)
            {
                var subjectReachability = reachability.RecordsFrom(version.Key).Select(
                    r =>
                    {
                        var sr = new Proto.Msg.SubjectReachability();
                        sr.AddressIndex = MapWithErrorMessage(addressMapping, r.Subject, "address");
                        sr.Status = (Proto.Msg.SubjectReachability.Types.ReachabilityStatus)r.Status;
                        sr.Version = r.Version;
                        return sr;
                    });

                var observerReachability = new Proto.Msg.ObserverReachability();
                observerReachability.AddressIndex = MapWithErrorMessage(addressMapping, version.Key, "address");
                observerReachability.Version = version.Value;
                observerReachability.SubjectReachability.AddRange(subjectReachability);
                builderList.Add(observerReachability);
            }
            return builderList;
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
            if (map.ContainsKey(value)) return map[value];
            throw new ArgumentException($"Unknown {unknown} [{value}] in cluster message");
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