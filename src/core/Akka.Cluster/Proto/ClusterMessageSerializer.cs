using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util.Internal;
using Google.ProtocolBuffers;

namespace Akka.Cluster.Proto
{
    /// <summary>
    /// Protobuff serializer for cluster messages
    /// </summary>
    internal class ClusterMessageSerializer : Serializer
    {
        public ClusterMessageSerializer(ExtendedActorSystem system)
            : base(system)
        {
            _gossipTimeToLive = new Lazy<TimeSpan>(() => Cluster.Get(system).Settings.GossipTimeToLive);
        }

        private const int BufferSize = 1024 * 4;

        public override int Identifier
        {
            get { return 5; }
        }

        public override bool IncludeManifest
        {
            get { return true; }
        }

        //must be lazy because serializer is initialized from Cluster extension constructor
        private Lazy<TimeSpan> _gossipTimeToLive;

        public override byte[] ToBinary(object obj)
        {
            if (obj is ClusterHeartbeatSender.Heartbeat) return AddressToProtoByteArray(((ClusterHeartbeatSender.Heartbeat)obj).From);
            if (obj is ClusterHeartbeatSender.HeartbeatRsp) return UniqueAddressToProtoByteArray(((ClusterHeartbeatSender.HeartbeatRsp)obj).From);
            if (obj is GossipEnvelope) return GossipEnvelopeToProto((GossipEnvelope) obj).ToByteArray();
            if (obj is GossipStatus) return GossipStatusToProto((GossipStatus) obj).ToByteArray();
            if (obj is MetricsGossipEnvelope) return Compress(MetricsGossipEnvelopeToProto((MetricsGossipEnvelope)obj));
            if (obj is InternalClusterAction.Join)
            {
                var join = (InternalClusterAction.Join) obj;
                return JoinToProto(join.Node, join.Roles).ToByteArray();
            }
            if (obj is InternalClusterAction.Welcome)
            {
                var welcome = (InternalClusterAction.Welcome) obj;
                return Compress(WelcomeToProto(welcome.From, welcome.Gossip));
            }
            if (obj is ClusterUserAction.Leave) return AddressToProtoByteArray(((ClusterUserAction.Leave) obj).Address);
            if (obj is ClusterUserAction.Down) return AddressToProtoByteArray(((ClusterUserAction.Down)obj).Address);
            if (obj is InternalClusterAction.InitJoin) return Msg.Empty.DefaultInstance.ToByteArray();
            if (obj is InternalClusterAction.InitJoinAck) return AddressToProtoByteArray(((InternalClusterAction.InitJoinAck)obj).Address);
            if (obj is InternalClusterAction.InitJoinNack) return AddressToProtoByteArray(((InternalClusterAction.InitJoinNack)obj).Address);
            throw new ArgumentException(string.Format("Can't serialize object of type {0}", obj.GetType()));
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof (InternalClusterAction.Join))
            {
                var m = Msg.Join.ParseFrom(bytes);
                return new InternalClusterAction.Join(UniqueAddressFromProto(m.Node),
                    ImmutableHashSet.Create<string>(m.RolesList.ToArray()));
            }

            if (type == typeof(InternalClusterAction.Welcome))
            {
                var m = Msg.Welcome.ParseFrom(Decompress(bytes));
                return new InternalClusterAction.Welcome(UniqueAddressFromProto(m.From), GossipFromProto(m.Gossip));
            }

