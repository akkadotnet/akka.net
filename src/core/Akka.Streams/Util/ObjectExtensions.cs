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