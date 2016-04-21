//-----------------------------------------------------------------------
// <copyright file="ObjectExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Streams.Util
{
    public static class ObjectExtensions
    {
        public static bool IsDefaultForType<T>(this T obj)
        {
            return EqualityComparer<T>.Default.Equals(obj, default(T));
        }
    }
}