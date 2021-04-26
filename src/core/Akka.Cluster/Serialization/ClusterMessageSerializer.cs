//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Annotations;
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
    [InternalApi]
    public class ClusterMessageSerializer : SerializerWithStringManifest
    {
        /*
         * BUG: should have been a SerializerWithStringManifest this entire time
         * Since it wasn't need to include full type names for backwards compatibility
         */
        internal const string JoinManifest = "Akka.Cluster.InternalClusterAction+Join, Akka.Cluster";
        internal const string WelcomeManifest = "Akka.Cluster.InternalClusterAction+Welcome, Akka.Cluster";
        internal const string LeaveManifest = "Akka.Cluster.ClusterUserAction+Leave, Akka.Cluster";
        internal const string DownManifest = "Akka.Cluster.ClusterUserAction+Down, Akka.Cluster";

        internal const string InitJoinManifest = "Akka.Cluster.InternalClusterAction+InitJoin, Akka.Cluster";

        internal const string InitJoinAckManifest = "Akka.Cluster.InternalClusterAction+InitJoinAck, Akka.Cluster";

        internal const string InitJoinNackManifest = "Akka.Cluster.InternalClusterAction+InitJoinNack, Akka.Cluster";

        // TODO: remove in a future version of Akka.NET (2.0)
        internal const string HeartBeatManifestPre1419 = "Akka.Cluster.ClusterHeartbeatSender+Heartbeat, Akka.Cluster";
        internal const string HeartBeatRspManifestPre1419 = "Akka.Cluster.ClusterHeartbeatSender+HeartbeatRsp, Akka.Cluster";

        internal const string HeartBeatManifest = "HB";
        internal const string HeartBeatRspManifest = "HBR";

        internal const string ExitingConfirmedManifest = "Akka.Cluster.InternalClusterAction+ExitingConfirmed, Akka.Cluster";

        internal const string GossipStatusManifest = "Akka.Cluster.GossipStatus, Akka.Cluster";
        internal const string GossipEnvelopeManifest = "Akka.Cluster.GossipEnvelope, Akka.Cluster";
        internal const string ClusterRouterPoolManifest = "Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster";


        public ClusterMessageSerializer(ExtendedActorSystem system) : base(system)
        {

           
        }

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

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case HeartBeatManifestPre1419:
                    return DeserializeHeartbeatAsAddress(bytes);
                case HeartBeatRspManifestPre1419:
                    return DeserializeHeartbeatRspAsUniqueAddress(bytes);
                case HeartBeatManifest:
                    return DeserializeHeartbeat(bytes);
                case HeartBeatRspManifest:
                    return DeserializeHeartbeatRsp(bytes);
                case GossipStatusManifest:
                    return GossipStatusFrom(bytes);
                case GossipEnvelopeManifest:
                    return GossipEnvelopeFrom(bytes);
                case InitJoinManifest:
                    return new InternalClusterAction.InitJoin();
                case InitJoinAckManifest:
                    return new InternalClusterAction.InitJoinAck(AddressFrom(AddressData.Parser.ParseFrom(bytes)));
                case InitJoinNackManifest:
                    return new InternalClusterAction.InitJoinNack(AddressFrom(AddressData.Parser.ParseFrom(bytes)));
                case JoinManifest:
                    return JoinFrom(bytes);
                case WelcomeManifest:
                    return WelcomeFrom(bytes);
                case LeaveManifest:
                    return new ClusterUserAction.Leave(AddressFrom(AddressData.Parser.ParseFrom(bytes)));
                case DownManifest:
                    return new ClusterUserAction.Down(AddressFrom(AddressData.Parser.ParseFrom(bytes)));
                case ExitingConfirmedManifest:
                    return new InternalClusterAction.ExitingConfirmed(
                        UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes)));
                case ClusterRouterPoolManifest:
                    return ClusterRouterPoolFrom(bytes);
                default:
                    throw new ArgumentException($"Unknown manifest [{manifest}] in [{nameof(ClusterMessageSerializer)}]");
            }
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case InternalClusterAction.Join _:
                    return JoinManifest;
                case InternalClusterAction.Welcome _:
                    return WelcomeManifest;
                case ClusterUserAction.Leave _:
                    return LeaveManifest;
                case ClusterUserAction.Down _:
                    return DownManifest;
                case InternalClusterAction.InitJoin _:
                    return InitJoinManifest;
                case InternalClusterAction.InitJoinAck _:
                    return InitJoinAckManifest;
                case InternalClusterAction.InitJoinNack _:
                    return InitJoinNackManifest;
                case ClusterHeartbeatSender.Heartbeat _:
                    return HeartBeatManifestPre1419;
                case ClusterHeartbeatSender.HeartbeatRsp _:
                    return HeartBeatRspManifestPre1419;
                case InternalClusterAction.ExitingConfirmed _:
                    return ExitingConfirmedManifest;
                case GossipStatus _:
                    return GossipStatusManifest;
                case GossipEnvelope _:
                    return GossipEnvelopeManifest;
                case ClusterRouterPool _:
                    return ClusterRouterPoolManifest;
                default:
                    throw new ArgumentException($"Can't serialize object of type [{o.GetType()}] in [{GetType()}]");
            }
        }

        //
        // Internal Cluster Action Messages
        //
        private static byte[] JoinToByteArray(InternalClusterAction.Join join)
        {
            var message = new Proto.Msg.Join();
            message.Node = UniqueAddressToProto(join.Node);
            message.Roles.AddRange(join.Roles);
            message.AppVersion = join.AppVersion.Version;
            return message.ToByteArray();
        }

        private static InternalClusterAction.Join JoinFrom(byte[] bytes)
        {
            var join = Proto.Msg.Join.Parser.ParseFrom(bytes);
            var ver = !string.IsNullOrEmpty(join.AppVersion) ? AppVersion.Create(join.AppVersion) : AppVersion.Zero;
            return new InternalClusterAction.Join(UniqueAddressFrom(join.Node), join.Roles.ToImmutableHashSet(), ver);
        }

        // TODO: need to gzip compress the Welcome message for large clusters
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
            var allAppVersions = allMembers.Select(i => i.AppVersion.Version).ToImmutableHashSet();
            var appVersionMapping = allAppVersions.ZipWithIndex();

            int MapUniqueAddress(UniqueAddress address) => MapWithErrorMessage(addressMapping, address, "address");

            int MapAppVersion(AppVersion appVersion) => MapWithErrorMessage(appVersionMapping, appVersion.Version, "appVersion");

            Proto.Msg.Member MemberToProto(Member m)
            {
                var protoMember = new Proto.Msg.Member();
                protoMember.AddressIndex = MapUniqueAddress(m.UniqueAddress);
                protoMember.UpNumber = m.UpNumber;
                protoMember.Status = (Proto.Msg.Member.Types.MemberStatus)m.Status;
                protoMember.RolesIndexes.AddRange(m.Roles.Select(s => MapWithErrorMessage(roleMapping, s, "role")));
                protoMember.AppVersionIndex = MapAppVersion(m.AppVersion);
                return protoMember;
            }

            var reachabilityProto = ReachabilityToProto(gossip.Overview.Reachability, addressMapping);
            var membersProtos = gossip.Members.Select((Func<Member, Proto.Msg.Member>)MemberToProto);
            var seenProtos = gossip.Overview.Seen.Select((Func<UniqueAddress, int>)MapUniqueAddress);

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
            message.AllAppVersions.AddRange(allAppVersions);
            return message;
        }

        private static Gossip GossipFrom(Proto.Msg.Gossip gossip)
        {
            var addressMapping = gossip.AllAddresses.Select(UniqueAddressFrom).ToList();
            var roleMapping = gossip.AllRoles.ToList();
            var hashMapping = gossip.AllHashes.ToList();
            var appVersionMapping = gossip.AllAppVersions.Select(i => AppVersion.Create(i)).ToList();

            Member MemberFromProto(Proto.Msg.Member member) =>
                Member.Create(
                    addressMapping[member.AddressIndex],
                    member.UpNumber,
                    (MemberStatus)member.Status,
                    member.RolesIndexes.Select(x => roleMapping[x]).ToImmutableHashSet(),
                    appVersionMapping.Any() ? appVersionMapping[member.AppVersionIndex] : AppVersion.Zero
                    );

            var members = gossip.Members.Select((Func<Proto.Msg.Member, Member>)MemberFromProto).ToImmutableSortedSet(Member.Ordering);
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
            if (map.TryGetValue(value, out int mapIndex))
                return mapIndex;

            throw new ArgumentException($"Unknown {unknown} [{value}] in cluster message");
        }

        //
        // Heartbeat
        //
        private static ClusterHeartbeatSender.HeartbeatRsp DeserializeHeartbeatRspAsUniqueAddress(byte[] bytes)
        {
            var uniqueAddress = UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes));
            return new ClusterHeartbeatSender.HeartbeatRsp(uniqueAddress, -1, -1);
        }

        private static ClusterHeartbeatSender.HeartbeatRsp DeserializeHeartbeatRsp(byte[] bytes)
        {
            var hbsp = HeartBeatResponse.Parser.ParseFrom(bytes);
            return new ClusterHeartbeatSender.HeartbeatRsp(UniqueAddressFrom(hbsp.From), hbsp.SequenceNr, hbsp.CreationTime);
        }

        private static ClusterHeartbeatSender.Heartbeat DeserializeHeartbeatAsAddress(byte[] bytes)
        {
            return new ClusterHeartbeatSender.Heartbeat(AddressFrom(AddressData.Parser.ParseFrom(bytes)), -1, -1);
        }

        private static ClusterHeartbeatSender.Heartbeat DeserializeHeartbeat(byte[] bytes)
        {
            var hb = Heartbeat.Parser.ParseFrom(bytes);
            return new ClusterHeartbeatSender.Heartbeat(AddressFrom(hb.From), hb.SequenceNr, hb.CreationTime);
        }

        //
        // Address
        //

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static AddressData AddressToProto(Address address)
        {
            var message = new AddressData();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Address AddressFrom(AddressData addressProto)
        {
            return new Address(
                addressProto.Protocol,
                addressProto.System,
                addressProto.Hostname,
                addressProto.Port == 0 ? null : (int?)addressProto.Port);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static Proto.Msg.UniqueAddress UniqueAddressToProto(UniqueAddress uniqueAddress)
        {
            var message = new Proto.Msg.UniqueAddress();
            message.Address = AddressToProto(uniqueAddress.Address);
            message.Uid = (uint)uniqueAddress.Uid;
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static UniqueAddress UniqueAddressFrom(Proto.Msg.UniqueAddress uniqueAddressProto)
        {
            return new UniqueAddress(AddressFrom(uniqueAddressProto.Address), (int)uniqueAddressProto.Uid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetObjectManifest(Serializer serializer, object obj)
        {
            if (serializer is SerializerWithStringManifest manifestSerializer)
            {
                return manifestSerializer.Manifest(obj);
            }

            return obj.GetType().TypeQualifiedName();
        }
    }
}
