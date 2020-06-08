using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Util
{
    internal sealed class WildcardIndex<T> : IEquatable<WildcardIndex<T>> where T : class
    {
        public WildcardTree<T> WildcardTree { get; }
        public WildcardTree<T> DoubleWildcardTree { get; }

        public bool IsEmpty => WildcardTree.IsEmpty && DoubleWildcardTree.IsEmpty;

        public WildcardIndex()
        {
            WildcardTree = new WildcardTree<T>();
            DoubleWildcardTree = new WildcardTree<T>();
        }

        private WildcardIndex(WildcardTree<T> singleWildcard, WildcardTree<T> doubleWildcard)
        {
            WildcardTree = singleWildcard;
            DoubleWildcardTree = doubleWildcard;
        }

        public WildcardIndex<T> Insert(IEnumerable<string> elems, T data)
        {
            if (elems == null) return this;
            
            switch(elems.Last())
            {
                case "**":
                    return new WildcardIndex<T>(WildcardTree, DoubleWildcardTree.Insert(elems.GetEnumerator(), data));
                default:
                    return new WildcardIndex<T>(WildcardTree.Insert(elems.GetEnumerator(), data), DoubleWildcardTree);
            }
        }

        public T Find(IEnumerable<string> elems)
        {
            if(WildcardTree.IsEmpty)
            {
                if (DoubleWildcardTree.IsEmpty)
                    return default;
                return DoubleWildcardTree.FindWithTerminalDoubleWildcard(elems.GetEnumerator(), null).Data;
            }

            var withSingleWildcard = WildcardTree.FindWithSingleWildcard(elems.GetEnumerator());
            if (!withSingleWildcard.IsEmpty)
                return withSingleWildcard.Data;
            return DoubleWildcardTree.FindWithTerminalDoubleWildcard(elems.GetEnumerator(), null).Data;
        }

        public bool Equals(WildcardIndex<T> other)
        {
            return WildcardTree.Equals(other.WildcardTree) 
                && DoubleWildcardTree.Equals(other.DoubleWildcardTree);
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(obj, this)) return true;
            if (!(obj is WildcardIndex<T> other)) return false;
            return Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = -872755323;
                hashCode = hashCode * -1521134295 + WildcardTree.GetHashCode();
                hashCode = hashCode * -1521134295 + DoubleWildcardTree.GetHashCode();
                return hashCode;
            }
        }
    }
}
