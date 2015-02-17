using System.Collections.Generic;
using System.Linq;

namespace Akka.Util.Internal
{
    /// <summary>
    /// Utility class for adding some basic immutable behaviors
    /// to specific types of collections without having to reference
    /// the entire BCL.Immutability NuGet package.
    /// 
    /// INTERNAL API
    /// </summary>
    internal static class ImmutabilityUtils
    {
        public static HashSet<T> CopyAndAdd<T>(this HashSet<T> set, T item)
        {
            Guard.Assert(set != null, "set cannot be null");
            // ReSharper disable once PossibleNullReferenceException
            var copy = new T[set.Count + 1];
            set.CopyTo(copy);
            copy[set.Count] = item;
            return new HashSet<T>(copy);
        }

        public static HashSet<T> CopyAndRemove<T>(this HashSet<T> set, T item)
        {
            Guard.Assert(set != null, "set cannot be null");
            // ReSharper disable once PossibleNullReferenceException
            var copy = new T[set.Count];
            set.CopyTo(copy);
            var copyList = copy.ToList();
            copyList.Remove(item);
            return new HashSet<T>(copyList);
        }
    }
}