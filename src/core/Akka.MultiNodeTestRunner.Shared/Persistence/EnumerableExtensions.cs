// -----------------------------------------------------------------------
//  <copyright file="EnumerableExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//      Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.MultiNodeTestRunner.Shared.Persistence
{
    public static class EnumerableExtensions
    {
        public static IEnumerable<T> Concat<T>(this IEnumerable<T> source, T item)
        {
            foreach (var cur in source)
            {
                yield return cur;
            }
            yield return item;
        }
    }
}