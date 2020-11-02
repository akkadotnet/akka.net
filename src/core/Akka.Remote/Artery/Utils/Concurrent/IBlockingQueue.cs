using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Remote.Artery.Utils.Concurrent
{
    public interface IBlockingQueue<T> : IQueue<T>
    {
        /// <summary>
        /// Inserts the specified element into this queue, waiting if necessary
        /// for space to become available.
        /// </summary>
        /// <param name="e"></param>
        void Put(T e);

        /// <summary>
        /// Inserts the specified element into this queue, waiting up to the
        /// specified wait time if necessary for space to become available.
        /// </summary>
        /// <param name="e"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        bool Offer(T e, TimeSpan timeout);

        /// <summary>
        /// Retrieves and removes the head of this queue, waiting if necessary
        /// until an element becomes available.
        /// </summary>
        /// <returns></returns>
        T Take();

        /// <summary>
        /// Retrieves and removes the head of this queue, waiting up to the
        /// specified wait time if necessary for an element to become available.
        /// </summary>
        /// <param name="timeout"></param>
        /// <returns></returns>
        T Poll(TimeSpan timeout);

        /// <summary>
        /// Returns the number of additional elements that this queue can ideally
        /// (in the absence of memory or resource constraints) accept without
        /// blocking, or {@code Integer.MAX_VALUE} if there is no intrinsic
        /// limit.
        ///
        /// <p>Note that you <em>cannot</em> always tell if an attempt to insert
        /// an element will succeed by inspecting {@code remainingCapacity}
        /// because it may be the case that another thread is about to
        /// insert or remove an element.</p>
        /// </summary>
        int RemainingCapacity { get; }

        /// <summary>
        /// Removes all available elements from this queue and adds them
        /// to the given collection.  This operation may be more
        /// efficient than repeatedly polling this queue.  A failure
        /// encountered while attempting to add elements to
        /// collection {@code c} may result in elements being in neither,
        /// either or both collections when the associated exception is
        /// thrown.  Attempts to drain a queue to itself result in
        /// {@code IllegalArgumentException}. Further, the behavior of
        /// this operation is undefined if the specified collection is
        /// modified while the operation is in progress.
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        int DrainTo(ICollection<T> c);

        /// <summary>
        /// Removes at most the given number of available elements from
        /// this queue and adds them to the given collection.  A failure
        /// encountered while attempting to add elements to
        /// collection {@code c} may result in elements being in neither,
        /// either or both collections when the associated exception is
        /// thrown.  Attempts to drain a queue to itself result in
        /// {@code IllegalArgumentException}. Further, the behavior of
        /// this operation is undefined if the specified collection is
        /// modified while the operation is in progress.
        /// </summary>
        /// <param name="c"></param>
        /// <param name="maxElements"></param>
        /// <returns></returns>
        int DrainTo(ICollection<T> c, int maxElements);
    }
}
