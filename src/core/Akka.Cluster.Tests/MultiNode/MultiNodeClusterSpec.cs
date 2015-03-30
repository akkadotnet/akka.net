using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using Xunit;

namespace Akka.Cluster.Tests.MultiNode
{
    //TODO: WatchedByCoroner?
    //@Aaronontheweb: Coroner is a JVM-specific instrument used to report deadlocks and other fun stuff.
    //can probably skip for now.
    public abstract class MultiNodeClusterSpec : MultiNodeSpec
    {
        public static Config ClusterConfigWithFailureDetectorPuppet()
        {
            return ConfigurationFactory.ParseString(
                @"akka.cluster.failure-detector.implementation-class = ""Akka.Cluster.Tests.FailureDetectorPuppet, Akka.Cluster.Tests""")
                .WithFallback(ClusterConfig());
        }

        public static Config ClusterConfig(bool failureDetectorPuppet)
        {
            return failureDetectorPuppet ? ClusterConfigWithFailureDetectorPuppet() : ClusterConfig();
        }

        public static Config ClusterConfig()
        {
            return ConfigurationFactory.ParseString(@"
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.cluster {
                    gossip-interval                     = 200 ms
                    leader-actions-interval             = 200 ms
                    unreachable-nodes-reaper-interval   = 500 ms
                    periodic-tasks-initial-delay        = 300 ms
                    publish-stats-interval              = 0 s # always, when it happens
                    failure-detector.heartbeat-interval = 500 ms
                }
                akka.loglevel = INFO
                akka.log-dead-letters = off
                akka.log-dead-letters-during-shutdown = off
                #akka.remote.log-remote-lifecycle-events = off
                #akka.loggers = [""Akka.TestKit.TestEventListener, Akka.TestKit""]
                akka.test {
                    single-expect-default = 15 s
                }
            ");
        }


        public class EndActor : UntypedActor
        {

            // sometimes we need to coordinate test shutdown with messages instead of barriers
            public sealed class SendEnd
            {
                private SendEnd() { }
                private static readonly SendEnd _instance = new SendEnd();
                public static SendEnd Instance
                {
                    get
                    {
                        return _instance;
                    }
                }
            }

            public sealed class End
            {
                private End() { }
                private static readonly End _instance = new End();
                public static End Instance
                {
                    get
                    {
                        return _instance;
                    }
                }
            }

            public sealed class EndAck
            {
                private EndAck() { }
                private static readonly EndAck _instance = new EndAck();
                public static EndAck Instance
                {
                    get
                    {
                        return _instance;
                    }
                }
            }

            readonly IActorRef _testActor;
            readonly Address _target;

            public EndActor(IActorRef testActor, Address target)
            {
                _testActor = testActor;
                _target = target;
            }

            protected override void OnReceive(object message)
            {
                if (message is SendEnd)
                {
                    if (_target != null)
                        Context.ActorSelection(new RootActorPath(_target) / Self.Path.Elements).Tell(End.Instance);
                    return;
                }
                if (message is End)
                {
                    _testActor.Forward(End.Instance);
                    Sender.Tell(EndAck.Instance);
                    return;
                }
                if (message is EndAck)
                {
                    _testActor.Forward(EndAck.Instance);
                }
            }
        }

        readonly TestKitAssertions _assertions;

        protected MultiNodeClusterSpec(MultiNodeConfig config)
            : base(config)
        {
            _assertions = new XunitAssertions();
            _roleNameComparer = new RoleNameComparer(this);
        }

        protected override int InitialParticipantsValueFactory
        {
            get { return Roles.Count; }
        }

        readonly ConcurrentDictionary<RoleName, Address> _cachedAddresses =
            new ConcurrentDictionary<RoleName, Address>();

        protected override void AtStartup()
        {
            MuteLog(Sys);
        }


        protected override void AfterTermination()
        {
        }

        //TODO: ExpectedTestDuration?

        void MuteLog(ActorSystem sys = null)
        {
            if (sys == null) sys = Sys;
            if (!sys.Log.IsDebugEnabled)
            {
                var patterns = new[]
                {
                    ".*Metrics collection has started successfully.*",
                    ".*Cluster Node.* - is starting up.*",
                    ".*Shutting down cluster Node.*",
                    ".*Cluster node successfully shut down.*",
                    ".*Using a dedicated scheduler for cluster.*"
                };

                foreach (var pattern in patterns)
                    EventFilter.Info(new Regex(pattern)).Mute();

                MuteDeadLetters(sys, 
                    typeof(ClusterHeartbeatSender.Heartbeat),
                    typeof(ClusterHeartbeatSender.HeartbeatRsp),
                    typeof(GossipEnvelope),
                    typeof(GossipStatus), 
                    typeof(GossipStatus),
                    typeof(MetricsGossipEnvelope),
                    typeof(ClusterEvent.ClusterMetricsChanged),
                    typeof(InternalClusterAction.ITick),
                    typeof(PoisonPill),
                    typeof(DeathWatchNotification),
                    typeof(Disassociated),
                    typeof(DisassociateUnderlying),
                    typeof(InboundPayload));
            }
        }

        protected void MuteMarkingAsUnreachable(ActorSystem system = null)
        {
            var sys = system ?? Sys;
            if (!sys.Log.IsDebugEnabled)
                EventFilter.Error(new Regex(".*Marking.* as UNREACHABLE.*")).Mute();
        }

        protected void MuteMarkingAsReachable(ActorSystem system = null)
        {
            var sys = system ?? Sys;
            if (!sys.Log.IsDebugEnabled)
                EventFilter.Info(new Regex(".*Marking.* as REACHABLE.*")).Mute();
        }

        public Address GetAddress(RoleName role)
        {
            Address address;
            if (!_cachedAddresses.TryGetValue(role, out address))
            {
                address = Node(role).Address;
                _cachedAddresses.TryAdd(role, address);
            }
            return address;
        }

        public ClusterReadView ClusterView { get { return Cluster.ReadView; } }

        /// <summary>
        /// Get the cluster node to use.
        /// </summary>
        public Cluster Cluster { get { return Cluster.Get(Sys); } }

        /// <summary>
        /// Use this method for the initial startup of the cluster node
        /// </summary>
        public void StartClusterNode()
        {
            if (ClusterView.Members.IsEmpty)
            {
                Cluster.Join(GetAddress(Myself));
                AwaitAssert(() => Assert.True(ClusterView.Members.Select(m => m.Address).Contains(GetAddress(Myself))));
            }
        }

        /// <summary>
        /// Initialize the cluster of the specified member nodes (<see cref="roles"/>)
        /// and wait until all joined and <see cref="MemberStatus.Up"/>.
        /// 
        /// First node will be started firat and others will join the first.
        /// </summary>
        public void AwaitClusterUp(params RoleName[] roles)
        {
            // make sure that the node-to-join is started before other join
            RunOn(StartClusterNode, roles.First());

            EnterBarrier(roles.First().Name + "-started");
            if (roles.Skip(1).Contains(Myself)) Cluster.Join(GetAddress(roles.First()));

            if (roles.Contains(Myself))
            {
                AwaitMembersUp(roles.Length);
            }
            EnterBarrier(roles.Select(r => r.Name).Aggregate((a, b) => a + "-" + b) + "-joined");
        }

        public void JoinWithin(RoleName joinNode, TimeSpan? max = null, TimeSpan? interval = null)
        {
            if (max == null) max = RemainingOrDefault;
            if (interval == null) interval = TimeSpan.FromSeconds(1);

            Cluster.Join(GetAddress(joinNode));
            AwaitCondition(() =>
            {
                ClusterView.RefreshCurrentState();
                if (MemberInState(GetAddress(joinNode), new[] { MemberStatus.Up }) &&
                    MemberInState(GetAddress(Myself), new[] { MemberStatus.Joining, MemberStatus.Up }))
                    return true;

                Cluster.Join(GetAddress(joinNode));
                return false;
            }, max, interval);

        }

        private bool MemberInState(Address member, IEnumerable<MemberStatus> status)
        {
            return ClusterView.Members.Any(m => m.Address == member && status.Contains(m.Status));
        }

        /// <summary>
        /// Assert that the
        /// member addresses match the expected addresses in the
        /// sort order used by the cluster.
        /// </summary>
        public void AssertMembers(IEnumerable<Member> gotMembers, params Address[] expectedAddresses)
        {
            var members = gotMembers.ToImmutableList();
            _assertions.AssertEqual(expectedAddresses.Length, members.Count);
            expectedAddresses.ToImmutableSortedSet(Member.AddressOrdering);
            for (var i = 0; i < expectedAddresses.Length; i++)
            {
                _assertions.AssertEqual(expectedAddresses[i], members[i].Address);
            }
        }

        /// <summary>
        /// Note that this can only be used for a cluster with all members
        /// in Up status, i.e. use `awaitMembersUp` before using this method.
        /// The reason for that is that the cluster leader is preferably a
        /// member with status Up or Leaving and that information can't
        /// be determined from the `RoleName`.
        /// </summary>
        public void AssertLeader(params RoleName[] nodesInCluster)
        {
            if (nodesInCluster.Contains(Myself)) AssertLeaderIn(nodesInCluster.ToImmutableList());
        }

        /// <summary>
        /// Assert that the cluster has elected the correct leader
        /// out of all nodes in the cluster. First
        /// member in the cluster ring is expected leader.
        ///   
        /// Note that this can only be used for a cluster with all members
        /// in Up status, i.e. use `awaitMembersUp` before using this method.
        /// The reason for that is that the cluster leader is preferably a
        /// member with status Up or Leaving and that information can't
        /// be determined from the `RoleName`.
        /// </summary>
        public void AssertLeaderIn(ImmutableList<RoleName> nodesInCluster)
        {
            if (!nodesInCluster.Contains(Myself)) return;

            _assertions.AssertTrue(nodesInCluster.Count != 0, "nodesInCluster must not be empty");
            var expectedLeader = RoleOfLeader(nodesInCluster);
            var leader = ClusterView.Leader;
            var isLeader = leader == ClusterView.SelfAddress;
            _assertions.AssertTrue(isLeader == IsNode(expectedLeader), "expected leader {0}, got leader {1}, members{2}", expectedLeader, leader, ClusterView.Members);
            _assertions.AssertTrue(ClusterView.Status == MemberStatus.Up ||
                                   ClusterView.Status == MemberStatus.Leaving,
                "Expected cluster view status Up or Leaving but got {0}", ClusterView.Status);
        }

        public void AwaitMembersUp(
            int numbersOfMembers,
            ImmutableHashSet<Address> canNotBePartOfMemberRing = null,
            TimeSpan? timeout = null)
        {
            if (canNotBePartOfMemberRing == null)
                canNotBePartOfMemberRing = ImmutableHashSet.Create<Address>();
            if (timeout == null) timeout = TimeSpan.FromSeconds(25);
            Within(timeout.Value, () =>
            {
                if (canNotBePartOfMemberRing.Any()) // don't run this on an empty set
                    AwaitAssert(() =>
                    {
                        foreach (var a in canNotBePartOfMemberRing)
                            ClusterView.Members.Select(m => m.Address).Contains(a).ShouldBeFalse();
                    });
                AwaitAssert(() => ClusterView.Members.Count.ShouldBe(numbersOfMembers));
                AwaitAssert(() => ClusterView.Members.All(m => m.Status == MemberStatus.Up).ShouldBeTrue("All members should be up"));
                // clusterView.leader is updated by LeaderChanged, await that to be updated also
                var firstMember = ClusterView.Members.FirstOrDefault();
                var expectedLeader = firstMember == null ? null : firstMember.Address;
                AwaitAssert(() => ClusterView.Leader.ShouldBe(expectedLeader));
            });
        }

        public void AwaitAllReachable()
        {
            AwaitAssert(() => _assertions.AssertFalse(ClusterView.UnreachableMembers.Any()));
        }

        public void AwaitSeenSameState(params Address[] addresses)
        {
            AwaitAssert(() => _assertions.AssertFalse(addresses.ToImmutableHashSet().Except(ClusterView.SeenBy).Any()));
        }

        /// <summary>
        /// Leader according to the address ordering of the roles.
        /// Note that this can only be used for a cluster with all members
        /// in Up status, i.e. use `awaitMembersUp` before using this method.
        /// The reason for that is that the cluster leader is preferably a
        /// member with status Up or Leaving and that information can't
        /// be determined from the `RoleName`.
        /// </summary>
        public RoleName RoleOfLeader(ImmutableList<RoleName> nodesInCluster)
        {
            if (nodesInCluster == null) nodesInCluster = Roles;
            _assertions.AssertFalse(nodesInCluster.Count == 0);
            return nodesInCluster.Sort(_roleNameComparer).First();
        }

        readonly RoleNameComparer _roleNameComparer;

        public class RoleNameComparer : IComparer<RoleName>
        {
            readonly MultiNodeClusterSpec _spec;

            public RoleNameComparer(MultiNodeClusterSpec spec)
            {
                _spec = spec;
            }

            public int Compare(RoleName x, RoleName y)
            {
                return Member.AddressOrdering.Compare(_spec.GetAddress(x), _spec.GetAddress(y));
            }
        }

        public RoleName RoleName(Address addr)
        {
            return Roles.FirstOrDefault(r => GetAddress(r) == addr);
        }

        /// <summary>
        /// Marks a node as available in the failure detector if
        /// <see cref="FailureDetectorPuppet"/> is used as
        /// failure detector
        /// </summary>
        public void MarkNodeAsAvailable(Address address)
        {
            var puppet = FailureDetectorPuppet(address);
            if (puppet != null) puppet.MarkNodeAsAvailable();
        }

        /// <summary>
        /// Marks a node as unavailable in the failure detector if
        /// <see cref="FailureDetectorPuppet"/> is used as
        /// failure detector
        /// </summary>
        public void MarkNodeAsUnavailable(Address address)
        {
            if (IsFailureDetectorPuppet())
            {
                // before marking it as unavailble there should be at least one heartbeat
                // to create the FailureDetectorPuppet in the FailureDetectorRegistry
                Cluster.FailureDetector.Heartbeat(address);
                var puppet = FailureDetectorPuppet(address);
                if (puppet != null) puppet.MarkNodeAsUnavailable();
            }
        }

        public bool IsFailureDetectorPuppet()
        {
            return Type.GetType(Cluster.Settings.FailureDetectorImplementationClass) == typeof (FailureDetectorPuppet);
        }

        public FailureDetectorPuppet FailureDetectorPuppet(Address address)
        {
            return (FailureDetectorPuppet)Cluster.FailureDetector.GetFailureDetector(address);
        }
    }
}
