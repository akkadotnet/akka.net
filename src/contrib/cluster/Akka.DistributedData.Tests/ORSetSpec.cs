//-----------------------------------------------------------------------
// <copyright file="ORSetSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.DistributedData.Tests
{
    [Collection("DistributedDataSpec")]
    public class ORSetSpec
    {
        private readonly string _user1 = "{\"username\":\"john\",\"password\":\"coltrane\"}";
        private readonly string _user2 = "{\"username\":\"sonny\",\"password\":\"rollins\"}";
        private readonly string _user3 = "{\"username\":\"charlie\",\"password\":\"parker\"}";
        private readonly string _user4 = "{\"username\":\"charles\",\"password\":\"mingus\"}";

        private readonly UniqueAddress _node1;
        private readonly UniqueAddress _node2;
        private readonly UniqueAddress _node3;

        private readonly UniqueAddress _nodeA;
        private readonly UniqueAddress _nodeB;
        private readonly UniqueAddress _nodeC;
        private readonly UniqueAddress _nodeD;
        private readonly UniqueAddress _nodeE;
        private readonly UniqueAddress _nodeF;
        private readonly UniqueAddress _nodeG;
        private readonly UniqueAddress _nodeH;

        public ORSetSpec(ITestOutputHelper output)
        {
            _node1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2551), 1);
            _node2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2552), 2);
            _node3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 2553), 3);

            _nodeA = new UniqueAddress(new Address("akka.tcp", "Sys", "a", 2552), 1);
            _nodeB = new UniqueAddress(new Address("akka.tcp", "Sys", "b", 2552), 2);
            _nodeC = new UniqueAddress(new Address("akka.tcp", "Sys", "c", 2552), 3);
            _nodeD = new UniqueAddress(new Address("akka.tcp", "Sys", "d", 2552), 4);
            _nodeE = new UniqueAddress(new Address("akka.tcp", "Sys", "e", 2552), 5);
            _nodeF = new UniqueAddress(new Address("akka.tcp", "Sys", "f", 2552), 6);
            _nodeG = new UniqueAddress(new Address("akka.tcp", "Sys", "g", 2552), 7);
            _nodeH = new UniqueAddress(new Address("akka.tcp", "Sys", "h", 2552), 8);
        }

        [Fact]
        public void ORSet_must_be_able_to_add_element()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Add(_node1, _user1);
            var c3 = c2.Add(_node1, _user2);
            var c4 = c3.Add(_node1, _user4);
            var c5 = c4.Add(_node1, _user3);

            Assert.Contains(_user1, c5.Elements);
            Assert.Contains(_user2, c5.Elements);
            Assert.Contains(_user3, c5.Elements);
            Assert.Contains(_user4, c5.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_remove_element()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Add(_node1, _user1);
            var c3 = c2.Add(_node1, _user2);
            var c4 = c3.Remove(_node1, _user2);
            var c5 = c4.Remove(_node1, _user1);

            Assert.DoesNotContain(_user1, c5.Elements);
            Assert.DoesNotContain(_user2, c5.Elements);

            var c6 = c3.Merge(c5);
            Assert.DoesNotContain(_user1, c6.Elements);
            Assert.DoesNotContain(_user2, c6.Elements);

            var c7 = c5.Merge(c3);
            Assert.DoesNotContain(_user1, c7.Elements);
            Assert.DoesNotContain(_user2, c7.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_add_removed_element()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Remove(_node1, _user1);
            var c3 = c2.Add(_node1, _user1);

            Assert.Contains(_user1, c3.Elements);
            var c4 = c3.Remove(_node1, _user1);
            Assert.DoesNotContain(_user1, c4.Elements);
            var c5 = c4.Add(_node1, _user1);
            Assert.Contains(_user1, c5.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_add_and_remove_several_times()
        {
            var c1 = ORSet<string>.Empty;
            var c2 = c1.Add(_node1, _user1);
            var c3 = c2.Add(_node1, _user2);
            var c4 = c3.Remove(_node1, _user1);
            Assert.Contains(_user2, c4.Elements);
            Assert.DoesNotContain(_user1, c4.Elements);

            var c5 = c4.Add(_node1, _user1);
            var c6 = c5.Add(_node1, _user2);
            Assert.Contains(_user1, c6.Elements);
            Assert.Contains(_user2, c6.Elements);

            var c7 = c6.Remove(_node1, _user1);
            var c8 = c7.Add(_node1, _user2);
            var c9 = c8.Remove(_node1, _user1);
            Assert.DoesNotContain(_user1, c9.Elements);
            Assert.Contains(_user2, c9.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_have_its_element_set_correctly_merged_with_another_ORSet_with_unique_element_sets()
        {
            // set 1
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2);
            Assert.Contains(_user1, c1.Elements);
            Assert.Contains(_user2, c1.Elements);

            // set 2
            var c2 = ORSet<string>.Empty.Add(_node2, _user3).Add(_node2, _user4).Remove(_node2, _user3);
            Assert.Contains(_user4, c2.Elements);
            Assert.DoesNotContain(_user3, c2.Elements);

            //merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.Contains(_user2, m1.Elements);
            Assert.DoesNotContain(_user3, m1.Elements);
            Assert.Contains(_user4, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.Contains(_user2, m2.Elements);
            Assert.DoesNotContain(_user3, m2.Elements);
            Assert.Contains(_user4, m2.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_have_its_element_set_correctly_merged_with_another_ORSet_with_overlaping_data()
        {
            // set 1
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2).Add(_node1, _user3).Remove(_node1, _user1).Remove(_node1, _user3);
            Assert.DoesNotContain(_user1, c1.Elements);
            Assert.Contains(_user2, c1.Elements);
            Assert.DoesNotContain(_user3, c1.Elements);

            // set 2
            var c2 = ORSet<string>.Empty.Add(_node2, _user1).Add(_node2, _user2).Add(_node2, _user3).Add(_node2, _user4).Remove(_node2, _user3);
            Assert.Contains(_user1, c2.Elements);
            Assert.Contains(_user2, c2.Elements);
            Assert.DoesNotContain(_user3, c2.Elements);
            Assert.Contains(_user4, c2.Elements);

            //merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.Contains(_user2, m1.Elements);
            Assert.DoesNotContain(_user3, m1.Elements);
            Assert.Contains(_user4, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.Contains(_user2, m2.Elements);
            Assert.DoesNotContain(_user3, m2.Elements);
            Assert.Contains(_user4, m2.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_have_its_element_set_correctly_merged_for_concurrent_updates()
        {
            // set 1
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2).Add(_node1, _user3);
            Assert.Contains(_user1, c1.Elements);
            Assert.Contains(_user2, c1.Elements);
            Assert.Contains(_user3, c1.Elements);

            // set 1
            var c2 = c1.Add(_node2, _user1).Remove(_node2, _user2).Remove(_node2, _user3);
            Assert.Contains(_user1, c2.Elements);
            Assert.DoesNotContain(_user2, c2.Elements);
            Assert.DoesNotContain(_user3, c2.Elements);

            //merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.DoesNotContain(_user2, m1.Elements);
            Assert.DoesNotContain(_user3, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.DoesNotContain(_user2, m2.Elements);
            Assert.DoesNotContain(_user3, m2.Elements);

            var c3 = c1.Add(_node1, _user4).Remove(_node1, _user3).Add(_node1, _user2);

            //merge both ways
            var m3 = c2.Merge(c3);
            Assert.Contains(_user1, m3.Elements);
            Assert.Contains(_user2, m3.Elements);
            Assert.DoesNotContain(_user3, m3.Elements);
            Assert.Contains(_user4, m3.Elements);

            var m4 = c3.Merge(c2);
            Assert.Contains(_user1, m4.Elements);
            Assert.Contains(_user2, m4.Elements);
            Assert.DoesNotContain(_user3, m4.Elements);
            Assert.Contains(_user4, m4.Elements);
        }

        [Fact]
        public void ORSet_must_be_able_to_have_its_element_set_correctly_merged_after_remove()
        {
            var c1 = ORSet<string>.Empty.Add(_node1, _user1).Add(_node1, _user2);
            var c2 = c1.Remove(_node2, _user2);

            // merge both ways
            var m1 = c1.Merge(c2);
            Assert.Contains(_user1, m1.Elements);
            Assert.DoesNotContain(_user2, m1.Elements);

            var m2 = c2.Merge(c1);
            Assert.Contains(_user1, m2.Elements);
            Assert.DoesNotContain(_user2, m2.Elements);

            var c3 = c1.Add(_node1, _user3);

            // merge both ways
            var m3 = c3.Merge(c2);
            Assert.Contains(_user1, m3.Elements);
            Assert.DoesNotContain(_user2, m3.Elements);
            Assert.Contains(_user1, m3.Elements);

            var m4 = c2.Merge(c3);
            Assert.Contains(_user1, m4.Elements);
            Assert.DoesNotContain(_user2, m4.Elements);
            Assert.Contains(_user1, m4.Elements);
        }

        #region ORSet delta specs

        private ORSet<T>.AddDeltaOperation AddDelta<T>(ORSet<T> set) => (ORSet<T>.AddDeltaOperation)set.Delta;

        [Fact]
        public void ORSetDeltas_must_work_with_addition()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.Add(_node1, "a");

            AddDelta(s2).Underlying.Should().BeEquivalentTo("a");
            s1.MergeDelta(s2.Delta).Should().Equal(s2);

            var s3 = s2.ResetDelta().Add(_node1, "b").Add(_node1, "c");
            AddDelta(s3).Underlying.Elements.Should().BeEquivalentTo("b", "c");
            s2.MergeDelta(s3.Delta).Should().Equal(s3);

            // another node adds "d"
            var s4 = s3.ResetDelta().Add(_node2, "d");
            AddDelta(s4).Underlying.Elements.Should().BeEquivalentTo("d");
            s3.MergeDelta(s4.Delta).Should().Equal(s4);

            // concurrent update
            var s5 = s3.ResetDelta().Add(_node1, "e");
            var s6 = s5.Merge(s4);
            s5.MergeDelta(s4.Delta).Should().Equal(s6);

            // concurrent add of same element
            var s7 = s3.ResetDelta().Add(_node1, "d");
            var s8 = s7.Merge(s4);
            // the dot contains both nodes
            s8.ElementsMap["d"].Contains(_node1).Should().BeTrue();
            s8.ElementsMap["d"].Contains(_node2).Should().BeTrue();
            // and same result when merging the deltas
            s7.MergeDelta(s4.Delta).Should().Equal(s8);
            s4.MergeDelta(s7.Delta).Should().Equal(s8);
        }

        [Fact]
        public void ORSetDeltas_must_handle_another_concurrent_add_scenario()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.Add(_node1, "a");
            var s3 = s2.Add(_node1, "b");
            var s4 = s2.Add(_node2, "c");

            // full state merge for reference
            var s5 = s4.Merge(s3);
            s5.Elements.Should().BeEquivalentTo("a", "b", "c");

            var s6 = s4.MergeDelta(s3.Delta);
            s6.Elements.Should().BeEquivalentTo("a", "b", "c");
        }

        [Fact]
        public void ORSetDeltas_must_merge_deltas_into_delta_groups()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.Add(_node1, "a");
            var d2 = s2.Delta;
            var s3 = s2.ResetDelta().Add(_node1, "b");
            var d3 = s3.Delta;
            var d4 = (ORSet<string>.IDeltaOperation)d2.Merge(d3);
            ((ORSet<string>.AddDeltaOperation)d4).Underlying.Elements.Should().BeEquivalentTo("a", "b");
            s1.MergeDelta(d4).Should().Equal(s3);
            s2.MergeDelta(d4).Should().Equal(s3);

            var s5 = s3.ResetDelta().Remove(_node1, "b");
            var d5 = s5.Delta;
            var d6 = (ORSet<string>.DeltaGroup)d4.Merge(d5);
            d6.Operations.Last().Should().BeOfType<ORSet<string>.RemoveDeltaOperation>();
            d6.Operations.Length.Should().Be(2);
            s3.MergeDelta(d6).Should().Equal(s5);

            var s7 = s5.ResetDelta().Add(_node1, "c");
            var s8 = s7.ResetDelta().Add(_node1, "d");
            var d9 = (ORSet<string>.DeltaGroup)d6.Merge(s7.Delta.Merge(s8.Delta));
            // the add "c" and add "d" are merged into one AddDeltaOp
            ((ORSet<string>.AddDeltaOperation)d9.Operations.Last()).Underlying.Elements.Should().BeEquivalentTo("c", "d");
            d9.Operations.Length.Should().Be(3);
            s5.MergeDelta(d9).Should().Equal(s8);
            s5.MergeDelta(s7.Delta).MergeDelta(s8.Delta).Should().Equal(s8);
        }


        [Fact]
        public void ORSetDeltas_must_work_for_removals()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.Add(_node1, "a").Add(_node1, "b").ResetDelta();
            var s3 = s2.Remove(_node1, "b");
            s2.Merge(s3).Should().Equal(s3);
            s2.MergeDelta(s3.Delta).Should().Equal(s3);
            s2.MergeDelta(s3.Delta).Elements.Should().BeEquivalentTo("a");

            // concurrent update
            var s4 = s2.Add(_node2, "c").ResetDelta();
            var s5 = s4.Merge(s3);
            s5.Elements.Should().BeEquivalentTo("a", "c");
            s4.MergeDelta(s3.Delta).Should().Equal(s5);

            // add "b" again
            var s6 = s5.Add(_node2, "b");
            // merging the old delta should not remove it
            s6.MergeDelta(s3.Delta).Should().Equal(s6);
            s6.MergeDelta(s3.Delta).Elements.Should().BeEquivalentTo("a", "b", "c");
        }


        [Fact]
        public void ORSetDeltas_must_work_for_clear()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.Add(_node1, "a").Add(_node1, "b");
            var s3 = s2.ResetDelta().Clear(_node1);
            var s4 = s3.ResetDelta().Add(_node1, "c");
            s2.Merge(s3).Should().Equal(s3);
            s2.MergeDelta(s3.Delta).Should().Equal(s3);
            var s5 = s2.MergeDelta(s3.Delta).MergeDelta(s4.Delta);
            s5.Elements.Should().BeEquivalentTo("c");
            s5.Should().Equal(s4);

            // concurrent update
            var s6 = s2.ResetDelta().Add(_node2, "d");
            var s7 = s6.Merge(s3);
            s7.Elements.Should().BeEquivalentTo("d");
            s6.MergeDelta(s3.Delta).Should().Equal(s7);

            // add "b" again
            var s8 = s7.Add(_node2, "b");
            // merging the old delta should not remove it
            s8.MergeDelta(s3.Delta).Should().Equal(s8);
            s8.MergeDelta(s3.Delta).Elements.Should().BeEquivalentTo("b", "d");
        }

        [Fact]
        public void ORSetDeltas_must_handle_a_mixed_add_and_remove_scenario()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.ResetDelta().Remove(_node1, "e");
            var s3 = s2.ResetDelta().Add(_node1, "b");
            var s4 = s3.ResetDelta().Add(_node1, "a");
            var s5 = s4.ResetDelta().Remove(_node1, "b");

            var deltaGroup1 = (ORSet<string>.IDeltaOperation)s3.Delta.Merge(s4.Delta.Merge(s5.Delta));

            var s7 = s2.MergeDelta(deltaGroup1);
            s7.Elements.Should().BeEquivalentTo("a");
            // The above scenario was constructed from failing ReplicatorDeltaSpec,
            // some more checks...

            var s8 = s2.ResetDelta().Add(_node2, "z"); // concurrent update from node 2
            var s9 = s8.MergeDelta(deltaGroup1);
            s9.Elements.Should().BeEquivalentTo("a", "z");
        }

        [Fact]
        public void ORSetDeltas_must_handle_a_mixed_add_and_remove_scenario_2()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.ResetDelta().Add(_node1, "a");
            var s3 = s2.ResetDelta().Add(_node1, "b");
            var s4 = s3.ResetDelta().Add(_node2, "a");
            var s5 = s4.ResetDelta().Remove(_node1, "a");

            s5.Elements.Should().BeEquivalentTo("b");

            var delta1 = (ORSet<string>.IDeltaOperation)s2.Delta.Merge(s3.Delta);
            var delta2 = s4.Delta;

            var t1 = ORSet<string>.Empty;
            var t2 = t1.MergeDelta(delta1).MergeDelta(delta2);
            t2.Elements.Should().BeEquivalentTo("a", "b");
            var t3 = t2.ResetDelta().Add(_node3, "z");

            var t4 = t3.MergeDelta(s5.Delta);

            t4.Elements.Should().BeEquivalentTo("b", "z");
        }

        [Fact]
        public void ORSetDeltas_must_handle_a_mixed_add_and_remove_scenario_3()
        {
            var s1 = ORSet<string>.Empty;
            var s2 = s1.ResetDelta().Add(_node1, "a");
            var s3 = s2.ResetDelta().Add(_node1, "b");
            var s4 = s3.ResetDelta().Add(_node2, "a");
            var s5 = s4.ResetDelta().Remove(_node1, "a");

            s5.Elements.Should().BeEquivalentTo("b");

            var delta1 = (ORSet<string>.IDeltaOperation)s2.Delta.Merge(s3.Delta);

            var t1 = ORSet<string>.Empty;
            var t2 = t1.MergeDelta(delta1);
            t2.Elements.Should().BeEquivalentTo("a", "b");

            var t3 = t2.ResetDelta().Add(_node3, "a");

            var t4 = t3.MergeDelta(s5.Delta);

            t4.Elements.Should().BeEquivalentTo("b", "a");
        }

        [Fact]
        public void ORSetDeltas_must_do_not_have_anomalies_for_ORSet_in_complex_but_realistic_scenario()
        {
            var node1_1 = ORSet<string>.Empty.Add(_node1, "q").Remove(_node1, "q");
            var delta1_1 = node1_1.Delta;
            var node1_2 = node1_1.ResetDelta().ResetDelta().Add(_node1, "z").Remove(_node1, "z");
            var delta1_2 = node1_2.Delta;
            // we finished doing stuff on node1 - there are two separate deltas that will be propagated
            // node2 is created, then gets first delta from node1 and then adds an element "x"
            var node2_1 = ORSet<string>.Empty.MergeDelta(delta1_1).ResetDelta().Add(_node2, "x");
            var delta2_1 = node2_1.Delta;
            // node2 continues its existence adding and later removing the element
            // it still didn't get the second update from node1 (that is fully legit :) )
            var node2_2 = node2_1.ResetDelta().Add(_node2, "a").Remove(_node2, "a");
            var delta2_2 = node2_2.Delta;

            // in the meantime there is some node3
            // there is not much activity on it, it just gets the first delta from node1 then it gets
            // first delta from node2
            // then it gets the second delta from node1 (that node2 still didn't get, but, hey!, this is fine)
            var node3_1 = ORSet<string>.Empty.MergeDelta(delta1_1).MergeDelta(delta2_1).MergeDelta(delta1_2);

            // and node3_1 receives full update from node2 via gossip
            var merged1 = node3_1.Merge(node2_2);

            merged1.Should().NotContain("a");

            // and node3_1 receives delta update from node2 (it just needs to get the second delta,
            // as it already got the first delta just a second ago)

            var merged2 = node3_1.MergeDelta(delta2_2);

            merged2.Should().BeEquivalentTo("x");
        }

        [Fact]
        public void ORSetDeltas_must_require_casual_delivery_of_deltas()
        {
            // This test illustrates why we need causal delivery of deltas.
            // Otherwise the following could happen.

            // s0 is the stable state that is initially replicated to all nodes
            var s0 = ORSet<string>.Empty.Add(_node1, "a");

            // add element "b" and "c" at node1
            var s11 = s0.ResetDelta().Add(_node1, "b");
            var s12 = s11.ResetDelta().Add(_node1, "c");

            // at the same time, add element "d" at node2
            var s21 = s0.ResetDelta().Add(_node2, "d");

            // node3 receives delta for "d" and "c", but the delta for "b" is lost
            var s31 = s0.MergeDelta(s21.Delta).MergeDelta(s12.Delta);
            s31.Elements.Should().BeEquivalentTo("a", "c", "d");

            // node4 receives all deltas
            var s41 = s0.MergeDelta(s11.Delta).MergeDelta(s12.Delta).MergeDelta(s21.Delta);
            s41.Elements.Should().BeEquivalentTo("a", "b", "c", "d");

            // node3 and node4 sync with full state gossip
            var s32 = s31.Merge(s41);
            // one would expect elements "a", "b", "c", "d", but "b" is removed
            // because we applied s12.delta without applying s11.delta
            s32.Elements.Should().BeEquivalentTo("a", "c", "d");
        }
        #endregion

        #region ORSet unit test

        [Fact]
        public void ORSet_must_verify_SubtractDots()
        {
            var dot = VersionVector.Create(ImmutableDictionary.CreateRange(new Dictionary<UniqueAddress, long>
            {
                {_nodeA, 3L}, { _nodeB, 2L }, { _nodeD, 14L }, { _nodeG, 22L }
            }));
            var vvector = VersionVector.Create(ImmutableDictionary.CreateRange(new Dictionary<UniqueAddress, long>
            {
                { _nodeA, 4L}, { _nodeB, 1L}, { _nodeC, 1L}, { _nodeD, 14L}, { _nodeE, 5L}, { _nodeF, 2L }
            }));
            var expected = VersionVector.Create(ImmutableDictionary.CreateRange(new Dictionary<UniqueAddress, long>
            {
                { _nodeB, 2L}, { _nodeG, 22L}
            }));

            ORSet.SubtractDots(dot, vvector).Should().Be(expected);
        }

        [Fact]
        public void ORSet_must_verify_MergeCommonKeys()
        {
            var commonKeys = ImmutableHashSet.CreateRange(new[] { "K1", "K2" });
            var thisDot1 = VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeA, 3L }, { _nodeD, 7L } }.ToImmutableDictionary());
            var thisDot2 = VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeB, 5L }, { _nodeC, 2L } }.ToImmutableDictionary());
            var thisVVector = VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeA, 3L }, { _nodeB, 5L }, { _nodeC, 2L }, { _nodeD, 7L } }.ToImmutableDictionary());

            var thisSet = new ORSet<string>(
                elementsMap: new Dictionary<string, VersionVector> { { "K1", thisDot1 }, { "K2", thisDot2 } }.ToImmutableDictionary(),
                versionVector: thisVVector);

            var thatDot1 = VersionVector.Create(_nodeA, 3L);
            var thatDot2 = VersionVector.Create(_nodeB, 6L);
            var thatVVector = VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeA, 3L }, { _nodeB, 6L }, { _nodeC, 1L }, { _nodeD, 8L } }.ToImmutableDictionary());
            var thatSet = new ORSet<string>(
                elementsMap: new Dictionary<string, VersionVector> { { "K1", thatDot1 }, { "K2", thatDot2 } }.ToImmutableDictionary(),
                versionVector: thatVVector);

            var expectedDots = new Dictionary<string, VersionVector>
            {
                { "K1", VersionVector.Create(_nodeA, 3L) },
                { "K2", VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeB, 6L }, { _nodeC, 2L } }.ToImmutableDictionary()) }
            };

            ORSet<string>.MergeCommonKeys(commonKeys, thisSet, thatSet).Should().Equal(expectedDots);
        }

        [Fact]
        public void ORSet_must_verify_MergeDisjointKeys()
        {
            var keys = ImmutableHashSet.CreateRange(new[] { "K3", "K4", "K5" });
            var elements = new Dictionary<string, VersionVector>
            {
                { "K3", VersionVector.Create(_nodeA, 4L) },
                { "K4", VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeA, 3L }, { _nodeD, 8L } }.ToImmutableDictionary()) },
                { "K5", VersionVector.Create(_nodeA, 2L) },
            }.ToImmutableDictionary();
            var vvector = VersionVector.Create(new Dictionary<UniqueAddress, long> { { _nodeA, 3L }, { _nodeD, 7L } }.ToImmutableDictionary());
            var acc = new Dictionary<string, VersionVector> { { "K1", VersionVector.Create(_nodeA, 3L) } }.ToImmutableDictionary();
            var expectedDots = new Dictionary<string, VersionVector>
            {
                { "K1", VersionVector.Create(_nodeA, 3L) },
                { "K3", VersionVector.Create(_nodeA, 4L) },
                { "K4", VersionVector.Create(_nodeD, 8L) }, // "a" -> 3 removed, optimized to include only those unseen
            };

            ORSet<string>.MergeDisjointKeys(keys, elements, vvector, acc).Should().Equal(expectedDots);
        }

        [Fact]
        public void ORSet_must_verify_disjoint_Merge()
        {
            var a1 = ORSet.Create(_node1, "bar");
            var b1 = ORSet.Create(_node2, "baz");
            var c = a1.Merge(b1);
            var a2 = a1.Remove(_node1, "bar");
            var d = a2.Merge(c);
            d.Elements.Should().BeEquivalentTo("baz");
        }

        [Fact]
        public void ORSet_must_verify_removed_after_merge()
        {
            // Add Z at node1 replica
            var a = ORSet.Create(_node1, "Z");
            // Replicate it to some node3, i.e. it has dot 'Z'->{node1 -> 1}
            var c = a;
            // Remove Z at node1 replica
            var a2 = a.Remove(_node1, "Z");
            // Add Z at node2, a new replica
            var b = ORSet.Create(_node2, "Z");
            // Replicate b to node1, so now node1 has a Z, the one with a Dot of
            // {node2 -> 1} and version vector of [{node1 -> 1}, {node2 -> 1}]
            var a3 = b.Merge(a2);
            a3.Elements.Should().BeEquivalentTo("Z");
            // Remove the 'Z' at node2 replica
            var b2 = b.Remove(_node2, "Z");
            // Both node3 (c) and node1 (a3) have a 'Z', but when they merge, there should be
            // no 'Z' as node3 (c)'s has been removed by node1 and node1 (a3)'s has been removed by
            // node2
            c.Elements.Should().BeEquivalentTo("Z");
            a3.Elements.Should().BeEquivalentTo("Z");
            b2.Elements.Should().BeEmpty();

            a3.Merge(c).Merge(b2).Elements.Should().BeEmpty();
            a3.Merge(b2).Merge(c).Elements.Should().BeEmpty();
            c.Merge(a3).Merge(b2).Elements.Should().BeEmpty();
            c.Merge(b2).Merge(a3).Elements.Should().BeEmpty();
            b2.Merge(c).Merge(a3).Elements.Should().BeEmpty();
            b2.Merge(a3).Merge(c).Elements.Should().BeEmpty();
        }

        [Fact]
        public void ORSet_must_verify_removed_after_merge_2()
        {
            var a = ORSet.Create(_node1, "Z");
            var b = ORSet.Create(_node2, "Z");
            // replicate node3
            var c = a;
            var a2 = a.Remove(_node1, "Z");
            // replicate b to node1, now node1 has node2's 'Z'
            var a3 = a2.Merge(b);
            a3.Elements.Should().BeEquivalentTo("Z");
            // Remove node2's 'Z'
            var b2 = b.Remove(_node2, "Z");
            // Replicate c to node2, now node2 has node1's old 'Z'
            var b3 = b2.Merge(c);
            b3.Elements.Should().BeEquivalentTo("Z");
            // Merge everytyhing

            a3.Merge(c).Merge(b3).Elements.Should().BeEmpty();
            a3.Merge(b3).Merge(c).Elements.Should().BeEmpty();
            c.Merge(a3).Merge(b3).Elements.Should().BeEmpty();
            c.Merge(b3).Merge(a3).Elements.Should().BeEmpty();
            b3.Merge(c).Merge(a3).Elements.Should().BeEmpty();
            b3.Merge(a3).Merge(c).Elements.Should().BeEmpty();
        }

        #endregion
    }
}
