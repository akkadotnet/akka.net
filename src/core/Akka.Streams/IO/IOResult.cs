//-----------------------------------------------------------------------
// <copyright file="IOResult.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Util;

namespace Akka.Streams.IO
{
    /// <summary>
    /// Holds a result of an IO operation.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public struct IOResult
    {
        private readonly Result<NotUsed> _status;

        /// <summary>
        /// Creates a new IOResult.
        /// </summary>
        /// <param name="count">Numeric value depending on context, for example IO operations performed or bytes processed.</param>
        /// <param name="status">Status of the result. Can be either <see cref="NotUsed"/> or an exception.</param>
        public IOResult(long count, Result<NotUsed> status)
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
        /// <exception cref="NotSupportedException">Is thrown if the property is accessed for a successful <see cref="IOResult"/></exception>
        public Exception Error
        {
            get
            {
                if (WasSuccessful)
                    throw new NotSupportedException("IO operation was successful.");

                return _status.Exception;
            }
        }

        /// <summary>
        /// Creates successful IOResult
        /// </summary>
        /// <param name="count">Numeric value depending on context, for example IO operations performed or bytes processed.</param>
        /// <returns>Successful IOResult</returns>
        public static IOResult Success(long count) => new IOResult(count, Result.Success(NotUsed.Instance));

        /// <summary>
        /// Creates failed IOResult, <paramref name="count"/> should be the number of bytes (or other unit, please document in your APIs) processed before failing
        /// </summary>
        /// <param name="count">Numeric value depending on context, for example IO operations performed or bytes processed.</param>
        /// <param name="reason">The corresponding <see cref="Exception"/></param>
        /// <returns>Failed IOResult</returns>
        public static IOResult Failed(long count, Exception reason)
            => new IOResult(count, Result.Failure<NotUsed>(reason));
    }
}
