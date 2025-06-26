//-----------------------------------------------------------------------
// <copyright file="ConcurrentSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    /// <summary>
    /// A thread-safe set implementation using a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the set.</typeparam>
    public class ConcurrentSet<T> : ICollection<T>, IEnumerable<T>, IEnumerable
    {
        private readonly ConcurrentDictionary<T, byte> _storage;

        /// <summary>
        /// Initializes a new, empty instance of the <see cref="ConcurrentSet{T}"/> class.
        /// </summary>
        public ConcurrentSet()
        {
            _storage = new ConcurrentDictionary<T, byte>();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentSet{T}"/> class that contains elements copied from the specified collection.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new set.</param>
        public ConcurrentSet(IEnumerable<T> collection)
        {
            _storage = new ConcurrentDictionary<T, byte>(collection.Select(_ => new KeyValuePair<T, byte>(_, 0)));
        }

        /// <summary>
        /// Initializes a new, empty instance of the <see cref="ConcurrentSet{T}"/> class that uses the specified equality comparer.
        /// </summary>
        /// <param name="comparer">The equality comparer to use for the set.</param>
        public ConcurrentSet(IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(comparer);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentSet{T}"/> class that contains elements copied from the specified collection and uses the specified equality comparer.
        /// </summary>
        /// <param name="collection">The collection whose elements are copied to the new set.</param>
        /// <param name="comparer">The equality comparer to use for the set.</param>
        public ConcurrentSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(collection.Select(_ => new KeyValuePair<T, byte>(_, 0)),
                comparer);
        }

        /// <summary>
        /// Initializes a new, empty instance of the <see cref="ConcurrentSet{T}"/> class with the specified concurrency level and capacity.
        /// </summary>
        /// <param name="concurrencyLevel">The estimated number of threads that will update the set concurrently.</param>
        /// <param name="capacity">The initial number of elements that the set can contain.</param>
        public ConcurrentSet(int concurrencyLevel, int capacity)
        {
            _storage = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentSet{T}"/> class that contains elements copied from the specified collection, has the specified concurrency level, and uses the specified equality comparer.
        /// </summary>
        /// <param name="concurrencyLevel">The estimated number of threads that will update the set concurrently.</param>
        /// <param name="collection">The collection whose elements are copied to the new set.</param>
        /// <param name="comparer">The equality comparer to use for the set.</param>
        public ConcurrentSet(int concurrencyLevel, IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(concurrencyLevel,
                collection.Select(_ => new KeyValuePair<T, byte>(_, 0)), comparer);
        }

        /// <summary>
        /// Initializes a new, empty instance of the <see cref="ConcurrentSet{T}"/> class with the specified concurrency level, capacity, and equality comparer.
        /// </summary>
        /// <param name="concurrencyLevel">The estimated number of threads that will update the set concurrently.</param>
        /// <param name="capacity">The initial number of elements that the set can contain.</param>
        /// <param name="comparer">The equality comparer to use for the set.</param>
        public ConcurrentSet(int concurrencyLevel, int capacity, IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity, comparer);
        }

        /// <summary>
        /// Gets a value indicating whether the set is empty.
        /// </summary>
        public bool IsEmpty
        {
            get { return _storage.IsEmpty; }
        }

        /// <summary>
        /// Gets the number of elements contained in the set.
        /// </summary>
        public int Count
        {
            get { return _storage.Count; }
        }

        /// <summary>
        /// Removes all elements from the set.
        /// </summary>
        public void Clear()
        {
            _storage.Clear();
        }

        /// <summary>
        /// Determines whether the set contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the set.</param>
        /// <returns>true if the set contains the specified value; otherwise, false.</returns>
        public bool Contains(T item)
        {
            return _storage.ContainsKey(item);
        }

        void ICollection<T>.Add(T item)
        {
            ((ICollection<KeyValuePair<T, byte>>) _storage).Add(new KeyValuePair<T, byte>(item, 0));
        }

        void ICollection<T>.CopyTo(T[] array, int arrayIndex)
        {
            foreach (var pair in _storage)
                array[arrayIndex++] = pair.Key;
        }

        bool ICollection<T>.IsReadOnly
        {
            get { return false; }
        }

        bool ICollection<T>.Remove(T item)
        {
            return TryRemove(item);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return _storage.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _storage.Keys.GetEnumerator();
        }

        /// <summary>
        /// Attempts to add the specified element to the set.
        /// </summary>
        /// <param name="item">The element to add to the set.</param>
        /// <returns>true if the element is added to the set; false if the element is already present.</returns>
        public bool TryAdd(T item)
        {
            return _storage.TryAdd(item, 0);
        }

        /// <summary>
        /// Attempts to remove and return the specified element from the set.
        /// </summary>
        /// <param name="item">The element to remove.</param>
        /// <returns>true if the element is successfully removed; otherwise, false.</returns>
        public bool TryRemove(T item)
        {
            byte dontCare;
            return _storage.TryRemove(item, out dontCare);
        }
    }
}

