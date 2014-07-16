using System.Collections;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Cluster.Tests
{
    [TestClass]
    public class VectorClockSpec
    {
        [TestMethod]
        public void MustHaveZeroVersionsWhenCreated()
        {
            var clock = VectorClock.Create();
            CollectionAssert.AreEqual(new Dictionary<VectorClock.Node, long>(), clock.Versions);
        }

        [TestMethod]
        public void MustNotHappenBeforeItself()
        {
            var clock1 = VectorClock.Create();
            var clock2 = VectorClock.Create();

            Assert.IsFalse(clock1.IsConcurrentWith(clock2));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest1()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));
            var clock4_1 = clock3_1.Increment(VectorClock.Node.Create("1"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));
            var clock4_2 = clock3_2.Increment(VectorClock.Node.Create("1"));

            Assert.IsFalse(clock4_1.IsConcurrentWith(clock4_2));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest2()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));
            var clock4_1 = clock3_1.Increment(VectorClock.Node.Create("1"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));
            var clock4_2 = clock3_2.Increment(VectorClock.Node.Create("1"));
            var clock5_2 = clock4_2.Increment(VectorClock.Node.Create("3"));

            Assert.IsTrue(clock4_1.IsBefore(clock5_2));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest3()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("2"));

            Assert.IsTrue(clock2_1.IsConcurrentWith(clock2_2));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest4()
        {
            var clock1_3 = VectorClock.Create();
            var clock2_3 = clock1_3.Increment(VectorClock.Node.Create("1"));
            var clock3_3 = clock2_3.Increment(VectorClock.Node.Create("2"));
            var clock4_3 = clock3_3.Increment(VectorClock.Node.Create("1"));

            var clock1_4 = VectorClock.Create();
            var clock2_4 = clock1_4.Increment(VectorClock.Node.Create("1"));
            var clock3_4 = clock2_4.Increment(VectorClock.Node.Create("1"));
            var clock4_4 = clock3_4.Increment(VectorClock.Node.Create("3"));

            Assert.IsTrue(clock4_3.IsConcurrentWith(clock4_4));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest5()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("2"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));
            var clock4_2 = clock3_2.Increment(VectorClock.Node.Create("2"));
            var clock5_2 = clock4_2.Increment(VectorClock.Node.Create("3"));
            
            Assert.IsTrue(clock3_1.IsBefore(clock5_2));
            Assert.IsTrue(clock5_2.IsAfter(clock3_1));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest6()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("1"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("1"));

            Assert.IsTrue(clock3_1.IsConcurrentWith(clock3_2));
            Assert.IsTrue(clock3_2.IsConcurrentWith(clock3_1));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest7()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.Create("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.Create("2"));
            var clock4_1 = clock3_1.Increment(VectorClock.Node.Create("2"));
            var clock5_1 = clock4_1.Increment(VectorClock.Node.Create("3"));

            var clock1_2 = clock4_1;
            var clock2_2 = clock1_2.Increment(VectorClock.Node.Create("2"));
            var clock3_2 = clock2_2.Increment(VectorClock.Node.Create("2"));

            Assert.IsTrue(clock5_1.IsConcurrentWith(clock3_2));
            Assert.IsTrue(clock3_2.IsConcurrentWith(clock5_1));
        }

        [TestMethod]
        public void MustPassMiscComparisonTest8()
        {
            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(VectorClock.Node.FromHash("1"));
            var clock3_1 = clock2_1.Increment(VectorClock.Node.FromHash("3"));

            var clock1_2 = clock3_1.Increment(VectorClock.Node.FromHash("2"));

            var clock4_1 = clock3_1.Increment(VectorClock.Node.FromHash("3"));

            Assert.IsTrue(clock4_1.IsConcurrentWith(clock1_2));
            Assert.IsTrue(clock1_2.IsConcurrentWith(clock4_1));
        }

        [TestMethod]
        public void MustCorrectlyMergeTwoClocks()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");

            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(node1);
            var clock3_1 = clock2_1.Increment(node2);
            var clock4_1 = clock3_1.Increment(node2);
            var clock5_1 = clock4_1.Increment(node3);

            var clock1_2 = clock4_1;
            var clock2_2 = clock1_2.Increment(node2);
            var clock3_2 = clock2_2.Increment(node2);

            var merged1 = clock3_2.Merge(clock5_1);
            Assert.AreEqual(3, merged1.Versions.Count);
            Assert.IsTrue(merged1.Versions.ContainsKey(node1));
            Assert.IsTrue(merged1.Versions.ContainsKey(node2));
            Assert.IsTrue(merged1.Versions.ContainsKey(node3));

            var merged2 = clock5_1.Merge(clock3_2);
            Assert.AreEqual(3, merged2.Versions.Count);
            Assert.IsTrue(merged2.Versions.ContainsKey(node1));
            Assert.IsTrue(merged2.Versions.ContainsKey(node2));
            Assert.IsTrue(merged2.Versions.ContainsKey(node3));

            Assert.IsTrue(clock3_2.IsBefore(merged1));
            Assert.IsTrue(clock5_1.IsBefore(merged1));

            Assert.IsTrue(clock3_2.IsBefore(merged2));
            Assert.IsTrue(clock5_1.IsBefore(merged2));

            Assert.IsTrue(merged1.IsSameAs(merged2));
        }

        [TestMethod]
        public void MustCorrectlyMergeTwoDisjointVectorClocks()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");
            var node4 = VectorClock.Node.Create("4");

            var clock1_1 = VectorClock.Create();
            var clock2_1 = clock1_1.Increment(node1);
            var clock3_1 = clock2_1.Increment(node2);
            var clock4_1 = clock3_1.Increment(node2);
            var clock5_1 = clock4_1.Increment(node3);

            var clock1_2 = VectorClock.Create();
            var clock2_2 = clock1_2.Increment(node4);
            var clock3_2 = clock2_2.Increment(node4);

            var merged1 = clock3_2.Merge(clock5_1);
            Assert.AreEqual(4, merged1.Versions.Count);
            Assert.IsTrue(merged1.Versions.ContainsKey(node1));
            Assert.IsTrue(merged1.Versions.ContainsKey(node2));
            Assert.IsTrue(merged1.Versions.ContainsKey(node3));
            Assert.IsTrue(merged1.Versions.ContainsKey(node4));

            var merged2 = clock5_1.Merge(clock3_2);
            Assert.AreEqual(4, merged2.Versions.Count);
            Assert.IsTrue(merged2.Versions.ContainsKey(node1));
            Assert.IsTrue(merged2.Versions.ContainsKey(node2));
            Assert.IsTrue(merged2.Versions.ContainsKey(node3));
            Assert.IsTrue(merged2.Versions.ContainsKey(node4));

            Assert.IsTrue(clock3_2.IsBefore(merged1));
            Assert.IsTrue(clock5_1.IsBefore(merged1));

            Assert.IsTrue(clock3_2.IsBefore(merged2));
            Assert.IsTrue(clock5_1.IsBefore(merged2));

            Assert.IsTrue(merged1.IsSameAs(merged2));            
        }

        [TestMethod]
        public void MustPassBlankClockIncrementing()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");

            var v1 = VectorClock.Create();
            var v2 = VectorClock.Create();

            var vv1 = v1.Increment(node1);
            var vv2 = v2.Increment(node2);

            Assert.IsTrue(vv1.IsAfter(v1));
            Assert.IsTrue(vv2.IsAfter(v2));

            Assert.IsTrue(vv1.IsAfter(v2));
            Assert.IsTrue(vv2.IsAfter(v1));

            Assert.IsFalse(vv2.IsAfter(vv1));
            Assert.IsFalse(vv1.IsAfter(vv2));
        }

        [TestMethod]
        public void MustPassMergingBehavior()
        {
            var node1 = VectorClock.Node.Create("1");
            var node2 = VectorClock.Node.Create("2");
            var node3 = VectorClock.Node.Create("3");

            var a = VectorClock.Create();
            var b = VectorClock.Create();

            var a1 = a.Increment(node1);
            var b1 = b.Increment(node2);

            var a2 = a1.Increment(node1);
            var c = a2.Merge(b1);
            var c1 = c.Increment(node3);

            Assert.IsTrue(c1.IsAfter(a2));
            Assert.IsTrue(c1.IsAfter(b1));
        }
    }
}
