//-----------------------------------------------------------------------
// <copyright file="CollectionsExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akka.Persistence.Internal
{
    internal static class CollectionsExtensions
    {
        public static ImmutableArray<T> AddFirst<T>(this ImmutableArray<T> @this, T item)
        {
            var builder = ImmutableArray.CreateBuilder<T>();
            builder.Add(item);
            builder.AddRange(@this);
            return builder.ToImmutable();
        }

        /// <summary>
        /// Removes first element from the list and returns it or returns default value if list was empty.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        public static T Pop<T>(this LinkedList<T> self)
        {
            if (self.First != null)
            {
                var first = self.First.Value;
                self.RemoveFirst();
                return first;
            }

            return default(T);
        }
    }
}
