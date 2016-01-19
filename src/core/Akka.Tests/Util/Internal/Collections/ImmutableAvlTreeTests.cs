//-----------------------------------------------------------------------
// <copyright file="ImmutableAvlTreeTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.TestKit;
using Akka.Util.Internal.Collections;
using Xunit;

namespace Akka.Tests.Util.Internal.Collections
{
    public class ImmutableAvlTreeTests
    {

        [Fact]
        public void CanGetSuccessfully()
        {
            var m = ImmutableAvlTree<int, int>.Empty
                .Add(1, 11)
                .Add(2, 22)
                .Add(3, 33);

            Assert.Equal(11, m[1]);
            Assert.Equal(22, m[2]);
            Assert.Equal(33, m[3]);
        }

        [Fact]
        public void LLCase()
        {
            var m = ImmutableAvlTree<int, int>.Empty
                .Add(5, 1)
                .Add(4, 2)
                .Add(3, 3);

            Assert.Equal(4, m.Root.Key);
            Assert.Equal(3, m.Root.Left.Key);
            Assert.Equal(5, m.Root.Right.Key);
        }

        [Fact]
        public void TreeRemainsBalancedAfterUnbalancedInsertIntoBalancedTree()
        {
            var m = ImmutableAvlTree<int, int>.Empty
                .Add(5, 1)
                .Add(4, 2)
                .Add(3, 3)
                .Add(2, 4)
                .Add(1, 5);

            Assert.Equal(4, m.Root.Key);
            Assert.Equal(2, m.Root.Left.Key);
            Assert.Equal(1, m.Root.Left.Left.Key);
            Assert.Equal(3, m.Root.Left.Right.Key);
            Assert.Equal(5, m.Root.Right.Key);
        }

        [Fact]
        public void LRCase()
        {
            var m = ImmutableAvlTree<int, int>.Empty
                .Add(5, 1)
                .Add(3, 2)
                .Add(4, 3);

            Assert.Equal(4, m.Root.Key);
            Assert.Equal(3, m.Root.Left.Key);
            Assert.Equal(5, m.Root.Right.Key);
        }

        [Fact]
        public void RRCase()
        {
            var m = ImmutableAvlTree<int, int>.Empty
                .Add(3, 1)
                .Add(4, 2)
                .Add(5, 3);

            Assert.Equal(4, m.Root.Key);
            Assert.Equal(3, m.Root.Left.Key);
            Assert.Equal(5, m.Root.Right.Key);
        }

        [Fact]
        public void RLCase()
        {
            var m = ImmutableAvlTree<int, int>.Empty
                .Add(3, 1)
                .Add(5, 2)
                .Add(4, 3);

            Assert.Equal(4, m.Root.Key);
            Assert.Equal(3, m.Root.Left.Key);
            Assert.Equal(5, m.Root.Right.Key);
        }

        [Fact]
        public void EnumerateInOrder()
        {
            var m = ImmutableAvlTree<int, string>.Empty
                .Add(3, "c")
                .Add(2, "b")
                .Add(1, "a")
                .Add(5, "e")
                .Add(6, "f")
                .Add(0, "");
            m.AllMinToMax.Select(kvp => kvp.Value).ShouldOnlyContainInOrder("", "a", "b", "c", "e", "f");
        }

        [Fact]
        public void EnumerateReversedInOrder()
        {
            var m = ImmutableAvlTree<int, string>.Empty
                .Add(3, "c")
                .Add(2, "b")
                .Add(1, "a")
                .Add(5, "e")
                .Add(6, "f")
                .Add(0, "");
            m.AllMaxToMin.Select(kvp => kvp.Value).ShouldOnlyContainInOrder("f", "e", "c", "b", "a", "");
        }


        [Fact]
        public void AddingSamekeyTwiceAreAllowed()
        {
            var tree = ImmutableAvlTree<int, string>.Empty
                .Add(3, "c")
                .Add(2, "b");

            //Should not fail:
            tree.Add(3, "XXXX");
        }

        [Fact]
        public void WhenRemovingNodeThatCausesUnbalanceTheTreeIsRebalanced()
        {
            var tree = ImmutableAvlTree<int, int>.Empty
                .Add(17, 17)
                .Add(32, 32)
                .Add(44, 44)
                .Add(48, 48)
                .Add(50, 50)
                .Add(62, 62)
                .Add(78, 78)
                .Add(88, 88);
            Assert.Equal(48, tree.Root.Key);
            Assert.Equal(32, tree.Root.Left.Key);
            Assert.Equal(17, tree.Root.Left.Left.Key);
            Assert.Equal(44, tree.Root.Left.Right.Key);
            Assert.Equal(62, tree.Root.Right.Key);
            Assert.Equal(50, tree.Root.Right.Left.Key);
            Assert.Equal(78, tree.Root.Right.Right.Key);
            Assert.Equal(88, tree.Root.Right.Right.Right.Key);

            ImmutableAvlTree<int, int> newTree;
            tree.TryRemove(50, out newTree).ShouldBeTrue();

            Assert.Equal(48, newTree.Root.Key);
            Assert.Equal(32, newTree.Root.Left.Key);
            Assert.Equal(17, newTree.Root.Left.Left.Key);
            Assert.Equal(44, newTree.Root.Left.Right.Key);
            Assert.Equal(78, newTree.Root.Right.Key);
            Assert.Equal(62, newTree.Root.Right.Left.Key);
            Assert.Equal(88, newTree.Root.Right.Right.Key);

        }