            if (type == typeof(ClusterUserAction.Leave)) return new ClusterUserAction.Leave(AddressFromBinary(bytes));
            if (type == typeof(ClusterUserAction.Down)) return new ClusterUserAction.Down(AddressFromBinary(bytes));
            if (type == typeof(InternalClusterAction.InitJoin)) return new InternalClusterAction.InitJoin();
            if (type == typeof(InternalClusterAction.InitJoinAck)) return new InternalClusterAction.InitJoinAck(AddressFromBinary(bytes));
            if (type == typeof(InternalClusterAction.InitJoinNack)) return new InternalClusterAction.InitJoinNack(AddressFromBinary(bytes));
            if (type == typeof(ClusterHeartbeatSender.Heartbeat)) return new ClusterHeartbeatSender.Heartbeat(AddressFromBinary(bytes));
            if (type == typeof(ClusterHeartbeatSender.HeartbeatRsp)) return new ClusterHeartbeatSender.HeartbeatRsp(UniqueAddressFromBinary(bytes));
            if (type == typeof(GossipStatus)) return GossipStatusFromBinary(bytes);
            if (type == typeof(GossipEnvelope)) return GossipEnvelopeFromBinary(bytes);
            if (type == typeof (MetricsGossipEnvelope)) return MetricsGossipEnvelopeFromBinary(bytes);

            throw new ArgumentException("Ned a cluster message class to be able to deserialize bytes in ClusterSerializer.");
        }

        /// <summary>
        /// Compresses the protobuf message using GZIP compression
        /// </summary>
        public byte[] Compress(IMessageLite message)
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
        public byte[] Decompress(byte[] bytes)
        {
            using(var input = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var buffer = new byte[BufferSize];
                var bytesRead = input.Read(buffer, 0, BufferSize);
                while (bytesRead > 0)
                {
                    output.Write(buffer,0,bytesRead);
                    bytesRead = input.Read(buffer, 0, BufferSize);
                }
                return output.ToArray();
            }
        }

        #region Private internals

        // we don't care about races here since it's just a cache
        private volatile string _protocolCache = null;
        private volatile string _systemCache = null;

        private Address AddressFromBinary(byte[] bytes)
        {
            return AddressFromProto(Msg.Address.ParseFrom(bytes));
        }

        private UniqueAddress UniqueAddressFromBinary(byte[] bytes)
        {
            return UniqueAddressFromProto(Msg.UniqueAddress.ParseFrom(bytes));
        }

        private Address AddressFromProto(Msg.Address address)
        {
            return new Address(GetProtocol(address), GetSystem(address), address.Hostname, GetPort(address));
        }

        private Msg.Address.Builder AddressToProto(Address address)
        {
            if(string.IsNullOrEmpty(address.Host) || !address.Port.HasValue) 
                throw new ArgumentException(string.Format("Address [{0}] could not be serialized: host or port missing.", address), "address");
            return
                Msg.Address.CreateBuilder()
                    .SetSystem(address.System)
                    .SetProtocol(address.Protocol)
                    .SetHostname(address.Host)
                    .SetPort((uint)address.Port.Value);
        }

        private byte[] AddressToProtoByteArray(Address address)
        {
            return AddressToProto(address).Build().ToByteArray();
        }

        private UniqueAddress UniqueAddressFromProto(Msg.UniqueAddress uniqueAddress)
        {
            return new UniqueAddress(AddressFromProto(uniqueAddress.Address), (int)uniqueAddress.Uid);
        }

        private Msg.UniqueAddress.Builder UniqueAddressToProto(UniqueAddress uniqueAddress)
        {
            return
                Msg.UniqueAddress.CreateBuilder()
                    .SetAddress(AddressToProto(uniqueAddress.Address))
                    .SetUid((uint) uniqueAddress.Uid);
        }

        private byte[] UniqueAddressToProtoByteArray(UniqueAddress uniqueAddress)
        {
            return UniqueAddressToProto(uniqueAddress).Build().ToByteArray();
        }

        private string GetProtocol(Msg.Address address)
        {
            var p = address.Protocol;
            var pc = _protocolCache;
            if (pc == p) return pc;

            _protocolCache = p;
            return p;
        }

        private string GetSystem(Msg.Address address)
        {
            var s = address.System;
            var sc = _systemCache;
            if (sc == s) return sc;

            _systemCache = s;
            return s;
        }

