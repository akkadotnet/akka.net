//-----------------------------------------------------------------------
// <copyright file="ClusterHeartBeatSenderStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Remote;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tests
{
    public class ClusterHeartBeatSenderStateSpec : ClusterSpecBase
    {
        public ClusterHeartBeatSenderStateSpec()
        {
            _emptyState = EmptyState(aa);
        }

        #region FailureDetectorStub

        /// <summary>
        /// Fake <see cref="FailureDetector"/> implementation used for testing cluster-wide failure detection.
        /// </summary>
        class FailureDetectorStub : FailureDetector
        {
            private enum Status
            {
                Up,
                Down,
                Unknown
            };

            private Status _status = Status.Unknown;

            public void MarkNodeAsUnavailable()
            {
                _status = Status.Down;
            }

            public void MarkNodeAsAvailable()
            {
                _status = Status.Up;
            }

            public override bool IsAvailable
            {
                get { return (_status == Status.Up || _status == Status.Unknown); }
            }

            public override bool IsMonitoring
            {
                get { return _status != Status.Unknown; }
            }

            public override void HeartBeat()
            {
                _status = Status.Up;
            }
        }

        #endregion

        private UniqueAddress aa = new UniqueAddress(new Address("akka.tcp", "sys", "aa", 2552), 1);
        private UniqueAddress bb = new UniqueAddress(new Address("akka.tcp", "sys", "bb", 2552), 2);
        private UniqueAddress cc = new UniqueAddress(new Address("akka.tcp", "sys", "cc", 2552), 3);
        private UniqueAddress dd = new UniqueAddress(new Address("akka.tcp", "sys", "dd", 2552), 4);
        private UniqueAddress ee = new UniqueAddress(new Address("akka.tcp", "sys", "ee", 2552), 5);

        private readonly ClusterHeartbeatSenderState _emptyState;

        private static ClusterHeartbeatSenderState EmptyState(UniqueAddress selfUniqueAddress)
        {
            return new ClusterHeartbeatSenderState(new HeartbeatNodeRing(selfUniqueAddress, ImmutableHashSet.Create(selfUniqueAddress), ImmutableHashSet<UniqueAddress>.Empty, 3),
                ImmutableHashSet.Create<UniqueAddress>(), new DefaultFailureDetectorRegistry<Address>(() => new FailureDetectorStub()));
        }

        private FailureDetectorStub Fd(ClusterHeartbeatSenderState state, UniqueAddress node)
        {
            return
                state.FailureDetector.AsInstanceOf<DefaultFailureDetectorRegistry<Address>>()
                    .GetFailureDetector(node.Address)
                    .AsInstanceOf<FailureDetectorStub>();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_return_empty_active_set_when_no_nodes()
        {
            _emptyState
                .ActiveReceivers.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_init_with_empty()
        {
            _emptyState.Init(ImmutableHashSet<UniqueAddress>.Empty, ImmutableHashSet<UniqueAddress>.Empty)
                .ActiveReceivers.IsEmpty.Should().BeTrue();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_init_with_self()
        {
            _emptyState.Init(ImmutableHashSet.Create(aa, bb, cc), ImmutableHashSet<UniqueAddress>.Empty)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_init_without_self()
        {
            _emptyState.Init(ImmutableHashSet.Create(bb, cc), ImmutableHashSet<UniqueAddress>.Empty)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_use_added_members()
        {
            _emptyState.AddMember(bb).AddMember(cc)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_use_added_members_also_when_unreachable()
        {
            _emptyState.AddMember(bb).AddMember(cc).UnreachableMember(bb)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_not_use_removed_members()
        {
            _emptyState.AddMember(bb).AddMember(cc).RemoveMember(bb)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_use_specified_number_of_members()
        {
            // they are sorted by the hash (UID) of the UniqueAddress
            _emptyState.AddMember(cc).AddMember(dd).AddMember(bb).AddMember(ee)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_use_specified_number_of_members_unreachable()
        {
            // they are sorted by the hash (UID) of the UniqueAddress
            _emptyState.AddMember(cc).AddMember(dd).AddMember(bb).AddMember(ee).UnreachableMember(cc)
                .ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd, ee));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_update_FailureDetector_in_active_set()
        {
            var s1 = _emptyState.AddMember(bb).AddMember(cc).AddMember(dd);
            var s2 = s1.HeartbeatRsp(bb).HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            s2.FailureDetector.IsMonitoring(bb.Address).Should().BeTrue();
            s2.FailureDetector.IsMonitoring(cc.Address).Should().BeTrue();
            s2.FailureDetector.IsMonitoring(dd.Address).Should().BeTrue();
            s2.FailureDetector.IsMonitoring(ee.Address).Should().BeFalse("Never added (ee) to active set, so we should not be monitoring it even if we did receive HeartbeatRsp from it");
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_continue_to_use_Unreachable()
        {
            var s1 = _emptyState.AddMember(cc).AddMember(dd).AddMember(ee);
            var s2 = s1.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            Fd(s2, ee).MarkNodeAsUnavailable();
            s2.FailureDetector.IsAvailable(ee.Address).Should().BeFalse();
            s2.AddMember(bb).ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd, ee));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_remove_unreachable_when_coming_back()
        {
            var s1 = _emptyState.AddMember(cc).AddMember(dd).AddMember(ee);
            var s2 = s1.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            Fd(s2,dd).MarkNodeAsUnavailable();
            Fd(s2,ee).MarkNodeAsUnavailable();
            var s3 = s2.AddMember(bb);
            s3.ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd, ee));
            var s4 = s3.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            s4.ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd));
            s4.FailureDetector.IsMonitoring(ee.Address).Should().BeFalse();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_remove_unreachable_member_when_removed()
        {
            var s1 = _emptyState.AddMember(cc).AddMember(dd).AddMember(ee);
            var s2 = s1.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            Fd(s2, cc).MarkNodeAsUnavailable();
            Fd(s2, ee).MarkNodeAsUnavailable();
            var s3 = s2.AddMember(bb).HeartbeatRsp(bb);
            s3.ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, cc, dd, ee));
            var s4 = s3.RemoveMember(cc).RemoveMember(ee);
            s4.ActiveReceivers.Should().BeEquivalentTo(ImmutableHashSet.Create(bb, dd));
            s4.FailureDetector.IsMonitoring(cc.Address).Should().BeFalse();
            s4.FailureDetector.IsMonitoring(ee.Address).Should().BeFalse();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_behave_correctly_for_random_operations()
        {
            var rnd = ThreadLocalRandom.Current;
            var nodes = Enumerable.Range(1, rnd.Next(10, 200))
                    .Select(n => new UniqueAddress(new Address("akka.tcp", "sys", "n" + n, 2552), n))
                    .ToList();
            Func<UniqueAddress> rndNode = () => nodes[rnd.Next(0, nodes.Count)];
            var selfUniqueAddress = rndNode();
            var state = EmptyState(selfUniqueAddress);
            const int Add = 0;
            const int Remove = 1;
            const int Unreachable = 2;
            const int HeartbeatRsp = 3;
            for (var i = 1; i <= 100000; i++)
            {
                var operation = rnd.Next(Add, HeartbeatRsp + 1);
                var node = rndNode();
                try
                {
                    switch (operation)
                    {
                        case Add:
                            if (node != selfUniqueAddress && !state.Ring.Nodes.Contains(node))
                            {
                                var oldUnreachable = state.OldReceiversNowUnreachable;
                                state = state.AddMember(node);
                                //keep unreachable
                                oldUnreachable.Except(state.ActiveReceivers).Should().BeEquivalentTo(ImmutableHashSet<UniqueAddress>.Empty);
                                state.FailureDetector.IsMonitoring(node.Address).Should().BeFalse();
                                state.FailureDetector.IsAvailable(node.Address).Should().BeTrue();
                            }
                            break;
                        case Remove:
                            if (node != selfUniqueAddress && state.Ring.Nodes.Contains(node))
                            {
                                var oldUnreachable = state.OldReceiversNowUnreachable;
                                state = state.RemoveMember(node);
                                // keep unreachable, unless it was the removed
                                if (oldUnreachable.Contains(node))
                                    oldUnreachable.Except(state.ActiveReceivers).Should().BeEquivalentTo(ImmutableHashSet.Create(node));
                                else
                                    oldUnreachable.Except(state.ActiveReceivers).Should().BeEquivalentTo(ImmutableHashSet<UniqueAddress>.Empty);

                                state.FailureDetector.IsMonitoring(node.Address).Should().BeFalse();
                                state.FailureDetector.IsAvailable(node.Address).Should().BeTrue();
                                state.ActiveReceivers.Should().NotContain(node);
                            }
                            break;
                        case Unreachable:
                            if (node != selfUniqueAddress && state.ActiveReceivers.Contains(node))
                            {
                                state.FailureDetector.Heartbeat(node.Address); //make sure the FD is created
                                Fd(state, node).MarkNodeAsUnavailable();
                                state.FailureDetector.IsMonitoring(node.Address).Should().BeTrue();
                                state.FailureDetector.IsAvailable(node.Address).Should().BeFalse();
                                state = state.UnreachableMember(node);
                            }
                            break;
                        case HeartbeatRsp:
                            if (node != selfUniqueAddress && state.Ring.Nodes.Contains(node))
                            {
                                var oldUnreachable = state.OldReceiversNowUnreachable;
                                var oldReceivers = state.ActiveReceivers;
                                var oldRingReceivers = state.Ring.MyReceivers.Value;
                                state = state.HeartbeatRsp(node);

                                if (oldUnreachable.Contains(node))
                                    state.OldReceiversNowUnreachable.Should().NotContain(node);

                                if (oldUnreachable.Contains(node) && !oldRingReceivers.Contains(node))
                                    state.FailureDetector.IsMonitoring(node.Address).Should().BeFalse();

                                if (oldRingReceivers.Contains(node))
                                    state.FailureDetector.IsMonitoring(node.Address).Should().BeTrue();

                                state.Ring.MyReceivers.Value.Should().BeEquivalentTo(oldRingReceivers);
                                state.FailureDetector.IsAvailable(node.Address).Should().BeTrue();
                            }
                            break;
                    }
                }
                catch (Exception)
                {
                    Debug.WriteLine("Failure context: i = {0}, node = {1}, op={2}, unreachable={3}, ringReceivers={4}, ringNodes={5}", i, node, operation, 
                        string.Join(",",state.OldReceiversNowUnreachable), 
                        string.Join(",", state.Ring.MyReceivers.Value), 
                        string.Join(",", state.Ring.Nodes));
                    throw;
                }
            }
        }
    }
}

