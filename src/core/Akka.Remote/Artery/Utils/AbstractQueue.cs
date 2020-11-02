using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Akka.Pattern;
using Akka.Remote.Artery.Internal;
using Akka.Streams;

namespace Akka.Remote.Artery.Utils
{
    public abstract class AbstractQueue<T> : IQueue<T>
    {
        public virtual bool Add(T e)
        {
            if (Offer(e)) return true;
            throw new IllegalStateException("Queue full");
        }

        public virtual T Remove()
        {
            var x = Poll();
            if (x != null) return x;
            throw new NoSuchElementException("Queue is empty");
        }

        public virtual T Element()
        {
            var x = Peek();
            if (x != null) return x;
            throw new NoSuchElementException("Queue is empty");
        }

        public abstract bool Offer(T e);

        public abstract T Poll();

        public abstract T Peek();

        /// <summary>
        /// Removes all of the elements from this queue.
        /// The queue will be empty after this call returns.
        ///
        /// <p>This implementation repeatedly invokes {@link #poll poll} until it
        /// returns {@code null}.</p>
        /// </summary>
        public virtual void Clear()
        {
            while(Poll() != null) { }
        }

        /// <summary>
        /// Adds all of the elements in the specified collection to this
        /// queue.  Attempts to addAll of a queue to itself result in
        /// {@code IllegalArgumentException}. Further, the behavior of
        /// this operation is undefined if the specified collection is
        /// modified while the operation is in progress.
        ///
        /// <p>This implementation iterates over the specified collection,
        /// and adds each element returned by the iterator to this
        /// queue, in turn.  A runtime exception encountered while
        /// trying to add an element (including, in particular, a
        /// {@code null} element) may result in only some of the elements
        /// having been successfully added when the associated exception is
        /// thrown.</p>
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public virtual bool AddAll(ICollection<T> c)
        {
            if(c is null)
                throw new ArgumentNullException(nameof(c));
            if(ReferenceEquals(this, c))
                throw new IllegalArgumentException();

            var modified = false;
            foreach (var e in c)
            {
                if (Add(e))
                    modified = true;
            }

            return modified;
        }

        public abstract T[] ToArray();
        public abstract T[] ToArray(T[] a);

        #region ICollection

        void ICollection<T>.Add(T item)
            => Add(item);

        public abstract bool Contains(T item);

        public abstract void CopyTo(T[] array, int arrayIndex);

        public abstract bool Remove(T item);

        public abstract int Count { get; }
        public abstract bool IsReadOnly { get; }

        public abstract IEnumerator<T> GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        #endregion

    }
}