        private int? GetPort(Msg.Address address)
        {
            if (!address.HasPort) return null;
            return (int)address.Port;
        }

        // ReSharper disable once InconsistentNaming
        private readonly Dictionary<MemberStatus, Msg.MemberStatus> MemberStatusToProto
            = new Dictionary<MemberStatus, Msg.MemberStatus>()
            {
                {MemberStatus.Joining, Msg.MemberStatus.Joining},
                {MemberStatus.Up, Msg.MemberStatus.Up},
                {MemberStatus.Leaving, Msg.MemberStatus.Leaving},
                {MemberStatus.Exiting, Msg.MemberStatus.Exiting},
                {MemberStatus.Down, Msg.MemberStatus.Down},
                {MemberStatus.Removed, Msg.MemberStatus.Removed}
            };

        private Dictionary<Msg.MemberStatus, MemberStatus> _memberStatusFromProtoCache = null;

        private Dictionary<Msg.MemberStatus, MemberStatus> MemberStatusFromProto
        {
            get
            {
                return _memberStatusFromProtoCache ??
                       (_memberStatusFromProtoCache = MemberStatusToProto.ToDictionary(pair => pair.Value,
                           pair => pair.Key));
            }
        }

        // ReSharper disable once InconsistentNaming
        private readonly Dictionary<Reachability.ReachabilityStatus, Msg.ReachabilityStatus> ReachabilityStatusToProto
            = new Dictionary<Reachability.ReachabilityStatus, Msg.ReachabilityStatus>()
            {
                { Reachability.ReachabilityStatus.Reachable, Msg.ReachabilityStatus.Reachable },
                { Reachability.ReachabilityStatus.Terminated, Msg.ReachabilityStatus.Terminated},
                { Reachability.ReachabilityStatus.Unreachable, Msg.ReachabilityStatus.Unreachable }
            };

        private Dictionary<Msg.ReachabilityStatus, Reachability.ReachabilityStatus> _reachabilityStatusFromProtoCache = null;
        private Dictionary<Msg.ReachabilityStatus, Reachability.ReachabilityStatus> ReachabilityStatusFromProto
        {
            get
            {
                return _reachabilityStatusFromProtoCache ??
                       (_reachabilityStatusFromProtoCache = ReachabilityStatusToProto.ToDictionary(pair => pair.Value,
                           pair => pair.Key));
            }
        }

        private int MapWithErrorMessage<T>(Dictionary<T, int> map, T value, string unknown)
        {
            if (map.ContainsKey(value)) return map[value];
            throw new ArgumentException(string.Format("Unknown {0} [{1}] in cluster message", unknown, value));
        }

        private Msg.Join JoinToProto(UniqueAddress node, ImmutableHashSet<string> roles)
        {
            return Msg.Join.CreateBuilder().SetNode(UniqueAddressToProto(node)).AddRangeRoles(roles).Build();
        }

        private Msg.Welcome WelcomeToProto(UniqueAddress node, Gossip gossip)
        {
            return Msg.Welcome.CreateBuilder().SetFrom(UniqueAddressToProto(node))
                .SetGossip(GossipToProto(gossip)).Build();
        }

