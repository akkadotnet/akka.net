using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Util
{
    /// <summary>
    /// An equivalent of a <see cref="IEnumerator{T}"/> that is able to enumerate
    /// over a sequence of incoming data in asynchronous fashion.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IAsyncEnumerator<out T> : IDisposable
    {
        /// <summary>
        /// Gets the current element in the enumerator.
        /// </summary>
        T Current { get; }

        /// <summary>
        /// Advances the enumerator to the next element of the enumerator.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task MoveNextAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        void Reset();
    }
}