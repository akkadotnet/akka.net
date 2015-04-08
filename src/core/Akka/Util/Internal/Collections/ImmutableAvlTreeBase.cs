//-----------------------------------------------------------------------
// <copyright file="ImmutableAvlTreeBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Util.Internal.Collections
{
	/// <summary>
	/// An immutable AVL tree.
	/// Originally from http://justinmchase.com/2011/12/13/immutable-avl-tree-in-c/
	/// </summary>
	/// <typeparam name="TKey">The type of the keys.</typeparam>
	/// <typeparam name="TValue">The type of the values.</typeparam>
	public abstract class ImmutableAvlTreeBase<TKey, TValue> : IEnumerable<IKeyValuePair<TKey, TValue>> where TKey : IComparable<TKey>
	{
		private readonly Node _node;


		public IBinaryTreeNode<TKey, TValue> Root
		{
			get { return _node; }
		}

		protected ImmutableAvlTreeBase()
		{
			_node = null;
		}

		protected ImmutableAvlTreeBase(Node node)
		{
			if (node == null) throw new ArgumentNullException("node");
			_node = node;
		}

		public bool IsEmpty { get { return _node == null; } }
		public int Count { get { return _node == null ? 0 : _node.Count; } }

		public bool Contains(TKey key)
		{
			Node node;
			var wasFound = TryGet(key, _node, out node);
			return wasFound;
		}

		public bool TryGet(TKey key, out TValue value)
		{
			Node node;
			var wasFound = TryGet(key, _node, out node);
			if (wasFound)
			{
				value = node.Value;
				return true;
			}
			value = default(TValue);
			return false;
		}

		public TValue this[TKey key]
		{
			get
			{
				TValue value;
				TryGet(key, out value);
				return value;
			}
		}


		private static bool TryGet(TKey key, Node startNode, out Node node)
		{
			var currentNode = startNode;
			while (currentNode != null)
			{
				var comparison = currentNode.Key.CompareTo(key);
				if (comparison == 0)
				{
					node = currentNode;
					return true;
				}
				if (comparison > 0)
					currentNode = currentNode.Left;
				else
					currentNode = currentNode.Right;
			}
			node = null;
			return false;
		}

		protected bool TryAdd(TKey key, TValue value, out Node newNode, AddOperation typeOfAdd)
		{
			return TryAdd(_node, key, value, out newNode, typeOfAdd);
		}

		protected enum AddOperation { Add, AddOrUpdate, AddOnlyUnique }
		protected static bool TryAdd(Node node, TKey key, TValue value, out Node newNode, AddOperation typeOfAdd)
		{
			if (node == null)
			{
				newNode = new Node(key, value, null, null);
				return true;
			}

			var comparison = node.Key.CompareTo(key);
			var keyAlreadyExists = comparison == 0;
			if (keyAlreadyExists)
			{
				switch (typeOfAdd)
				{

					case AddOperation.AddOrUpdate:
						newNode = new Node(key, value, node.Left, node.Right);
						return true;
					case AddOperation.AddOnlyUnique:
						newNode = null;
						return false;
                    //case AddOperation.Add:
                    //default:
                    //    //Intentionally left blank
                    //    break;
				}
			}

			var l = node.Left;
			var r = node.Right;

			if (comparison > 0)
			{
				Node nd;
				var couldAdd = TryAdd(node.Left, key, value, out nd, typeOfAdd);
				if (!couldAdd)
				{
					newNode = null;
					return false;
				}
				l = nd;
			}
			else
			{
				Node nd;
				var couldAdd = TryAdd(node.Right, key, value, out nd, typeOfAdd);
				if (!couldAdd)
				{
					newNode = null;
					return false;
				}
				r = nd;
			}

			var n = new Node(node.Key, node.Value, l, r);
			var lh = n.Left == null ? 0 : n.Left.Height;
			var rh = n.Right == null ? 0 : n.Right.Height;
			var b = lh - rh;

			if (Math.Abs(b) == 2) // 2 or -2 means unbalanced
			{
				if (b == 2) // L
				{
					var llh = n.Left.Left == null ? 0 : n.Left.Left.Height;
					var lrh = n.Left.Right == null ? 0 : n.Left.Right.Height;
					var lb = llh - lrh;
					if (lb == 1) // LL
					{
						// rotate right
						n = RotateRight(n);
					}
					else // LR
					{
						// rotate left
						// rotate right
						l = RotateLeft(l);
						n = new Node(n.Key, n.Value, l, r);
						n = RotateRight(n);
					}
				}
				else // R
				{
					var rlh = n.Right.Left == null ? 0 : n.Right.Left.Height;
					var rrh = n.Right.Right == null ? 0 : n.Right.Right.Height;
					var rb = rlh - rrh;
					if (rb == 1) // RL
					{
						// rotate right
						// rotate left
						r = RotateRight(r);
						n = new Node(n.Key, n.Value, l, r);
						n = RotateLeft(n);
					}
					else // RR
					{
						// rotate left
						n = RotateLeft(n);
					}
				}
			}
			newNode = n;
			return true;
		}

		private static Node RotateRight(Node node)
		{
			//       (5)            4     
			//       / \           / \    
			//      4   D         /   \   
			//     / \           3     5  
			//    3   C    -->  / \   / \ 
			//   / \           A   B C   D
			//  A   B                     

			var L = node.Left.Left;
			var R = new Node(node.Key, node.Value, node.Left.Right, node.Right);
			var N = new Node(node.Left.Key, node.Left.Value, L, R);
			return N;
		}

		private static Node RotateLeft(Node node)
		{
			//    (3)               4     
			//    / \              / \    
			//   A   4            /   \   
			//      / \          3     5  
			//     B   5   -->  / \   / \ 
			//        / \      A   B C   D
			//       C   D                

			var L = new Node(node.Key, node.Value, node.Left, node.Right.Left);
			var R = node.Right.Right;
			var N = new Node(node.Right.Key, node.Right.Value, L, R);
			return N;
		}

		private static Node DoubleRightRotation(Node node)
		{
			if (node.Left == null) return node;
			var rotatedLeftChild = new Node(node.Key, node.Value, RotateLeft(node.Left), node.Right);
			return RotateRight(rotatedLeftChild);
		}
		private static Node DoubleLeftRotation(Node node)
		{
			if (node.Right == null) return node;
			var rotatedRightChild = new Node(node.Key, node.Value, node.Left, RotateRight(node.Right));
			return RotateLeft(rotatedRightChild);
		}

		public IEnumerable<IKeyValuePair<TKey, TValue>> AllMinToMax { get { return EnumerateMinToMax(); } }
		public IEnumerable<IKeyValuePair<TKey, TValue>> AllMaxToMin { get { return EnumerateMaxToMin(); } }

		public IEnumerable<TValue> AllValuesMinToMax { get { return EnumerateMinToMax().Select(kvp => kvp.Value); } }
		public IEnumerable<TValue> AllValuesMaxToMin { get { return EnumerateMaxToMin().Select(kvp => kvp.Value); ; } }

		public IEnumerable<TKey> AllKeysMinToMax { get { return EnumerateMinToMax().Select(kvp => kvp.Key); } }
		public IEnumerable<TKey> AllKeysMaxToMin { get { return EnumerateMaxToMin().Select(kvp => kvp.Key); ; } }



		public IEnumerator<IKeyValuePair<TKey, TValue>> GetEnumerator()
		{
			return EnumerateMinToMax().GetEnumerator();
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		private IEnumerable<IKeyValuePair<TKey, TValue>> EnumerateMinToMax()
		{
			var stack = new Stack<Node>(8);
			for (var current = _node; current != null || stack.Count != 0; current = current.Right)
			{
				while (current != null)
				{
					stack.Push(current);
					current = current.Left;
				}
				current = stack.Pop();
				yield return current;
			}
		}
		private IEnumerable<IKeyValuePair<TKey, TValue>> EnumerateMaxToMin()
		{
			var stack = new Stack<Node>(8);
			for (var current = _node; current != null || stack.Count != 0; current = current.Left)
			{
				while (current != null)
				{
					stack.Push(current);
					current = current.Right;
				}
				current = stack.Pop();
				yield return current;
			}
		}

		public override string ToString()
		{
			return _node.ToString();
		}

		public class Node : IBinaryTreeNode<TKey, TValue>
		{
			private readonly Node _left;
			private readonly Node _right;
			private readonly int _height;
			private readonly TKey _key;
			private readonly TValue _value;
			private readonly int _count;

			public TKey Key { get { return _key; } }
			public TValue Value { get { return _value; } }
			public int Height { get { return _height; } }
			public Node Left { get { return _left; } }
			public Node Right { get { return _right; } }
			public int Count { get { return _count; } }

			IBinaryTreeNode<TKey, TValue> IBinaryTreeNode<TKey, TValue>.Left { get { return _left; } }
			IBinaryTreeNode<TKey, TValue> IBinaryTreeNode<TKey, TValue>.Right { get { return _right; } }

			public Node(TKey key, TValue value, Node left, Node right)
			{
				_key = key;
				_value = value;
				_left = left;
				_right = right;

				var leftHeight = 0;
				var leftCount = 0;
				var rightHeight = 0;
				var rightCount = 0;
				if (left != null)
				{
					leftHeight = left.Height;
					leftCount = left.Count;
				} if (right != null)
				{
					rightHeight = right.Height;
					rightCount = right.Count;
				}
				_height = Math.Max(leftHeight, rightHeight) + 1;
				_count = leftCount + rightCount + 1;
			}

			public override string ToString()
			{
				var sb = new StringBuilder();
				sb.Append('<').Append(_key).Append(", ").Append(_value).Append('>');
				if (_left != null) sb.Append(" L=<").Append(_left.Key).Append('>');
				if (_right != null) sb.Append(" R=<").Append(_right.Key).Append('>');
				return sb.ToString();
			}
		}

		protected bool TryRemove(TKey key, out Node newNode)
		{
			if (_node == null)
			{
				newNode = null;
				return false;
			}
			return TryRemove(key, _node, out newNode);
		}

		private bool TryRemove(TKey key, Node node, out Node newNode)
		{
			Node result;
			int compare = key.CompareTo(node.Key);
			var left = node.Left;
			var right = node.Right;
			if (compare == 0)
			{
				var rightIsEmpty = right == null;
				if (rightIsEmpty)
				{
					newNode = left;	//Note, if node is a leaf, Left will be null too.
					return true;
				}
				var leftIsEmpty = left == null;

				if (leftIsEmpty)
				{
					newNode = right;
					return true;
				}
				// We have two children. Remove the next-highest node and replace
				// this node with it.
				var successor = right;
				while (successor.Left != null)
				{
					successor = successor.Left;
				}
				Node newRight;
				TryRemove(successor.Key, right, out newRight);	//right is always!=null
				result = new Node(successor.Key, successor.Value, left, newRight);
			}
			else
			{
				if (compare < 0)
				{
					Node newLeft = null;
					var wasRemovedFromLeft = false;
					if (left != null)
					{
						wasRemovedFromLeft = TryRemove(key, left, out newLeft);
					}
					if (!wasRemovedFromLeft)
					{
						newNode = null;
						return false;
					}
					result = new Node(node.Key, node.Value, newLeft, right);
				}
				else
				{
					Node newRight = null;
					var wasRemovedFromRight = false;
					if (right != null)
					{
						wasRemovedFromRight = TryRemove(key, right, out newRight);
					}
					if (!wasRemovedFromRight)
					{
						newNode = null;
						return false;
					}
					result = new Node(node.Key, node.Value, left, newRight);
				}
			}
			newNode = MakeBalanced(result);
			return true;
		}

		private Node MakeBalanced(Node tree)
		{
			Node result;
			if (IsRightHeavy(tree))
			{
				result = IsLeftHeavy(tree.Right) ? DoubleLeftRotation(tree) : RotateLeft(tree);
			}
			else if (IsLeftHeavy(tree))
			{
				result = IsRightHeavy(tree.Left) ? DoubleRightRotation(tree) : RotateRight(tree);
			}
			else
				result = tree;
			return result;
		}

		private bool IsRightHeavy(Node tree) { return Balance(tree) >= 2; }
		private bool IsLeftHeavy(Node tree) { return Balance(tree) <= -2; }

		private int Balance(Node tree)
		{
			if (tree == null) return 0;
			var right = tree.Right;
			var rightHeight = right == null ? 0 : right.Height;
			var left = tree.Left;
			var leftHeight = left == null ? 0 : left.Height;
			return rightHeight - leftHeight;
		}
	}
}

