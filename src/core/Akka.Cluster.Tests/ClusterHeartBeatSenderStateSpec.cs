//-----------------------------------------------------------------------
// <copyright file="ClusterHeartBeatSenderStateSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Remote;
using Akka.TestKit;
using Akka.Util;
using Akka.Util.Internal;
using Xunit;

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
            return new ClusterHeartbeatSenderState(new HeartbeatNodeRing(selfUniqueAddress, new[] { selfUniqueAddress }, 3),
                ImmutableHashSet.Create<UniqueAddress>(), new DefaultFailureDetectorRegistry<Address>(() => new FailureDetectorStub()));
        }

        private FailureDetectorStub Fd(ClusterHeartbeatSenderState state, UniqueAddress node)
        {
            return
                state.FailureDetector.AsInstanceOf<DefaultFailureDetectorRegistry<Address>>()
                    .GetFailureDetector(node.Address)
                    .AsInstanceOf<FailureDetectorStub>();
        }

        #region Tests

        [Fact]
        public void ClusterHeartbeatSenderState_must_return_empty_active_set_when_no_nodes()
        {
            _emptyState.ActiveReceivers.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_init_with_empty()
        {
            _emptyState.Init(ImmutableHashSet.Create<UniqueAddress>()).ActiveReceivers.IsEmpty.ShouldBeTrue();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_init_with_self()
        {
            _emptyState.Init(ImmutableHashSet.Create<UniqueAddress>(aa, bb, cc)).ActiveReceivers.ShouldBe(ImmutableHashSet.Create<UniqueAddress>(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_init_without_self()
        {
            _emptyState.Init(ImmutableHashSet.Create<UniqueAddress>(bb, cc)).ActiveReceivers.ShouldBe(ImmutableHashSet.Create<UniqueAddress>(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_use_add_members()
        {
            _emptyState.AddMember(bb).AddMember(cc).ActiveReceivers.ShouldBe(ImmutableHashSet.Create<UniqueAddress>(bb, cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_not_use_removed_members()
        {
            _emptyState.AddMember(bb).AddMember(cc).RemoveMember(bb).ActiveReceivers.ShouldBe(ImmutableHashSet.Create<UniqueAddress>(cc));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_use_specified_number_of_members()
        {
            // they are sorted by the hash (UID) of the UniqueAddress
            _emptyState.AddMember(cc).AddMember(dd).AddMember(bb).AddMember(ee).ActiveReceivers.ShouldBe(ImmutableHashSet.Create<UniqueAddress>(bb,cc,dd));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_update_FailureDetector_in_active_set()
        {
            var s1 = _emptyState.AddMember(bb).AddMember(cc).AddMember(dd);
            var s2 = s1.HeartbeatRsp(bb).HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            s2.FailureDetector.IsMonitoring(bb.Address).ShouldBeTrue();
            s2.FailureDetector.IsMonitoring(cc.Address).ShouldBeTrue();
            s2.FailureDetector.IsMonitoring(dd.Address).ShouldBeTrue();
            s2.FailureDetector.IsMonitoring(ee.Address).ShouldBeFalse("Never added (ee) to active set, so we should not be monitoring it even if we did receive HeartbeatRsp from it");
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_continue_to_use_Unreachable()
        {
            var s1 = _emptyState.AddMember(cc).AddMember(dd).AddMember(ee);
            var s2 = s1.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            Fd(s2, ee).MarkNodeAsUnavailable();
            s2.FailureDetector.IsAvailable(ee.Address).ShouldBeFalse();
            s2.AddMember(bb).ActiveReceivers.ShouldBe(ImmutableHashSet.Create(bb,cc,dd,ee));
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_remove_unreachable_when_coming_back()
        {
            var s1 = _emptyState.AddMember(cc).AddMember(dd).AddMember(ee);
            var s2 = s1.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            Fd(s2,dd).MarkNodeAsUnavailable();
            Fd(s2,ee).MarkNodeAsUnavailable();
            var s3 = s2.AddMember(bb);
            s3.ActiveReceivers.ShouldBe(ImmutableHashSet.Create(bb,cc,dd,ee));
            var s4 = s3.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            s4.ActiveReceivers.ShouldBe(ImmutableHashSet.Create(bb,cc,dd));
            s4.FailureDetector.IsMonitoring(ee.Address).ShouldBeFalse();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_remove_unreachable_member_when_removed()
        {
            var s1 = _emptyState.AddMember(cc).AddMember(dd).AddMember(ee);
            var s2 = s1.HeartbeatRsp(cc).HeartbeatRsp(dd).HeartbeatRsp(ee);
            Fd(s2, cc).MarkNodeAsUnavailable();
            Fd(s2, ee).MarkNodeAsUnavailable();
            var s3 = s2.AddMember(bb).HeartbeatRsp(bb);
            s3.ActiveReceivers.ShouldBe(ImmutableHashSet.Create(bb,cc,dd,ee));
            var s4 = s3.RemoveMember(cc).RemoveMember(ee);
            s4.ActiveReceivers.ShouldBe(ImmutableHashSet.Create(bb,dd));
            s4.FailureDetector.IsMonitoring(cc.Address).ShouldBeFalse();
            s4.FailureDetector.IsMonitoring(ee.Address).ShouldBeFalse();
        }

        [Fact]
        public void ClusterHeartbeatSenderState_must_behave_correctly_for_random_operations()
        {
            var rnd = ThreadLocalRandom.Current;
            var nodes =
                Enumerable.Range(1, rnd.Next(10, 200))
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
                            if (node != selfUniqueAddress && !state.Ring.NodeRing.Contains(node))
                            {
                                var oldUnreachable = state.Unreachable;
                                state = state.AddMember(node);
                                //keep unreachable
                                (oldUnreachable.Except(state.ActiveReceivers)).ShouldBe(ImmutableHashSet.Create<UniqueAddress>());
                                state.FailureDetector.IsMonitoring(node.Address).ShouldBeFalse();
                                state.FailureDetector.IsAvailable(node.Address).ShouldBeTrue();
                            }
                            break;
                        case Remove:
                            if (node != selfUniqueAddress && state.Ring.NodeRing.Contains(node))
                            {
                                var oldUnreachable = state.Unreachable;
                                state = state.RemoveMember(node);
                                // keep unreachable, unless it was the removed
                                if(oldUnreachable.Contains(node))
                                    oldUnreachable.Except(state.ActiveReceivers).ShouldBe(ImmutableHashSet.Create(node));
                                else
                                    (oldUnreachable.Except(state.ActiveReceivers)).ShouldBe(ImmutableHashSet.Create<UniqueAddress>());

                                state.FailureDetector.IsMonitoring(node.Address).ShouldBeFalse();
                                state.FailureDetector.IsAvailable(node.Address).ShouldBeTrue();
                                Assert.False(state.ActiveReceivers.Any(x => x == node));
                            }
                            break;
                        case Unreachable:
                            if (node != selfUniqueAddress && state.ActiveReceivers.Contains(node))
                            {
                                state.FailureDetector.Heartbeat(node.Address); //make sure the FD is created
                                Fd(state, node).MarkNodeAsUnavailable();
                                state.FailureDetector.IsMonitoring(node.Address).ShouldBeTrue();
                                state.FailureDetector.IsAvailable(node.Address).ShouldBeFalse();
                            }
                            break;
                        case HeartbeatRsp:
                            if (node != selfUniqueAddress && state.Ring.NodeRing.Contains(node))
                            {
                                var oldUnreachable = state.Unreachable;
                                var oldReceivers = state.ActiveReceivers;
                                var oldRingReceivers = state.Ring.MyReceivers.Value;
                                state = state.HeartbeatRsp(node);

                                if(oldUnreachable.Contains(node))
                                    Assert.False(state.Unreachable.Contains(node));
                                if(oldUnreachable.Contains(node) && !oldRingReceivers.Contains(node))
                                    state.FailureDetector.IsMonitoring(node.Address).ShouldBeFalse();
                                if(oldRingReceivers.Contains(node))
                                    state.FailureDetector.IsMonitoring(node.Address).ShouldBeTrue();

                                state.Ring.MyReceivers.Value.ShouldBe(oldRingReceivers);
                                state.FailureDetector.IsAvailable(node.Address).ShouldBeTrue();
                            }
                            break;
                    }
                }
                catch (Exception)
                {
                    Debug.WriteLine("Failure context: i = {0}, node = {1}, op={2}, unreachable={3}, ringReceivers={4}, ringNodes={5}", i, node, operation, 
                        string.Join(",",state.Unreachable), 
                        string.Join(",", state.Ring.MyReceivers.Value), 
                        string.Join(",", state.Ring.NodeRing));
                    throw;
                }
            }
        }

        #endregion
    }
}

