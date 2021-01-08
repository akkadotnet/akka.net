//-----------------------------------------------------------------------
// <copyright file="ConcurrentSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class ConcurrentSet<T> : ICollection<T>, IEnumerable<T>, IEnumerable
    {
        private readonly ConcurrentDictionary<T, byte> _storage;

        /// <summary>
        /// TBD
        /// </summary>
        public ConcurrentSet()
        {
            _storage = new ConcurrentDictionary<T, byte>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="collection">TBD</param>
        public ConcurrentSet(IEnumerable<T> collection)
        {
            _storage = new ConcurrentDictionary<T, byte>(collection.Select(_ => new KeyValuePair<T, byte>(_, 0)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="collection">TBD</param>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(collection.Select(_ => new KeyValuePair<T, byte>(_, 0)),
                comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="concurrencyLevel">TBD</param>
        /// <param name="capacity">TBD</param>
        public ConcurrentSet(int concurrencyLevel, int capacity)
        {
            _storage = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="concurrencyLevel">TBD</param>
        /// <param name="collection">TBD</param>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(int concurrencyLevel, IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(concurrencyLevel,
                collection.Select(_ => new KeyValuePair<T, byte>(_, 0)), comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="concurrencyLevel">TBD</param>
        /// <param name="capacity">TBD</param>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(int concurrencyLevel, int capacity, IEqualityComparer<T> comparer)
        {
            _storage = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity, comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty
        {
            get { return _storage.IsEmpty; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Count
        {
            get { return _storage.Count; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Clear()
        {
            _storage.Clear();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
        public bool TryAdd(T item)
        {
            return _storage.TryAdd(item, 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
        public bool TryRemove(T item)
        {
            byte dontCare;
            return _storage.TryRemove(item, out dontCare);
        }
    }
}

