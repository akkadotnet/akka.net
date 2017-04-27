//-----------------------------------------------------------------------
// <copyright file="CollectionsExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    }
}
