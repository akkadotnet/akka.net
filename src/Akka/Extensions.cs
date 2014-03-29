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

        /// <summary>
        /// Dictionary helper that allows for idempotent updates. You don't need to care whether or not
        /// this item is already in the collection in order to update it.
        /// </summary>
        public static void AddOrSet<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue value)
        {
            if (hash.ContainsKey(key))
                hash[key] = value;
            else
                hash.Add(key,value);
        }
    }
}