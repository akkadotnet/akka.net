//-----------------------------------------------------------------------
// <copyright file="ClusterMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Serialization;
using Akka.Util;
using Google.Protobuf;

namespace Akka.Cluster.Serialization
{
    public class ClusterMessageSerializer : Serializer
    {
        public ClusterMessageSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier { get; } = 23;

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
                return new Proto.Msg.Empty().ToByteArray();
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

            var pool = obj as Routing.ClusterRouterPool;
            if (pool != null)
            {
                return ClusterRouterPoolToByteArray(pool);
            }

            throw new ArgumentException($"Can't serialize object of type {obj.GetType()}");
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(ClusterHeartbeatSender.Heartbeat))
            {
                return new ClusterHeartbeatSender.Heartbeat(AddressFrom(Proto.Msg.Address.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(ClusterHeartbeatSender.HeartbeatRsp))
            {
                return new ClusterHeartbeatSender.HeartbeatRsp(UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(GossipEnvelope))
            {
                return GossipEnvelopeFrom(bytes);
            }

            if (type == typeof(GossipStatus))
            {
                return GossipStatusFrom(bytes);
            }

            if (type == typeof(InternalClusterAction.Join))
            {
                return JoinFrom(bytes);
            }

            if (type == typeof(InternalClusterAction.Welcome))
            {
                return WelcomeFrom(Proto.Msg.Welcome.Parser.ParseFrom(bytes));
            }

            if (type == typeof(ClusterUserAction.Leave))
            {
                return new ClusterUserAction.Leave(AddressFrom(Proto.Msg.Address.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(ClusterUserAction.Down))
            {
                return new ClusterUserAction.Down(AddressFrom(Proto.Msg.Address.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(InternalClusterAction.InitJoin))
            {
                return new InternalClusterAction.InitJoin();
            }

            if (type == typeof(InternalClusterAction.InitJoinAck))
            {
                return new InternalClusterAction.InitJoinAck(AddressFrom(Proto.Msg.Address.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(InternalClusterAction.InitJoinNack))
            {
                return new InternalClusterAction.InitJoinNack(AddressFrom(Proto.Msg.Address.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(InternalClusterAction.ExitingConfirmed))
            {
                return new InternalClusterAction.ExitingConfirmed(UniqueAddressFrom(Proto.Msg.UniqueAddress.Parser.ParseFrom(bytes)));
            }

            if (type == typeof(Routing.ClusterRouterPool))
            {
                return ClusterRouterPoolFrom(bytes);
            }

            throw new ArgumentException(typeof(ClusterMessageSerializer) + " cannot deserialize object of type " + type);
        }

        //
        // Internal Cluster Action Messages
        //
        private byte[] JoinToByteArray(InternalClusterAction.Join join)
        {
            var message = new Proto.Msg.Join();
            message.Node = UniqueAddressToProto(join.Node);
            foreach (var role in join.Roles)
            {
                message.Roles.Add(role);
            }
            return message.ToByteArray();
        }

        private InternalClusterAction.Join JoinFrom(byte[] bytes)
        {
            var join = Proto.Msg.Join.Parser.ParseFrom(bytes);
            var builder = ImmutableHashSet<string>.Empty.ToBuilder(); // TODO: should not be an immutable collection
            foreach (var role in join.Roles)
            {
                builder.Add(role);
            }
            return new InternalClusterAction.Join(UniqueAddressFrom(join.Node), builder.ToImmutable());
        }

        private byte[] WelcomeMessageBuilder(InternalClusterAction.Welcome welcome)
        {
            var welcomeProto = new Proto.Msg.Welcome();
            welcomeProto.From = UniqueAddressToProto(welcome.From);
            welcomeProto.Gossip = GossipToProto(welcome.Gossip);
            return welcomeProto.ToByteArray();
        }

        private static InternalClusterAction.Welcome WelcomeFrom(Proto.Msg.Welcome welcomeProto)
        {
            return new InternalClusterAction.Welcome(
                UniqueAddressFrom(welcomeProto.From),
                GossipFrom(welcomeProto.Gossip));
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
            var message = new Proto.Msg.GossipStatus();
            message.From = UniqueAddressToProto(gossipStatus.From);
            message.Version = VectorClockToProto(gossipStatus.Version);
            return message.ToByteArray();
        }

        private static GossipStatus GossipStatusFrom(byte[] bytes)
        {
            var gossipStatusProto = Proto.Msg.GossipStatus.Parser.ParseFrom(bytes);
            return new GossipStatus(UniqueAddressFrom(gossipStatusProto.From), VectorClockFrom(gossipStatusProto.Version));
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

        private static Proto.Msg.ClusterRouterPoolSettings ClusterRouterPoolSettingsToProto(Routing.ClusterRouterPoolSettings clusterRouterPoolSettings)
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

        private static string GetObjectManifest(Serializer serializer, object obj)
        {
            var manifestSerializer = serializer as SerializerWithStringManifest;
            if (manifestSerializer != null)
            {
                return manifestSerializer.Manifest(obj);
            }

            return obj.GetType().TypeQualifiedName();
        }

        //
        // Gossip
        //

        private static Proto.Msg.Gossip GossipToProto(Gossip gossip)
        {
            var message = new Proto.Msg.Gossip();

            foreach (var member in gossip.Members)
            {
                var protoMember = new Proto.Msg.Member();
                protoMember.UniqueAddress = UniqueAddressToProto(member.UniqueAddress);
                protoMember.UpNumber = member.UpNumber;
                protoMember.Status = (Proto.Msg.Member.Types.MemberStatus)member.Status;

                foreach (var role in member.Roles)
                {
                    protoMember.Roles.Add(role);
                }

                message.Members.Add(protoMember);
            }

            message.Overview = new Proto.Msg.GossipOverview();
            foreach (var seen in gossip.Overview.Seen)
            {
                message.Overview.Seen.Add(UniqueAddressToProto(seen));
            }

            message.Overview.Reachability = new Proto.Msg.Reachability();
            foreach (var record in gossip.Overview.Reachability.Records)
            {
                var protoRecord = new Proto.Msg.Record();
                protoRecord.Observer = UniqueAddressToProto(record.Observer);
                protoRecord.Subject = UniqueAddressToProto(record.Subject);
                protoRecord.Status = (Proto.Msg.Record.Types.ReachabilityStatus)record.Status;
                protoRecord.Version = record.Version;
                message.Overview.Reachability.Records.Add(protoRecord);
            }

            foreach (var version in gossip.Overview.Reachability.Versions)
            {
                var reachabilityVersion = new Proto.Msg.Reachability.Types.ReachabilityVersion();
                reachabilityVersion.UniqueAddress = UniqueAddressToProto(version.Key);
                reachabilityVersion.Version = version.Value;
                message.Overview.Reachability.Versions.Add(reachabilityVersion);
            }

            message.Version = VectorClockToProto(gossip.Version);

            return message;
        }

        private static Gossip GossipFrom(Proto.Msg.Gossip gossipProto)
        {
            var members = new SortedSet<Member>();
            foreach (var protoMember in gossipProto.Members)
            {
                var roles = new HashSet<string>();

                foreach (var role in protoMember.Roles)
                {
                    roles.Add(role);
                }

                var member = new Member(
                    UniqueAddressFrom(protoMember.UniqueAddress),
                    protoMember.UpNumber,
                    (MemberStatus)protoMember.Status,
                    roles.ToImmutableHashSet());
                members.Add(member);
            }

            var seens = new HashSet<UniqueAddress>();
            foreach (var protoSeen in gossipProto.Overview.Seen)
            {
                seens.Add(UniqueAddressFrom(protoSeen));
            }

            var records = new List<Reachability.Record>();
            foreach (var protoRecord in gossipProto.Overview.Reachability.Records)
            {
                var record = new Reachability.Record(
                    UniqueAddressFrom(protoRecord.Observer),
                    UniqueAddressFrom(protoRecord.Subject),
                    (Reachability.ReachabilityStatus)protoRecord.Status,
                    protoRecord.Version);

                records.Add(record);
            }

            var versions = new Dictionary<UniqueAddress, long>();
            foreach (var protoVersion in gossipProto.Overview.Reachability.Versions)
            {
                versions.Add(UniqueAddressFrom(protoVersion.UniqueAddress), protoVersion.Version);
            }

            var reachability = new Reachability(records.ToImmutableList(), versions.ToImmutableDictionary());

            var gossipOverview = new GossipOverview(seens.ToImmutableHashSet(), reachability);
            var version = VectorClockFrom(gossipProto.Version);
            return new Gossip(members.ToImmutableSortedSet(), gossipOverview, version);
        }

        private static Proto.Msg.VectorClock VectorClockToProto(VectorClock vectorClock)
        {
            var message = new Proto.Msg.VectorClock();

            foreach (var version in vectorClock.Versions)
            {
                var versionProto = new Proto.Msg.VectorClock.Types.Version();
                versionProto.Node = version.Key.ToString();
                versionProto.Timestamp = version.Value;
                message.Versions.Add(versionProto);
            }

            return message;
        }

        private static VectorClock VectorClockFrom(Proto.Msg.VectorClock vectorClockProto)
        {
            var versions = new SortedDictionary<VectorClock.Node, long>();
            foreach (var versionProto in vectorClockProto.Versions)
            {
                versions.Add(new VectorClock.Node(versionProto.Node), versionProto.Timestamp);
            }

            return VectorClock.Create(versions.ToImmutableSortedDictionary());
        }

        //
        // Address
        //

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Proto.Msg.Address AddressToProto(Actor.Address address)
        {
            var message = new Proto.Msg.Address();
            message.System = address.System;
            message.Hostname = address.Host;
            message.Port = (uint)(address.Port ?? 0);
            message.Protocol = address.Protocol;
            return message;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Address AddressFrom(Proto.Msg.Address addressProto)
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
    }
}