        private Msg.Gossip.Builder GossipToProto(Gossip gossip)
        {
            var allMembers = gossip.Members.ToList();
            var allAddresses = gossip.Members.Select(x => x.UniqueAddress).ToList();
            var addressMapping = allAddresses.ZipWithIndex();
            var allRoles = allMembers.Aggregate(ImmutableHashSet.Create<string>(),
                (set, member) => set.Union(member.Roles));
            var roleMapping = allRoles.ZipWithIndex();
            var allHashes = gossip.Version.Versions.Keys.Select(x => x.ToString()).ToList();
            var hashMapping = allHashes.ZipWithIndex();

            Func<UniqueAddress, int> mapUniqueAddress =
                address => MapWithErrorMessage(addressMapping, address, "address");

            Func<string, int> mapRole = s => MapWithErrorMessage(roleMapping, s, "role");

            Func<Member, Msg.Member.Builder> memberToProto = member => Msg.Member.CreateBuilder().SetAddressIndex(mapUniqueAddress(member.UniqueAddress))
                .SetUpNumber(member.UpNumber)
                .SetStatus(MemberStatusToProto[member.Status])
                .AddRangeRolesIndexes(member.Roles.Select(mapRole));

            Func<Reachability, IEnumerable<Msg.ObserverReachability>> reachabilityToProto = reachability =>
            {
                var builderList = new List<Msg.ObserverReachability>();
                foreach (var version in reachability.Versions)
                {
                    var subjectReachability = reachability.RecordsFrom(version.Key).Select(
                        r => Msg.SubjectReachability.CreateBuilder().SetAddressIndex(mapUniqueAddress(r.Subject))
                            .SetStatus(ReachabilityStatusToProto[r.Status]).SetVersion(r.Version).Build());
                    builderList.Add(Msg.ObserverReachability.CreateBuilder()
                        .SetAddressIndex(mapUniqueAddress(version.Key))
                        .SetVersion(version.Value).AddRangeSubjectReachability(subjectReachability).Build());
                }
                return builderList;
            };

            var reachabilityProto = reachabilityToProto(gossip.Overview.Reachability);
            var membersProto = gossip.Members.Select(memberToProto);
            var seenProto = gossip.Overview.Seen.Select(mapUniqueAddress);

            var overview = Msg.GossipOverview.CreateBuilder().AddRangeSeen(seenProto)
                .AddRangeObserverReachability(reachabilityProto);

            return Msg.Gossip.CreateBuilder()
                .AddRangeAllAddresses(allAddresses.Select(x => UniqueAddressToProto(x).Build()))
                .AddRangeAllRoles(allRoles)
                .AddRangeAllHashes(allHashes.Select(y => y))
                .AddRangeMembers(membersProto.Select(x => x.Build()))
                .SetOverview(overview).SetVersion(VectorClockToProto(gossip.Version, hashMapping));
        }

        private Msg.MetricsGossipEnvelope MetricsGossipEnvelopeToProto(MetricsGossipEnvelope envelope)
        {
            var mgossip = envelope.Gossip;
            var allAddresses = mgossip.Nodes.Select(x => x.Address).ToList();
            var addressMapping = allAddresses.ZipWithIndex();
            var allMetricNames = mgossip.Nodes.Aggregate(ImmutableHashSet.Create<string>(),
                (set, metrics) => set = set.Union(metrics.Metrics.Select(x => x.Name)));
            var metricNamesMapping = allMetricNames.ZipWithIndex();

            Func<Address, int> mapAddress = address => MapWithErrorMessage(addressMapping, address, "address");
            Func<string, int> mapName = name => MapWithErrorMessage(metricNamesMapping, name, "address");

            Func<EWMA, Msg.NodeMetrics.Types.EWMA.Builder> ewmaToProto = ewma => ewma == null ? null :
                Msg.NodeMetrics.Types.EWMA.CreateBuilder().SetAlpha(ewma.Alpha).SetValue(ewma.Value);

            // we set all metric types as doubles, since we don't have a convenienent Number base class like Scala
            Func<double, Msg.NodeMetrics.Types.Number.Builder> numberToProto = d => Msg.NodeMetrics.Types.Number.CreateBuilder()
                .SetType(Msg.NodeMetrics.Types.NumberType.Double)
                .SetValue64((ulong) BitConverter.DoubleToInt64Bits(d));

            Func<Metric, Msg.NodeMetrics.Types.Metric.Builder> metricToProto = metric =>
            {
                var builder =
                    Msg.NodeMetrics.Types.Metric.CreateBuilder()
                        .SetNameIndex(mapName(metric.Name))
                        .SetNumber(numberToProto(metric.Value));
                var ewmaBuilder = ewmaToProto(metric.Average);
                return ewmaBuilder != null ? builder.SetEwma(ewmaBuilder) : builder;
            };

            Func<NodeMetrics, Msg.NodeMetrics.Builder> nodeMetricsToProto = metrics => Msg.NodeMetrics.CreateBuilder()
                .SetAddressIndex(mapAddress(metrics.Address))
                .SetTimestamp(metrics.Timestamp)
                .AddRangeMetrics(metrics.Metrics.Select(x => metricToProto(x).Build()));

            var nodeMetrics = mgossip.Nodes.Select(x => nodeMetricsToProto(x).Build());

            return Msg.MetricsGossipEnvelope.CreateBuilder().SetFrom(AddressToProto(envelope.From)).SetGossip(
                Msg.MetricsGossip.CreateBuilder()
                    .AddRangeAllAddresses(allAddresses.Select(x => AddressToProto(x).Build()))
                    .AddRangeAllMetricNames(allMetricNames).AddRangeNodeMetrics(nodeMetrics))
                .SetReply(envelope.Reply)
                .Build();
        }