        private static int TreeImbalance(ImmutableAvlTree<int, int> tree)
        {
            var lh = NodeHeight(tree.Root.Left);
            var rh = NodeHeight(tree.Root.Right);
            return lh - rh;
        }

        private static int NodeHeight(IBinaryTreeNode<int, int> tree)
        {
            if (tree == null) return 0;
            return Math.Max(NodeHeight(tree.Left), NodeHeight(tree.Right)) + 1;
        }

        [Fact]
        public void WhenRemovingNodeThatCausesLeftThenRightHeavyUnbalanceTheTreeIsRebalanced()
        {
            var tree = ImmutableAvlTree<int, int>.Empty
                .Add(60, 6)
                .Add(20, 2)
                .Add(80, 8)
                .Add(10, 1)
                .Add(70, 7)
                .Add(40, 4)
                .Add(30, 3)
                .Add(50, 5);
            Assert.Equal(60, tree.Root.Key);
            Assert.Equal(20, tree.Root.Left.Key);
            Assert.Equal(10, tree.Root.Left.Left.Key);
            Assert.Equal(40, tree.Root.Left.Right.Key);
            Assert.Equal(80, tree.Root.Right.Key);
            Assert.Equal(70, tree.Root.Right.Left.Key);
            Assert.Null(tree.Root.Right.Right);
            tree = tree.Remove(70);

            Assert.InRange(TreeImbalance(tree), -1, 1);

            Assert.Equal(40, tree.Root.Key);
            Assert.Equal(20, tree.Root.Left.Key);
            Assert.Equal(60, tree.Root.Right.Key);
            Assert.Equal(10, tree.Root.Left.Left.Key);
            Assert.Equal(30, tree.Root.Left.Right.Key);
            Assert.Equal(50, tree.Root.Right.Left.Key);
            Assert.Equal(80, tree.Root.Right.Right.Key);
        }

        [Fact]
        public void WhenRemovingNodeThatCausesRightThenLeftHeavyUnbalanceTheTreeIsRebalanced()
        {
            var tree = ImmutableAvlTree<int, int>.Empty
                .Add(-60, 6)
                .Add(-20, 2)
                .Add(-80, 8)
                .Add(-10, 1)
                .Add(-70, 7)
                .Add(-40, 4)
                .Add(-30, 3)
                .Add(-50, 5);
            Assert.Equal(-60, tree.Root.Key);
            Assert.Equal(-20, tree.Root.Right.Key);
            Assert.Equal(-10, tree.Root.Right.Right.Key);
            Assert.Equal(-40, tree.Root.Right.Left.Key);
            Assert.Equal(-80, tree.Root.Left.Key);
            Assert.Equal(-70, tree.Root.Left.Right.Key);
            Assert.Null(tree.Root.Left.Left);
            tree = tree.Remove(-70);

            Assert.InRange(TreeImbalance(tree), -1, 1);

            Assert.Equal(-40, tree.Root.Key);
            Assert.Equal(-20, tree.Root.Right.Key);
            Assert.Equal(-60, tree.Root.Left.Key);
            Assert.Equal(-10, tree.Root.Right.Right.Key);
            Assert.Equal(-30, tree.Root.Right.Left.Key);
            Assert.Equal(-50, tree.Root.Left.Right.Key);
            Assert.Equal(-80, tree.Root.Left.Left.Key);
        }

        [Fact]
        public void WhenRemovingNodeThatDoNotExistSameTreeIsReturned()
        {
            var tree = ImmutableAvlTree<int, int>.Empty
                .Add(17, 17)
                .Add(32, 32)
                .Add(44, 44)
                .Add(48, 48)
                .Add(50, 50)
                .Add(62, 62)
                .Add(78, 78)
                .Add(88, 88);

            ImmutableAvlTree<int, int> newTree;
            tree.TryRemove(-4711, out newTree).ShouldBeFalse();
            Assert.Same(tree, newTree);

        }


        [Fact]
        public void Removing_last_item_returns_empty_map()
        {
            var tree = ImmutableAvlTree<int, string>.Empty
                .Add(1, "a");

            ImmutableAvlTree<int, string> removedTree;
            tree.TryRemove(1, out removedTree).ShouldBeTrue();
            removedTree.IsEmpty.ShouldBeTrue();

            ImmutableAvlTree<int, string> removedTree2;
            removedTree.TryRemove(1, out removedTree2).ShouldBeFalse();
            removedTree2.IsEmpty.ShouldBeTrue();
        }
    }
}

