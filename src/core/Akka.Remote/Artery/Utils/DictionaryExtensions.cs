using System;
using System.Collections.Generic;
using System.Text;
using Akka.Event;

namespace Akka.Remote.Artery.Utils
{
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Get a value for a given key, or if it does not exist then default the value via a
        /// <see cref="Func{TKey, TResult}"/> and put it in the Dictionary.
        /// </summary>
        /// <typeparam name="TKey">key type of Dictionary</typeparam>
        /// <typeparam name="TValue">value type of Dictionary</typeparam>
        /// <param name="dict">the <see cref="IDictionary{TKey,TValue}"/> to search on.</param>
        /// <param name="key">to search on.</param>
        /// <param name="mappingFunction">to provide a value if <see cref="IDictionary{TKey,TValue}.TryGetValue"/> returns false.</param>
        /// <returns>the value if found otherwise the default.</returns>
        public static TValue ComputeIfAbsent<TKey, TValue>(
            this IDictionary<TKey, TValue> dict,
            TKey key,
            Func<TKey, TValue> mappingFunction)
        {
            if (dict.TryGetValue(key, out var value))
                return value;

            value = mappingFunction(key);
            if (!value.Equals(default(TValue)))
                dict.Add(key, value);

            return value;
        }

        public static string DebugString<TKey, TValue>(this IDictionary<TKey, TValue> dict)
        {
            var sb = new StringBuilder($"[{Logging.SimpleName(dict.GetType())}]: {{");
            foreach (var kvp in dict)
            {
                sb.AppendLine($"  {kvp.Key}: {kvp.Value},");
            }

            return sb.AppendLine("}}").ToString();
        }

    }
}
