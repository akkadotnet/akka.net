// //-----------------------------------------------------------------------
// // <copyright file="ITestQueue.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.TestKit.Internal
{
    public interface ITestQueue<T>
    {
        /// <summary>
        /// The number of items that are currently in the queue.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Adds the specified item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        void Enqueue(T item);

        /// <summary>
        /// Adds the specified item to the end of the queue.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        ValueTask EnqueueAsync(T item);

        /// <summary>
        /// Tries to add the specified item to the end of the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the add to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the add completed within the specified timeout; otherwise, <c>false</c>.</returns>
        bool TryEnqueue(T item, int millisecondsTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// Tries to add the specified item to the end of the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to add to the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the add to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the add completed within the specified timeout; otherwise, <c>false</c>.</returns>
        ValueTask<bool> TryEnqueueAsync(T item, int millisecondsTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// Tries to remove the specified item from the queue.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the item was removed; otherwise, <c>false</c>.</returns>
        bool TryTake(out T item, CancellationToken cancellationToken = default);

        /// <summary>
        /// Tries to remove the specified item from the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the remove to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the remove completed within the specified timeout; otherwise, <c>false</c>.</returns>
        bool TryTake(out T item, int millisecondsTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// Tries to remove the specified item from the queue.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns>a tuple of <c>bool</c> and <c>T</c>, <c>true</c> if the item was removed; otherwise, <c>false</c>.</returns>
        ValueTask<(bool success, T item)> TryTakeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Tries to remove the specified item from the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the remove to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns>a tuple of <c>bool</c> and <c>T</c>, <c>true</c> if the remove completed within the specified timeout; otherwise, <c>false</c>.</returns>
        ValueTask<(bool success, T item)> TryTakeAsync(int millisecondsTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// Removes an item from the collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">
        /// This exception is thrown when the operation is canceled.
        /// </exception>
        /// <returns>The item removed from the collection.</returns>
        T Take(CancellationToken cancellationToken);

        /// <summary>
        /// Removes an item from the collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">
        /// This exception is thrown when the operation is canceled.
        /// </exception>
        /// <returns>The item removed from the collection.</returns>
        ValueTask<T> TakeAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Tries to peek the specified item from the queue.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <returns><c>true</c> if the item was removed; otherwise, <c>false</c>.</returns>
        bool TryPeek(out T item);

        /// <summary>
        /// Tries to peek the specified item from the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="item">The item to remove from the queue.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the remove to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns><c>true</c> if the remove completed within the specified timeout; otherwise, <c>false</c>.</returns>
        bool TryPeek(out T item, int millisecondsTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// Tries to peek the specified item from the queue.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns>a tuple of <c>bool</c> and <c>T</c>, <c>true</c> if the item was removed; otherwise, <c>false</c>.</returns>
        ValueTask<(bool success, T item)> TryPeekAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Tries to peek the specified item from the queue within the specified time period.
        /// A token can be provided to cancel the operation if needed.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait for the remove to complete.</param>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <returns>a tuple of <c>bool</c> and <c>T</c>, <c>true</c> if the remove completed within the specified timeout; otherwise, <c>false</c>.</returns>
        ValueTask<(bool success, T item)> TryPeekAsync(int millisecondsTimeout, CancellationToken cancellationToken);

        /// <summary>
        /// Peek an item from the collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">
        /// This exception is thrown when the operation is canceled.
        /// </exception>
        /// <returns>The item removed from the collection.</returns>
        T Peek(CancellationToken cancellationToken);

        /// <summary>
        /// Peek an item from the collection.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token that can be used to cancel the operation.</param>
        /// <exception cref="OperationCanceledException">
        /// This exception is thrown when the operation is canceled.
        /// </exception>
        /// <returns>The item removed from the collection.</returns>
        ValueTask<T> PeekAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Copies the items from the <see cref="ITestQueue{T}"/> instance into a new <see cref="List{T}"/>.
        /// </summary>
        /// <returns>A <see cref="List{T}"/> containing copies of the elements of the collection</returns>
        [Obsolete("This method will be removed in the future")]
        List<T> ToList();
    }
}