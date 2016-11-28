using System.Collections.Generic;

namespace Akka.Persistence.Sql.Common.Journal
{
    internal static class MultiValueDictionaryExtensions
    {
        public static void AddItem<TKey, TVal>(this Dictionary<TKey, HashSet<TVal>> dictionary, TKey key, TVal item)
        {
            HashSet<TVal> bucket;
            if (!dictionary.TryGetValue(key, out bucket))
            {
                bucket = new HashSet<TVal>();
                dictionary.Add(key, bucket);
            }

            bucket.Add(item);
        }

        public static void RemoveItem<TKey, TVal>(this Dictionary<TKey, HashSet<TVal>> dictionary, TKey key, TVal item)
        {
            HashSet<TVal> bucket;
            if (dictionary.TryGetValue(key, out bucket))
            {
                if (bucket.Remove(item) && bucket.Count == 0)
                {
                    dictionary.Remove(key);
                }
            }
        }

        public static void RemoveItem<TKey, TVal>(this Dictionary<TKey, HashSet<TVal>> dictionary, TVal item)
        {
            foreach (var entry in dictionary)
            {
                entry.Value.Remove(item);
            }
        }
    }
}