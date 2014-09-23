using System;
using System.Collections;
using System.Collections.Generic;

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public static class TypeListSorter
    {
        /// <summary>INTERNAL
        /// Sorts the sequence and returns a new <see cref="List{T}"/>.
        /// The list is ordered so the most specific type is before all its subtypes, i.e.
        /// subtypes are before their supertypes
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public static List<T> SortSoSubtypesPrecedeTheirSupertypes<T>(IEnumerable<T> sequence, Func<T,Type> typeSelector)
        {
            //Insertion sort. Insert subtypes before supertypes
            var collection = sequence as ICollection;
            var ordered = collection!=null ? new List<T>(collection.Count) : new List<T>();

            foreach(var item in sequence)
            {
                var typeToAdd = typeSelector(item);

                var indexOfFirstSupertype = ordered.FindIndex(tup =>
                {
                    var typeInList = typeSelector(tup);
                    var typeInListIsSupertypeOfTypeToAdd = typeInList.IsAssignableFrom(typeToAdd);
                    return typeInListIsSupertypeOfTypeToAdd;
                });
                if(indexOfFirstSupertype < 0)
                {
                    ordered.Add(item);
                }
                else
                {
                    //Type to add is a subtype to the type at indexToInsert, so
                    //we insert the item before that
                    ordered.Insert(indexOfFirstSupertype, item);
                }
            }
            return ordered;
        }
    }
}