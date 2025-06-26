//-----------------------------------------------------------------------
// <copyright file="WildcardTree.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <typeparam name="T">The type of data stored in the tree nodes.</typeparam>
    internal sealed class WildcardTree<T> where T:class
    {
        public bool IsEmpty => Data == null && Children.Count == 0;

        /// <summary>
        /// Initializes a new empty instance of the <see cref="WildcardTree{T}"/> class.
        /// </summary>
        public WildcardTree() : this(null, new Dictionary<string, WildcardTree<T>>()) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="WildcardTree{T}"/> class with the specified data and children.
        /// </summary>
        /// <param name="data">The data to store in this tree node.</param>
        /// <param name="children">The child nodes of this tree node, keyed by name.</param>
        /// <returns>A new instance of WildcardTree.</returns>
        public WildcardTree(T data, IDictionary<string, WildcardTree<T>> children)
        {
            Children = children;
            Data = data;
        }

        /// <summary>
        /// Gets the data stored in this tree node.
        /// </summary>
        public T Data { get; private set; }

        /// <summary>
        /// Gets the child nodes of this tree node, keyed by name.
        /// </summary>
        public IDictionary<string, WildcardTree<T>> Children { get; private set; }

        /// <summary>
        /// Inserts data at the path represented by the elements enumerator.
        /// </summary>
        /// <param name="elements">The path elements to traverse when inserting the data.</param>
        /// <param name="data">The data to insert at the specified path.</param>
        /// <returns>The updated tree after insertion.</returns>
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

        public WildcardTree<T> FindWithSingleWildcard(IEnumerator<string> elements)
        {
            if (!elements.MoveNext()) return this;

            if(Children.TryGetValue(elements.Current, out var next))
                return next.FindWithSingleWildcard(elements);
            else
                if (Children.TryGetValue("*", out next))
                    return next.FindWithSingleWildcard(elements);
                else
                    return Empty;
        }

        public WildcardTree<T> FindWithTerminalDoubleWildcard(IEnumerator<string> elements, WildcardTree<T> alt)
        {
            if (!elements.MoveNext()) return this;
            if (alt == null) alt = Empty;

            var newAlt = Children.GetOrElse("**", alt);

            if (Children.TryGetValue(elements.Current, out var next))
                return next.FindWithTerminalDoubleWildcard(elements, newAlt);
            else
                if (Children.TryGetValue("*", out next))
                    return next.FindWithTerminalDoubleWildcard(elements, newAlt);
                else
                    return newAlt;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (obj == null) return false;
            return GetHashCode() == obj.GetHashCode();
        }

        /// <inheritdoc/>
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

        /// <summary>
        /// An empty WildcardTree instance.
        /// </summary>
        public static readonly WildcardTree<T> Empty = new();

        #endregion
    }
}

