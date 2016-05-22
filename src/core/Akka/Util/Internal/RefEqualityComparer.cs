using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Akka.Util.Internal
{
    internal class RefEqualityComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly RefEqualityComparer<T> Default = new RefEqualityComparer<T>();

        public bool Equals(T x, T y)
        {
            return ReferenceEquals(x, y);
        }

        public int GetHashCode(T obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
}