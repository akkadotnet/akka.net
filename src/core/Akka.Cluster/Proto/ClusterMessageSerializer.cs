using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Util;
using Google.ProtocolBuffers;
using Akka.Actor;
using Akka.Serialization;

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
            throw new NotImplementedException();
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
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

        private Msg.Join JoinToProto(UniqueAddress node, HashSet<string> roles)
        {
            return Msg.Join.CreateBuilder().SetNode(UniqueAddressToProto(node)).AddRangeRoles(roles).Build();
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
                .AddRangeAllHashes(allHashes.Select(y => y.ToString()))
                .AddRangeMembers(membersProto.Select(x => x.Build()))
                .SetOverview(overview).SetVersion(VectorClockToProto(gossip.Version, hashMapping));
        }

        private Msg.VectorClock.Builder VectorClockToProto(VectorClock version, Dictionary<string, int> hashMapping)
        {
            var versions = version.Versions.Select(pair =>
                Msg.VectorClock.Types.Version.CreateBuilder()
                    .SetHashIndex(MapWithErrorMessage(hashMapping, pair.Key.ToString(), "hash"))
                    .SetTimestamp(pair.Value).Build());

            return Msg.VectorClock.CreateBuilder().SetTimestamp(0).AddRangeVersions(versions);
        }

        #endregion
    }
}
