using System.Collections.Generic;
using System.Linq;

namespace Akka
{
    public static class Extensions
    {
        public static T AsInstanceOf<T>(this object self)
        {
            return (T) self;
        }

        public static T[] Add<T>(this T[] self, T add)
        {
            return self.Union(Enumerable.Repeat(add, 1)).ToArray();
        }

        public static IEnumerable<T> Drop<T>(this IEnumerable<T> self, int count)
        {
            return self.Skip(count);
        }

        public static T Head<T>(this IEnumerable<T> self)
        {
            return self.FirstOrDefault();
        }

        public static string Join(this IEnumerable<string> self, string separator)
        {
            return string.Join(separator, self);
        }
    }
}