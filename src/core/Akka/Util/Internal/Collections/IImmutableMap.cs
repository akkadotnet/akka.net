//-----------------------------------------------------------------------
// <copyright file="IImmutableMap.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Util.Internal.Collections
{
    public interface IImmutableMap<TKey, TValue> : IEnumerable<IKeyValuePair<TKey, TValue>> where TKey : IComparable<TKey>
	{
		bool IsEmpty { get; }
		int Count { get; }

		bool TryGet(TKey key, out TValue value);
		TValue this[TKey key] { get; }

		bool Contains(TKey key);

		bool TryAdd(TKey key, TValue value, out IImmutableMap<TKey, TValue> newMap);
		IImmutableMap<TKey, TValue> Add(TKey key, TValue value);

		IImmutableMap<TKey, TValue> AddOrUpdate(TKey key, TValue value);

		IEnumerable<IKeyValuePair<TKey, TValue>> AllMinToMax { get; }
		IEnumerable<IKeyValuePair<TKey, TValue>> AllMaxToMin { get; }

		IEnumerable<TKey> AllKeysMinToMax { get; }
		IEnumerable<TKey> AllKeysMaxToMin { get; }

		IEnumerable<TValue> AllValuesMinToMax { get; }
		IEnumerable<TValue> AllValuesMaxToMin { get; }

        IImmutableMap<TKey, TValue> Remove(TKey key);
        IImmutableMap<TKey, TValue> Remove(IEnumerable<TKey> keys);
		bool TryRemove(TKey key, out IImmutableMap<TKey, TValue> newMap);
	    IImmutableMap<TKey, TValue> Range(TKey from, TKey to);
	    IImmutableMap<TKey, TValue> Concat(IEnumerable<IKeyValuePair<TKey, TValue>> other);
	}
}

