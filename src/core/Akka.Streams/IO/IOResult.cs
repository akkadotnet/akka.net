using System;
using System.Reactive.Streams;
using Akka.Util;

namespace Akka.Streams.IO
{
    /// <summary>
    /// Holds a result of an IO operation.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public struct IOResult
    {
        private readonly Result<Unit> _status;

        /// <summary>
        /// Creates a new IOResult.
        /// </summary>
        /// <param name="count">Numeric value depending on context, for example IO operations performed or bytes processed.</param>
        /// <param name="status">Status of the result. Can be either <see cref="Unit"/> or an exception.</param>
        public IOResult(long count, Result<Unit> status)
        {
            Count = count;
            _status = status;
        }

        /// <summary>
        /// Numeric value depending on context, for example IO operations performed or bytes processed.
        /// </summary>
        public readonly long Count;

        /// <summary>
        /// Indicates whether IO operation completed successfully or not.
        /// </summary>
        public bool WasSuccessful => _status.IsSuccess;

        /// <summary>
        /// If the IO operation resulted in an error, returns the corresponding <see cref="Exception"/>
        /// or throws <see cref="NotSupportedException"/> otherwise.
        /// </summary>
        public Exception Error
        {
            get
            {
                if (WasSuccessful)
                    throw new NotSupportedException("IO operation was successfull.");

                return _status.Exception;
            }
        }

    }
}