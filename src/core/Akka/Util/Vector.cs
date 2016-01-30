//-----------------------------------------------------------------------
// <copyright file="Vector.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    public static class Vector
    {
        public static Func<Func<T>, IList<T>> Fill<T>(int number)
        {
            return func => Enumerable
                .Range(1, number)
                .Select(_ => func())
                .ToList();
        } 
    }
}
