//-----------------------------------------------------------------------
// <copyright file="EnumeratorExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Util.Internal.Collections
{
    public static class EnumeratorExtensions
    {
        public static Iterator<T> Iterator<T>(this IEnumerable<T> enumerable)
        {
            return new Iterator<T>(enumerable);
        }
    }
}
