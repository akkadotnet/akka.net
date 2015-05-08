using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    /// <summary>
    /// Utility methods for operating on arrays.
    /// </summary>
    public static class Vector
    {
        /// <summary>
        /// Returns a function that returns an array that contains the results of some element 
        /// computation a number of times.
        /// </summary>
        /// <typeparam name="T">The type of element</typeparam>
        /// <param name="number">the number of elements desired</param>
        /// <returns>A func that will take the element computation and return an Array of size n, 
        /// where each element contains the result of computing elem. </returns>
        public static Func<Func<T>, IList<T>> Fill<T>(int number)
        {
            return func => Enumerable
                .Range(1, number)
                .Select(_ => func())
                .ToList();
        } 
    }
}