        private MetricsGossipEnvelope MetricsGossipEnvelopeFromProto(Msg.MetricsGossipEnvelope envelope)
        {
            var mgossip = envelope.Gossip;
            var addressMapping = mgossip.AllAddressesList.Select(AddressFromProto).ToList();
            var metricNameMapping = mgossip.AllMetricNamesList;

            Func<Msg.NodeMetrics.Types.EWMA, EWMA> ewmaFromProto = ewma => ewma == null ? null : new EWMA(ewma.Value, ewma.Alpha);
            Func<Msg.NodeMetrics.Types.Number, double> numberFromProto =
                number => BitConverter.Int64BitsToDouble((long) number.Value64);
            Func<Msg.NodeMetrics.Types.Metric, Metric> metricFromProto =
                metric =>
                    new Metric(metricNameMapping[metric.NameIndex], numberFromProto(metric.Number),
                        ewmaFromProto(metric.Ewma));
            Func<Msg.NodeMetrics, NodeMetrics> nodeMetricsFromProto = metrics => new NodeMetrics(addressMapping[metrics.AddressIndex], metrics.Timestamp, metrics.MetricsList.Select(metricFromProto).ToImmutableHashSet());

            var nodeMetrics = mgossip.NodeMetricsList.Select(nodeMetricsFromProto).ToImmutableHashSet();

            return new MetricsGossipEnvelope(AddressFromProto(envelope.From), new MetricsGossip(nodeMetrics), envelope.Reply);
        }

        private MetricsGossipEnvelope MetricsGossipEnvelopeFromBinary(byte[] bytes)
        {
            return MetricsGossipEnvelopeFromProto(Msg.MetricsGossipEnvelope.ParseFrom(Decompress(bytes)));
        }

        private Msg.VectorClock.Builder VectorClockToProto(VectorClock version, Dictionary<string, int> hashMapping)
        {
            var versions = version.Versions.Select(pair =>
                Msg.VectorClock.Types.Version.CreateBuilder()
                    .SetHashIndex(MapWithErrorMessage(hashMapping, pair.Key.ToString(), "hash"))
                    .SetTimestamp(pair.Value).Build());

            return Msg.VectorClock.CreateBuilder().SetTimestamp(0).AddRangeVersions(versions);
        }

        private Msg.GossipEnvelope GossipEnvelopeToProto(GossipEnvelope gossipEnvelope)
        {
            return Msg.GossipEnvelope.CreateBuilder()
                .SetFrom(UniqueAddressToProto(gossipEnvelope.From))
                .SetTo(UniqueAddressToProto(gossipEnvelope.To))
                .SetSerializedGossip(ByteString.CopyFrom(Compress(GossipToProto(gossipEnvelope.Gossip).Build())))
                .Build();
        }

