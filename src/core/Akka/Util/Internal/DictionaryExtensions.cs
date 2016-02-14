using System.Collections.Generic;

namespace Akka.Util.Internal
{
    public static class DictionaryExtensions
    {
        public static void Put<TKey, TVal>(this IDictionary<TKey, TVal> dict, TKey key, TVal value)
        {
            if (dict.ContainsKey(key))
                dict[key] = value;
            else
                dict.Add(key, value);
        }

        public static bool TryAdd<TKey, TVal>(this IDictionary<TKey, TVal> dict, TKey key, TVal value)
        {
            if (dict.ContainsKey(key))
                return false;

            dict.Add(key, value);
            return true;
        }
    }
}