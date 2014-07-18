using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Cluster.Tests
{
    [TestClass]
    public class GossipSpec
    {
        static readonly Member a1 = TestMember.Create(new Address("akka.tcp", "sys", "a", 2552), MemberStatus.Up);
        static readonly Member a2 = TestMember.Create(a1.Address, MemberStatus.Joining);
        static readonly Member b1 = TestMember.Create(new Address("akka.tcp", "sys", "b", 2552), MemberStatus.Up);
        static readonly Member b2 = TestMember.Create(b1.Address, MemberStatus.Removed);
        static readonly Member c1 = TestMember.Create(new Address("akka.tcp", "sys", "c", 2552), MemberStatus.Leaving);
        static readonly Member c2 = TestMember.Create(c1.Address, MemberStatus.Up);
        static readonly Member c3 = TestMember.Create(c1.Address, MemberStatus.Exiting);
        static readonly Member d1 = TestMember.Create(new Address("akka.tcp", "sys", "d", 2552), MemberStatus.Leaving);
        static readonly Member e1 = TestMember.Create(new Address("akka.tcp", "sys", "e", 2552), MemberStatus.Joining);
        static readonly Member e2 = TestMember.Create(e1.Address, MemberStatus.Up);
        static readonly Member e3 = TestMember.Create(e1.Address, MemberStatus.Down);

        [TestMethod]
        public void AGossipMustReachConvergenceWhenItsEmpty()
        {
            Assert.IsTrue(Gossip.Empty.Convergence);
        }

        [TestMethod]
        public void AGossipMustMergeMembersByStatusPriority()
        {
            var g1 = Gossip.Create(ImmutableSortedSet.Create(a1, c1, e1));
            var g2 = Gossip.Create(ImmutableSortedSet.Create(a2, c2, e2));

            var merged1 = g1.Merge(g2);
            CollectionAssert.AreEqual(ImmutableSortedSet.Create(a2, c1, e1), merged1.Members);
            CollectionAssert.AreEqual(new []{MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Up}, merged1.Members.Select(m => m.Status).ToArray());

            var merged2 = g2.Merge(g1);
            CollectionAssert.AreEqual(ImmutableSortedSet.Create(a2, c1, e1), merged2.Members);
            CollectionAssert.AreEqual(new []{MemberStatus.Up, MemberStatus.Leaving, MemberStatus.Up}, merged2.Members.Select(m => m.Status).ToArray());
        }

        [TestMethod]
        public void AGossipMustMergeUnreachable()
        {
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, a1.UniqueAddress)
                .Unreachable(b1.UniqueAddress, c1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1), new GossipOverview(r1));
            var r2 = Reachability.Empty.Unreachable(a1.UniqueAddress, d1.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1), new GossipOverview(r2));

            var merged1 = g1.Merge(g2);
            CollectionAssert.AreEquivalent(ImmutableHashSet.Create(a1.UniqueAddress, c1.UniqueAddress, d1.UniqueAddress),
                merged1.Overview.Reachability.AllUnreachable);

            var merged2 = g2.Merge(g1);
            CollectionAssert.AreEquivalent(merged1.Overview.Reachability.AllUnreachable,
                merged2.Overview.Reachability.AllUnreachable
                );
        }

        [TestMethod]
        public void AGossipMustMergeMembersByRemovingRemovedMembers()
        {
            // c3 removed
            var r1 = Reachability.Empty.Unreachable(b1.UniqueAddress, a1.UniqueAddress);
            var g1 = new Gossip(ImmutableSortedSet.Create(a1, b1), new GossipOverview(r1));
            var r2 = r1.Unreachable(b1.UniqueAddress, c3.UniqueAddress);
            var g2 = new Gossip(ImmutableSortedSet.Create(a1, b1, c3), new GossipOverview(r2));

            var merged1 = g1.Merge(g2);
            CollectionAssert.AreEqual(ImmutableHashSet.Create(a1, b1), merged1.Members);
            CollectionAssert.AreEqual(ImmutableHashSet.Create(a1.UniqueAddress), merged1.Overview.Reachability.AllUnreachable);


            var merged2 = g2.Merge(g1);
            CollectionAssert.AreEqual(merged2.Overview.Reachability.AllUnreachable, merged1.Overview.Reachability.AllUnreachable);
            CollectionAssert.AreEqual(merged1.Members, merged2.Members);
        }

        [TestMethod]
        public void AGossipMustHaveLeaderAsFirstMemberBasedOnOrderingExceptExitingStatus()
        {
            Assert.AreEqual(c2.UniqueAddress, new Gossip(ImmutableSortedSet.Create(c2, e2)).Leader);
            Assert.AreEqual(e2.UniqueAddress, new Gossip(ImmutableSortedSet.Create(c3, e2)).Leader);
            Assert.AreEqual(c3.UniqueAddress, new Gossip(ImmutableSortedSet.Create(c3)).Leader);
        }

        [TestMethod]
        public void AGossipMustMergeSeenTableCorrectly()
        {
            var vclockNode = VectorClock.Node.Create("something");
            var g1 =
                new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(vclockNode)
                    .Seen(a1.UniqueAddress)
                    .Seen(b1.UniqueAddress);
            var g2 =
                new Gossip(ImmutableSortedSet.Create(a1, b1, c1, d1)).Increment(vclockNode)
                    .Seen(a1.UniqueAddress)
                    .Seen(c1.UniqueAddress);
            var g3 = g1.Copy(version: g2.Version).Seen(d1.UniqueAddress);

            Action<Gossip> checkMerge = (m) =>
            {
                var seen = m.Overview.Seen;
                Assert.AreEqual(0, seen.Count());

                Assert.IsFalse(m.SeenByNode(a1.UniqueAddress));
                Assert.IsFalse(m.SeenByNode(b1.UniqueAddress));
                Assert.IsFalse(m.SeenByNode(c1.UniqueAddress));
                Assert.IsFalse(m.SeenByNode(d1.UniqueAddress));
                Assert.IsFalse(m.SeenByNode(e1.UniqueAddress));
            };

            checkMerge(g3.Merge(g2));
            checkMerge(g2.Merge(g3));
        }

        [TestMethod]
        public void AGossipMustKnowWhoIsYoungest()
        {
            // a2 and e1 is Joining
            var g1 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e1),
                new GossipOverview(Reachability.Empty.Unreachable(a2.UniqueAddress, e1.UniqueAddress)));
            Assert.AreEqual(b1, g1.YoungestMember);
            var g2 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e1),
                new GossipOverview(Reachability.Empty.Unreachable(a2.UniqueAddress, b1.UniqueAddress).Unreachable(a2.UniqueAddress, e1.UniqueAddress)));
            Assert.AreEqual(b1, g2.YoungestMember);
            var g3 = new Gossip(ImmutableSortedSet.Create(a2, b1.CopyUp(3), e2.CopyUp(4)));
            Assert.AreEqual(e2, g3.YoungestMember);
        }
    }
}
