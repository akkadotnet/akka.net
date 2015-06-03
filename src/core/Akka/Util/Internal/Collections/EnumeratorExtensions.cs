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
