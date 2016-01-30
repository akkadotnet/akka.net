//-----------------------------------------------------------------------
// <copyright file="IImmutableSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace Akka.Util.Internal.Collections
{
	public interface IImmutableSet<TValue> : IEnumerable<TValue> where TValue : IComparable<TValue>
	{
		bool IsEmpty { get; }
		int Count { get; }


		[Pure]
		bool Contains(TValue value);

		bool TryAdd(TValue value, out IImmutableSet<TValue> newMap);

		[Pure]
		IImmutableSet<TValue> Add(TValue value);


		IEnumerable<TValue> AllMinToMax { get; }
		IEnumerable<TValue> AllMaxToMin { get; }

		[Pure]
		IImmutableSet<TValue> Remove(TValue value);

		[Pure]
		IImmutableSet<TValue> Remove(IEnumerable<TValue> values);

		bool TryRemove(TValue value, out IImmutableSet<TValue> newMap);
	}

	public static class ImmutableSetExtensions
	{
		[Pure]
		public static IImmutableSet<TValue> Remove<TValue>(this IImmutableSet<TValue> set, Predicate<TValue> itemsToRemove) where TValue : IComparable<TValue>
		{
			return set.Remove(set.Where(v => itemsToRemove(v)));
		}
	}
}

