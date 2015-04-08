//-----------------------------------------------------------------------
// <copyright file="ImmutableAvlTreeMap.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util.Internal.Collections
{
	public sealed class ImmutableTreeMap<TKey, TValue> : ImmutableAvlTreeBase<TKey, TValue>, IImmutableMap<TKey, TValue> where TKey : IComparable<TKey>
	{
		public static readonly ImmutableTreeMap<TKey, TValue> Empty = new ImmutableTreeMap<TKey, TValue>();

		private ImmutableTreeMap(){}

		private ImmutableTreeMap(ImmutableAvlTreeBase<TKey, TValue>.Node node) : base(node)
		{
			//Intentionally left blank
		}

		bool IImmutableMap<TKey, TValue>.TryAdd(TKey key, TValue value, out IImmutableMap<TKey, TValue> newTreeMap)
		{
			ImmutableTreeMap<TKey, TValue> newMap;
			var result=TryAdd(key, value, out newMap);
			newTreeMap = newMap;
			return result;
		}

		IImmutableMap<TKey, TValue> IImmutableMap<TKey, TValue>.Add(TKey key, TValue value)
		{
			return Add(key, value);
		}

		public bool TryAdd(TKey key, TValue value, out ImmutableTreeMap<TKey, TValue> newTreeMap)
		{
			Node newNode;
			var couldAdd = TryAdd(key, value, out newNode, AddOperation.AddOnlyUnique);
			if(!couldAdd)
			{
				newTreeMap = this;
				return false;
			}
			newTreeMap = new ImmutableTreeMap<TKey, TValue>(newNode);
			return true;
		}

		public ImmutableTreeMap<TKey, TValue> Add(TKey key, TValue value)
		{
			ImmutableTreeMap<TKey, TValue> newTree;
			var couldAdd = TryAdd(key, value, out newTree);
			if(!couldAdd) throw new InvalidOperationException(string.Format("Duplicate keys are not allowed. The tree already contains the key \"{0}\".", key));
			return newTree;
		}

		public ImmutableTreeMap<TKey, TValue> AddOrUpdate(TKey key, TValue value)
		{
			Node newNode;
			TryAdd(key, value, out newNode, AddOperation.AddOrUpdate);
			return new ImmutableTreeMap<TKey, TValue>(newNode);
		}


		IImmutableMap<TKey, TValue> IImmutableMap<TKey, TValue>.AddOrUpdate(TKey key, TValue value)
		{
			return AddOrUpdate(key, value);
		}



		IImmutableMap<TKey, TValue> IImmutableMap<TKey, TValue>.Remove(TKey key)
		{
			return Remove(key);
		}

		bool IImmutableMap<TKey, TValue>.TryRemove(TKey key, out IImmutableMap<TKey, TValue> newMap)
		{
			ImmutableTreeMap<TKey, TValue> newTreeMap;
			var result = TryRemove(key, out newTreeMap);
			newMap = newTreeMap;
			return result;
		}

		public ImmutableTreeMap<TKey, TValue> Remove(TKey key)
		{
			ImmutableTreeMap<TKey, TValue> newMap;
			return TryRemove(key, out newMap) ? newMap : this;
		}

		public bool TryRemove(TKey key, out ImmutableTreeMap<TKey, TValue> newMap)
		{
			Node newNode;
			if(TryRemove(key, out newNode))
			{
				newMap=newNode==null ? Empty : new ImmutableTreeMap<TKey, TValue>(newNode);
				return true;
			}
			newMap = this;
			return false;
		}
	}
}
