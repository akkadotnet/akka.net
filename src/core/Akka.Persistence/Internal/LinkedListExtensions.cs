using System.Collections.Generic;

namespace Akka.Persistence
{
    /// <summary>
    /// TBD
    /// </summary>
    internal static class LinkedListExtensions
    {
        /// <summary>
        /// Removes first element from the list and returns it or returns default value if list was empty.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="self">TBD</param>
        /// <returns>TBD</returns>
        internal static T Pop<T>(this LinkedList<T> self)
        {
            if (self.First != null)
            {
                var first = self.First.Value;
                self.RemoveFirst();
                return first;
            }

            return default(T);
        }
    }
}