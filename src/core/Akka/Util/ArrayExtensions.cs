using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// Provides extension utilities to arrays.
    /// </summary>
    public static class ArrayExtensions
    {
        /// <summary>
        /// Determines if an array is null or empty.
        /// </summary>
        /// <param name="obj">The array to check.</param>
        /// <returns>True if null or empty, false otherwise.</returns>
        public static bool IsNullOrEmpty(this Array obj)
        {
            return ((obj == null) || (obj.Length == 0));
        }

        /// <summary>
        /// Shuffles an array of objects.
        /// </summary>
        /// <typeparam name="T">The type of the array to sort.</typeparam>
        /// <param name="array">The array to sort.</param>
        public static T[] Shuffle<T>(this T[] array)
        {
            var length = array.Length;
            var random = new Random();

            while (length > 1)
            {
                int randomNumber = random.Next(length--);
                T obj = array[length];
                array[length] = array[randomNumber];
                array[randomNumber] = obj;
            }
            return array;
        }

        /// <summary>
        /// Implementation of Scala's ZipWithIndex method.
        /// 
        /// Folds a collection into a Dictionary where the original value (of type T) acts as the key
        /// and the index of the item in the array acts as the value.
        /// </summary>
        public static Dictionary<T, int> ZipWithIndex<T>(this IEnumerable<T> collection)
        {
            var i = 0;
            var dict = new Dictionary<T, int>();
            foreach (var item in collection)
            {
                dict.Add(item, i);
                i++;
            }
            return dict;
        }
    }
}
