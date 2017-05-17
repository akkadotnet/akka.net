//-----------------------------------------------------------------------
// <copyright file="ConcurrentSet.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        private readonly ConcurrentDictionary<T, byte> storage;

        /// <summary>
        /// TBD
        /// </summary>
        public ConcurrentSet()
        {
            storage = new ConcurrentDictionary<T, byte>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="collection">TBD</param>
        public ConcurrentSet(IEnumerable<T> collection)
        {
            storage = new ConcurrentDictionary<T, byte>(collection.Select(_ => new KeyValuePair<T, byte>(_, 0)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(IEqualityComparer<T> comparer)
        {
            storage = new ConcurrentDictionary<T, byte>(comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="collection">TBD</param>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            storage = new ConcurrentDictionary<T, byte>(collection.Select(_ => new KeyValuePair<T, byte>(_, 0)),
                comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="concurrencyLevel">TBD</param>
        /// <param name="capacity">TBD</param>
        public ConcurrentSet(int concurrencyLevel, int capacity)
        {
            storage = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="concurrencyLevel">TBD</param>
        /// <param name="collection">TBD</param>
        /// <param name="comparer">TBD</param>
        public ConcurrentSet(int concurrencyLevel, IEnumerable<T> collection, IEqualityComparer<T> comparer)
        {
            storage = new ConcurrentDictionary<T, byte>(concurrencyLevel,
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
            storage = new ConcurrentDictionary<T, byte>(concurrencyLevel, capacity, comparer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsEmpty
        {
            get { return storage.IsEmpty; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public int Count
        {
            get { return storage.Count; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void Clear()
        {
            storage.Clear();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
        public bool Contains(T item)
        {
            return storage.ContainsKey(item);
        }

        void ICollection<T>.Add(T item)
        {
            ((ICollection<KeyValuePair<T, byte>>) storage).Add(new KeyValuePair<T, byte>(item, 0));
        }

        void ICollection<T>.CopyTo(T[] array, int arrayIndex)
        {
            foreach (var pair in storage)
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
            return storage.Keys.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return storage.Keys.GetEnumerator();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
        public bool TryAdd(T item)
        {
            return storage.TryAdd(item, 0);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="item">TBD</param>
        /// <returns>TBD</returns>
        public bool TryRemove(T item)
        {
            byte dontCare;
            return storage.TryRemove(item, out dontCare);
        }
    }
}