        private GossipEnvelope GossipEnvelopeFromProto(Msg.GossipEnvelope gossipEnvelope)
        {
            var serializedGossip = gossipEnvelope.SerializedGossip;
            return new GossipEnvelope(UniqueAddressFromProto(gossipEnvelope.From), 
                UniqueAddressFromProto(gossipEnvelope.To),
                GossipFromProto(Msg.Gossip.ParseFrom(Decompress(serializedGossip.ToByteArray()))));
        }

        private Msg.GossipStatus GossipStatusToProto(GossipStatus gossipStatus)
        {
            var allHashes = gossipStatus.Version.Versions.Keys.Select(x => x.ToString()).ToList();
            var hashMapping = allHashes.ZipWithIndex();
            return Msg.GossipStatus.CreateBuilder().SetFrom(UniqueAddressToProto(gossipStatus.From))
                .AddRangeAllHashes(allHashes).SetVersion(VectorClockToProto(gossipStatus.Version, hashMapping)).Build();
        }

        private Gossip GossipFromProto(Msg.Gossip gossip)
        {
            var addressMapping = gossip.AllAddressesList.Select(UniqueAddressFromProto).ToList();
            var roleMapping = gossip.AllRolesList.ToList();
            var hashMapping = gossip.AllHashesList.ToList();

            Func<IEnumerable<Msg.ObserverReachability>, Reachability> reachabilityFromProto = reachabilityProto =>
            {
                var recordBuilder = ImmutableList.CreateBuilder<Reachability.Record>();
                var versionsBuilder = ImmutableDictionary.CreateBuilder<UniqueAddress, long>();
                foreach (var o in reachabilityProto)
                {
                    var observer = addressMapping[o.AddressIndex];
                    versionsBuilder.Add(observer, o.Version);
                    foreach (var s in o.SubjectReachabilityList)
                    {
                        var subject = addressMapping[s.AddressIndex];
                        var record = new Reachability.Record(observer, subject, ReachabilityStatusFromProto[s.Status],
                            s.Version);
                        recordBuilder.Add(record);
                    }
                }

                return new Reachability(recordBuilder.ToImmutable(), versionsBuilder.ToImmutable());
            };

            Func<Msg.Member, Member> memberFromProto = member => Member.Create(addressMapping[member.AddressIndex], MemberStatusFromProto[member.Status],
                member.RolesIndexesList.Select(x => roleMapping[x]).ToImmutableHashSet());

            var members = gossip.MembersList.Select(memberFromProto).ToImmutableSortedSet(Member.Ordering);
            var reachability = reachabilityFromProto(gossip.Overview.ObserverReachabilityList);
            var seen = gossip.Overview.SeenList.Select(x => addressMapping[x]).ToImmutableHashSet();
            var overview = new GossipOverview(seen, reachability);

            return new Gossip(members, overview, VectorClockFromProto(gossip.Version, hashMapping));
        }

        private GossipStatus GossipStatusFromProto(Msg.GossipStatus status)
        {
            return new GossipStatus(UniqueAddressFromProto(status.From), VectorClockFromProto(status.Version, status.AllHashesList));
        }

        private VectorClock VectorClockFromProto(Msg.VectorClock version, IList<string> hashMapping)
        {
            return
                VectorClock.Create(
                    version.VersionsList.ToImmutableSortedDictionary(version1 => VectorClock.Node.FromHash(hashMapping[version1.HashIndex]),
                        version1 => version1.Timestamp));
        }

        private GossipEnvelope GossipEnvelopeFromBinary(byte[] bytes)
        {
            return GossipEnvelopeFromProto(Msg.GossipEnvelope.ParseFrom(bytes));
        }

        private GossipStatus GossipStatusFromBinary(byte[] bytes)
        {
            return GossipStatusFromProto(Msg.GossipStatus.ParseFrom(bytes));
        }

        #endregion
    }
}
