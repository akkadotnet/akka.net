using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Util
{
    internal sealed class WildcardIndex<T> : IEquatable<WildcardIndex<T>> where T : class
    {
        private readonly WildcardTree<T> _wildcardTree;
        private readonly WildcardTree<T> _doubleWildcardTree;

        public bool IsEmpty => _wildcardTree.IsEmpty && _doubleWildcardTree.IsEmpty;

        public WildcardIndex()
        {
            _wildcardTree = new WildcardTree<T>();
            _doubleWildcardTree = new WildcardTree<T>();
        }

        private WildcardIndex(WildcardTree<T> singleWildcard, WildcardTree<T> doubleWildcard)
        {
            _wildcardTree = singleWildcard;
            _doubleWildcardTree = doubleWildcard;
        }

        public WildcardIndex<T> Insert(IEnumerable<string> elems, T data)
        {
            if (elems == null) return this;
            
            switch(elems.Last())
            {
                case "**":
                    return new WildcardIndex<T>(_wildcardTree, _doubleWildcardTree.Insert(elems.GetEnumerator(), data));
                default:
                    return new WildcardIndex<T>(_wildcardTree.Insert(elems.GetEnumerator(), data), _doubleWildcardTree);
            }
        }

        public T Find(IEnumerable<string> elems)
        {
            if(_wildcardTree.IsEmpty)
            {
                if (_doubleWildcardTree.IsEmpty)
                    return default;
                return _doubleWildcardTree.FindWithTerminalDoubleWildcard(elems.GetEnumerator(), null).Data;
            }

            var withSingleWildcard = _wildcardTree.FindWithSingleWildcard(elems.GetEnumerator());
            if (!withSingleWildcard.IsEmpty)
                return withSingleWildcard.Data;
            return _doubleWildcardTree.FindWithTerminalDoubleWildcard(elems.GetEnumerator(), null).Data;
        }

        public bool Equals(WildcardIndex<T> other)
        {
            return _wildcardTree.Equals(other._wildcardTree) 
                && _doubleWildcardTree.Equals(other._doubleWildcardTree);
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
                hashCode = hashCode * -1521134295 + _wildcardTree.GetHashCode();
                hashCode = hashCode * -1521134295 + _doubleWildcardTree.GetHashCode();
                return hashCode;
            }
        }
    }
}
