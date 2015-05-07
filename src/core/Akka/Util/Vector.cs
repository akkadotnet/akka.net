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
