//-----------------------------------------------------------------------
// <copyright file="AsyncContext.TaskQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nito.AsyncEx
{
    public sealed partial class AsyncContext
    {
        /// <summary>
        /// A blocking queue.
        /// </summary>
        private sealed class TaskQueue : IDisposable
        {
            /// <summary>
            /// The underlying blocking collection.
            /// </summary>
            private readonly BlockingCollection<Tuple<Task, bool>> _queue;

            /// <summary>
            /// Initializes a new instance of the <see cref="TaskQueue"/> class.
            /// </summary>
            public TaskQueue()
            {
                _queue = new BlockingCollection<Tuple<Task, bool>>();
            }

            /// <summary>
            /// Gets a blocking enumerable that removes items from the queue. This enumerable only completes after <see cref="CompleteAdding"/> has been called.
            /// </summary>
            /// <returns>A blocking enumerable that removes items from the queue.</returns>
            public IEnumerable<Tuple<Task, bool>> GetConsumingEnumerable()
            {
                return _queue.GetConsumingEnumerable();
            }

            /// <summary>
            /// Generates an enumerable of <see cref="T:System.Threading.Tasks.Task"/> instances currently queued to the scheduler waiting to be executed.
            /// </summary>
            /// <returns>An enumerable that allows traversal of tasks currently queued to this scheduler.</returns>
            [System.Diagnostics.DebuggerNonUserCode]
            internal IEnumerable<Task> GetScheduledTasks()
            {
                foreach (var item in _queue)
                    yield return item.Item1;
            }

            /// <summary>
            /// Attempts to add the item to the queue. If the queue has been marked as complete for adding, this method returns <c>false</c>.
            /// </summary>
            /// <param name="item">The item to enqueue.</param>
            /// <param name="propagateExceptions">A value indicating whether exceptions on this task should be propagated out of the main loop.</param>
            public bool TryAdd(Task item, bool propagateExceptions)
            {
                try
                {
                    return _queue.TryAdd(Tuple.Create(item, propagateExceptions));
                }
                catch (InvalidOperationException)
                {
                    // vexing exception
                    return false;
                }
            }

            /// <summary>
            /// Marks the queue as complete for adding, allowing the enumerator returned from <see cref="GetConsumingEnumerable"/> to eventually complete. This method may be called several times.
            /// </summary>
            public void CompleteAdding()
            {
                _queue.CompleteAdding();
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            public void Dispose()
            {
                _queue.Dispose();
            }
        }
    }
}
