using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization.Formatters;

namespace Akka
{
    public static class Extensions
    {
        public static T AsInstanceOf<T>(this object self)
        {
            return (T) self;
        }

        /// <summary>
        /// Scala alias for Skip
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="self"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public static IEnumerable<T> Drop<T>(this IEnumerable<T> self, int count)
        {
            return self.Skip(count).ToList();
        }

        /// <summary>
        /// Scala alias for FirstOrDefault
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="self"></param>
        /// <returns></returns>
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

        public static TValue GetOrElse<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue elseValue)
        {
            if (hash.ContainsKey(key)) return hash[key];
            return elseValue;
        }

        public static T GetOrElse<T>(this T obj, T elseValue)
        {
            if (obj.Equals(default(T)))
                return elseValue;
            return obj;
        }

        public static IDictionary<TKey, TValue> AddAndReturn<TKey, TValue>(this IDictionary<TKey, TValue> hash, TKey key, TValue value)
        {
            hash.AddOrSet(key, value);
            return hash;
        }

        public static TimeSpan Max(this TimeSpan @this, TimeSpan other) 
        {
            return @this > other ? @this : other;
        }

        /// <summary>
        /// Grabs a subset of an IEnumerable based on a starting index and position
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="items">The array of items to slice</param>
        /// <param name="startIndex">The starting position to begin the slice</param>
        /// <param name="count">The number of items to take</param>
        /// <returns>A slice of size <see cref="count"/> beginning from position <see cref="startIndex"/> in <see cref="items"/>.</returns>
        public static IEnumerable<T> Slice<T>(this IEnumerable<T> items, int startIndex, int count)
        {
            var itemsAsList = items.ToList();
            if(startIndex < 0 || startIndex > itemsAsList.Count) throw new ArgumentOutOfRangeException("startIndex");
            if(startIndex + count > itemsAsList.Count) 
                throw new ArgumentOutOfRangeException("count", 
                    string.Format("startIndex + count has length {0} which exceeds maximum index of array ({1})", 
                    startIndex + count, itemsAsList.Count));

            var resultantList = new List<T>(count);
            for (var i = 0; i < count; i++)
            {
                resultantList.Add(itemsAsList[startIndex + i]);
            }
            return resultantList;
        }

        /// <summary>
        /// Select all the items in this array beginning with <see cref="startingItem"/> and up until the end of the array.
        /// 
        /// If <see cref="startingItem"/> is not found in the array, From will return an empty set.
        /// If <see cref="startingItem"/> is found at the end of the array, From will return the entire original array.
        /// </summary>
        public static IEnumerable<T> From<T>(this IEnumerable<T> items, T startingItem)
        {
            var itemsAsList = items.ToList();
            var indexOf = itemsAsList.IndexOf(startingItem);
            if (indexOf == -1) return new List<T>();
            if (indexOf == 0) return itemsAsList;
            var itemCount = (itemsAsList.Count - indexOf);
            return itemsAsList.Slice(indexOf, itemCount);
        }

        /// <summary>
        /// Select all the items in this array from the beginning until (but not including) <see cref="startingItem"/>
        /// 
        /// If <see cref="startingItem"/> is not found in the array, Until will select all items.
        /// If <see cref="startingItem"/> is the first item in the array, an empty array will be returned.
        /// </summary>
        public static IEnumerable<T> Until<T>(this IEnumerable<T> items, T startingItem)
        {
            var itemsAsList = items.ToList();
            var indexOf = itemsAsList.IndexOf(startingItem);
            if (indexOf == -1) return itemsAsList;
            if (indexOf == 0) return new List<T>();
            return itemsAsList.Slice(0, indexOf);
        }
    }
}