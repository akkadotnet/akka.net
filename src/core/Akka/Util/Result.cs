//-----------------------------------------------------------------------
// <copyright file="Result.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
#nullable enable

namespace Akka.Util
{
    /// <summary>
    /// A result type frequently used inside Akka.Streams and elsewhere.
    /// </summary>
    public readonly record struct Result<T>
    {
        /// <summary>
        /// <c>true</c> if the result is successful, <c>false</c> otherwise.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// <c>null</c> when <see cref="IsSuccess"/> is <c>false</c>.
        /// </summary>
        public readonly T? Value;

        /// <summary>
        /// <c>null</c> when <see cref="IsSuccess"/> is <c>true</c>.
        /// </summary>
        public readonly Exception? Exception;
        
        public Result(T value) : this()
        {
            IsSuccess = true;
            Value = value;
        }

        public Result(Exception exception) : this()
        {
            IsSuccess = false;
            Exception = exception;
        }
        
        public override string ToString() => IsSuccess ? $"Success ({Value})" : $"Failure ({Exception})";
    }

    /// <summary>
    /// Helper methods for creating <see cref="Result{T}"/> instances.
    /// </summary>
    public static class Result
    {
        public static Result<T> Success<T>(T value)
        {
            return new Result<T>(value);
        }
        
        public static Result<T> Failure<T>(Exception exception)
        {
            return new Result<T>(exception);
        }
        
        public static Result<T> FromTask<T>(Task<T> task)
        {
            if(!task.IsCompleted)
                throw new ArgumentException("Task is not completed. Result.FromTask only accepts completed tasks.", nameof(task));
            
            if(task.Exception is not null)
                return new Result<T>(task.Exception);
            
            if (task is { IsCanceled: true, Exception: null })
            {
                try
                {
                    _ = task.GetAwaiter().GetResult();
                }
                catch(Exception e)
                {
                    return new Result<T>(e);
                }

                throw new InvalidOperationException("Should never reach this line!");
            }
            
            if(task is { IsFaulted: true, Exception: null })
                throw new InvalidOperationException("Should never happen! something is wrong with .NET Task code!");
            
            return new Result<T>(task.Result);
        }
        
        public static Result<T> From<T>(Func<T> func)
        {
            try
            {
                var value = func();
                return new Result<T>(value);
            }
            catch (Exception e)
            {
                return new Result<T>(e);
            }
        }
    }
}
