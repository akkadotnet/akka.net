//-----------------------------------------------------------------------
// <copyright file="ImmutableAvlTree.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util.Internal.Collections
{
	/// <summary>
	/// An immutable AVL tree.
	/// Originally from http://justinmchase.com/2011/12/13/immutable-avl-tree-in-c/
	/// </summary>
	/// <typeparam name="TKey">The type of the keys.</typeparam>
	/// <typeparam name="TValue">The type of the values.</typeparam>
	public sealed class ImmutableAvlTree<TKey, TValue> : ImmutableAvlTreeBase<TKey,TValue> where TKey : IComparable<TKey>
	{
		public static readonly ImmutableAvlTree<TKey, TValue> Empty = new ImmutableAvlTree<TKey, TValue>();

		private ImmutableAvlTree(){}

		private ImmutableAvlTree(ImmutableAvlTreeBase<TKey, TValue>.Node node) : base(node)
		{
			//Intentionally left blank
		}


		public ImmutableAvlTree<TKey, TValue> Add(TKey key, TValue value)
		{
			Node newNode;
			TryAdd(key, value, out newNode, AddOperation.Add);
			return new ImmutableAvlTree<TKey, TValue>(newNode);
		}


		public ImmutableAvlTree<TKey, TValue> AddOrUpdate(TKey key, TValue value)
		{
			Node newNode;
			TryAdd(key, value, out newNode, AddOperation.AddOrUpdate);
			return new ImmutableAvlTree<TKey, TValue>(newNode);
		}


		public ImmutableAvlTree<TKey, TValue> Remove(TKey key)
		{
			ImmutableAvlTree<TKey, TValue> newMap;
			return TryRemove(key, out newMap) ? newMap : this;
		}

		public bool TryRemove(TKey key, out ImmutableAvlTree<TKey, TValue> newMap)
		{
			Node newNode;
			if (TryRemove(key, out newNode))
			{
                newMap = newNode == null ? Empty : new ImmutableAvlTree<TKey, TValue>(newNode);
                return true;
			}
			newMap = this;
			return false;
		}
	}
}
