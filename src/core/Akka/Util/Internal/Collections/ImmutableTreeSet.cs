//-----------------------------------------------------------------------
// <copyright file="ImmutableTreeSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal.Collections
{
	public class ImmutableTreeSet<TValue> : ImmutableAvlTreeBase<TValue, TValue>, IImmutableSet<TValue> where TValue : IComparable<TValue>
	{
		public static readonly ImmutableTreeSet<TValue> Empty = new ImmutableTreeSet<TValue>();

		private ImmutableTreeSet(){}

		private ImmutableTreeSet(ImmutableAvlTreeBase<TValue, TValue>.Node node) : base(node)
		{
			//Intentionally left blank
		}

		bool IImmutableSet<TValue>.TryAdd(TValue value, out IImmutableSet<TValue> newSet)
		{
			ImmutableTreeSet<TValue> newTreeSet;
			var result = TryAdd(value, out newTreeSet);
			newSet = newTreeSet;
			return result;
		}

		public bool TryAdd(TValue value, out ImmutableTreeSet<TValue> newTreeMap)
		{
			Node newNode;
			var couldAdd = TryAdd(value, value, out newNode, AddOperation.AddOnlyUnique);
			if (!couldAdd)
			{
				newTreeMap = this;
				return false;
			}
			newTreeMap = new ImmutableTreeSet<TValue>(newNode);
			return true;
		}


		IImmutableSet<TValue> IImmutableSet<TValue>.Add(TValue value)
		{
			return Add(value);
		}

		public ImmutableTreeSet<TValue> Add(TValue value)
		{
			ImmutableTreeSet<TValue> newTree;
			TryAdd(value, out newTree);
			return newTree;
		}

		public ImmutableTreeSet<TValue> AddOrUpdate(TValue value)
		{
			Node newNode;
			TryAdd(value, value, out newNode, AddOperation.AddOrUpdate);
			var newTreeMap = new ImmutableTreeSet<TValue>(newNode);
			return newTreeMap;
		}

		IImmutableSet<TValue> IImmutableSet<TValue>.Remove(TValue value)
		{
			return Remove(value);
		}

		bool IImmutableSet<TValue>.TryRemove(TValue	 value, out IImmutableSet<TValue> newMap)
		{
			ImmutableTreeSet<TValue> newTreeMap;
			var result = TryRemove(value, out newTreeMap);
			newMap = newTreeMap;
			return result;
		}

		public ImmutableTreeSet<TValue> Remove(TValue value)
		{
			ImmutableTreeSet<TValue> newMap;
			return TryRemove(value, out newMap) ? newMap : this;
		}

		IImmutableSet<TValue> IImmutableSet<TValue>.Remove(IEnumerable<TValue> values)
		{
			return Remove(values);
		}

		public ImmutableTreeSet<TValue> Remove(IEnumerable<TValue> values)
		{
			return values.Aggregate(this, (current, value) => current.Remove(value));
		}

		public bool TryRemove(TValue value, out ImmutableTreeSet<TValue> newMap)
		{
			Node newNode;
			if (TryRemove(value, out newNode))
			{
				newMap = newNode==null ? Empty : new ImmutableTreeSet<TValue>(newNode);
				return true;
			}
			newMap = this;
			return false;
		}


		IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator()
		{
			return AllValuesMinToMax.GetEnumerator();
		}

		IEnumerable<TValue> IImmutableSet<TValue>.AllMinToMax { get { return base.AllValuesMinToMax; } }
		IEnumerable<TValue> IImmutableSet<TValue>.AllMaxToMin { get { return base.AllValuesMaxToMin; } }

		public static ImmutableTreeSet<TValue> Create(TValue value,params TValue[] values)
		{
			var tree = Empty.Add(value);
			return values.Aggregate(tree, (current, v) => current.Add(v));
		}
		public static ImmutableTreeSet<TValue> Create(IEnumerable<TValue> values)
		{
			return values.Aggregate(Empty, (current, v) => current.Add(v));
		}
	}
}

