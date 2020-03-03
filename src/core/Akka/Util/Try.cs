//-----------------------------------------------------------------------
// <copyright file="Try.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util
{
    /// <summary>
    /// Represents either success or failure of some operation
    /// </summary>
    /// <typeparam name="T">Success type</typeparam>
    public class Try<T>
    {
        /// <summary>
        /// Creates <see cref="Try{T}"/> with success result 
        /// </summary>
        public Try(T success)
        {
            Success = success;
        }

        /// <summary>
        /// Creates <see cref="Try{T}"/> with failure
        /// </summary>
        public Try(Exception failure)
        {
            Failure = failure;
        }

        public static implicit operator Try<T>(T value)
        {
            return new Try<T>(value);
        }

        /// <summary>
        /// Shows if this is Success
        /// </summary>
        public bool IsSuccess => Success.HasValue;
        
        /// <summary>
        /// If set, contains successfull execution result
        /// </summary>
        public Option<T> Success { get; } = Option<T>.None;
        /// <summary>
        /// If set, contains failure description
        /// </summary>
        public Option<Exception> Failure { get; } = Option<Exception>.None;

        /// <summary>
        /// Applies the given function f if this is a Failure, otherwise returns this if this is a Success.
        /// </summary>
        public Try<T> Recover(Action<Exception> failureHandler)
        {
            if (Failure.HasValue)
            {
                try
                {
                    failureHandler(Failure.Value);
                    return this;
                }
                catch (Exception ex)
                {
                    return new Try<T>(ex);
                }
            }
            else
            {
                return this;
            }
        }
        
        /// <summary>
        /// Gets <see cref="Try{T}"/> result from function execution
        /// </summary>
        public static Try<T> From(Func<T> func)
        {
            try
            {
                return new Try<T>(func());
            }
            catch (Exception ex)
            {
                return new Try<T>(ex);
            }
        }

        /// <summary>
        /// Returns this Try if it's a Success or the given default argument if this is a Failure.
        /// </summary>
        public Try<T> OrElse(Try<T> @default)
        {
            if (Success.HasValue)
                return this;
            else
                return @default;
        }
        
        /// <summary>
        /// Returns this Try if it's a Success or tries execute given fallback if this is a Failure.
        /// </summary>
        public Try<T> GetOrElse(Func<T> fallback)
        {
            if (Success.HasValue)
                return Success.Value;

            try
            {
                return fallback();
            }
            catch (Exception ex)
            {
                return new Try<T>(ex);
            }
        }

        /// <summary>
        /// Returns the value from this Success or throws the exception if this is a Failure.
        /// </summary>
        public T Get()
        {
            if (Failure.HasValue)
                throw Failure.Value;

            return Success.Value;
        }
    }
}
