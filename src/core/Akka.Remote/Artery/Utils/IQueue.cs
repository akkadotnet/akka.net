using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Akka.Remote.Artery.Utils
{
    /// <summary>
    /// A wrapper/facade interface for C# Queue to emulate java.util.Queue
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IQueue<T> : ICollection<T>
    {
        /// <summary>
        /// Inserts the specified element into this queue if it is possible to do so
        /// immediately without violating capacity restrictions, returning
        /// {@code true} upon success and throwing an {@code IllegalStateException}
        /// if no space is currently available.
        /// </summary>
        /// <param name="e">the element to add</param>
        /// <returns>{@code true}</returns>
        new bool Add(T e);

        /// <summary>
        /// Inserts the specified element into this queue if it is possible to do
        /// so immediately without violating capacity restrictions.
        /// When using a capacity-restricted queue, this method is generally
        /// preferable to {@link #add}, which can fail to insert an element only
        /// by throwing an exception.
        /// </summary>
        /// <param name="e">the element to add</param>
        /// <returns>{@code true} if the element was added to this queue, else {@code false}</returns>
        bool Offer(T e);

        /// <summary>
        /// Retrieves and removes the head of this queue.  This method differs
        /// from {@link #poll() poll()} only in that it throws an exception if
        /// this queue is empty.
        /// </summary>
        /// <returns>the head of this queue</returns>
        T Remove();

        /// <summary>
        /// Retrieves and removes the head of this queue,
        /// or returns {@code null} if this queue is empty.
        /// </summary>
        /// <returns>the head of this queue, or {@code null} if this queue is empty</returns>
        T Poll();

        /// <summary>
        /// Retrieves, but does not remove, the head of this queue.  This method
        /// differs from {@link #peek peek} only in that it throws an exception
        /// if this queue is empty.
        /// </summary>
        /// <returns>the head of this queue</returns>
        T Element();

        /// <summary>
        /// Retrieves, but does not remove, the head of this queue,
        /// or returns {@code null} if this queue is empty.
        /// </summary>
        /// <returns>the head of this queue, or {@code null} if this queue is empty</returns>
        T Peek();

        // From here on out, declared function interface is based on java.util.Collection
        bool AddAll(ICollection<T> c);
        T[] ToArray();
        T[] ToArray(T[] a);
    }
}
