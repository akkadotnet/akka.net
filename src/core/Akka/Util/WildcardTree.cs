//-----------------------------------------------------------------------
// <copyright file="WildcardTree.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Akka.Util.Internal;

namespace Akka.Util
{
    /// <summary>
    /// A searchable nested dictionary, represents a searchable tree structure underneath
    /// </summary>
    public sealed class WildcardTree<T> where T:class
    {
        public WildcardTree() : this(null, new Dictionary<string, WildcardTree<T>>()) { }

        public WildcardTree(T data, IDictionary<string, WildcardTree<T>> children)
        {
            Children = children;
            Data = data;
        }

        public T Data { get; private set; }

        public IDictionary<string, WildcardTree<T>> Children { get; private set; }

        public WildcardTree<T> Insert(IEnumerator<string> elements, T data)
        {
            if (!elements.MoveNext())
            {
                Data = data;
                return this;
            }
            else
            {
                var e = elements.Current;
                Children = Children.AddAndReturn(e, Children.GetOrElse(e, new WildcardTree<T>()).Insert(elements, data));
                return this;
            }
        }

        public WildcardTree<T> Find(IEnumerator<string> elements)
        {
            if (!elements.MoveNext()) return this;
            else
            {
                var next = Children.GetOrElse(elements.Current, Children.GetOrElse("*", null));
                return next == null ? Empty : next.Find(elements);
            }
        }

        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            return GetHashCode() == obj.GetHashCode();
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (Data == null ? 0 : Data.GetHashCode());
                return Children.Aggregate(hash, (current, child) => current*23 + child.GetHashCode());
            }
        }

        #region Static methods

        public static readonly WildcardTree<T> Empty = new WildcardTree<T>();

        #endregion
    }
}
