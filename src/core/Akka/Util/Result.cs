//-----------------------------------------------------------------------
// <copyright file="Result.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Threading.Tasks;

namespace Akka.Util
{
    //A generic type can't have a explicit layout
    //[StructLayout(LayoutKind.Explicit)]
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public struct Result<T> : IEquatable<Result<T>>
    {
        //[FieldOffset(0)]
        /// <summary>
        /// TBD
        /// </summary>
        public readonly bool IsSuccess;
        //[FieldOffset(1)]
        /// <summary>
        /// TBD
        /// </summary>
        public readonly T Value;
        //[FieldOffset(1)]
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Exception Exception;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        public Result(T value) : this()
        {
            IsSuccess = true;
            Value = value;
        }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="exception">TBD</param>
        public Result(Exception exception) : this()
        {
            IsSuccess = false;
            Exception = exception;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="other">TBD</param>
        /// <returns>TBD</returns>
        public bool Equals(Result<T> other)
        {
            if (IsSuccess ^ other.IsSuccess) return false;
            return IsSuccess
                ? Equals(Value, other.Value)
                : Equals(Exception, other.Exception);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="obj">TBD</param>
        /// <returns>TBD</returns>
        public override bool Equals(object obj)
        {
            if (obj is Result<T>) return Equals((Result<T>) obj);
            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override int GetHashCode()
        {
            return IsSuccess
                ? (Value == null ? 0 : Value.GetHashCode())
                : (Exception == null ? 0 : Exception.GetHashCode());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <param name="y">TBD</param>
        /// <returns>TBD</returns>
        public static bool operator ==(Result<T> x, Result<T> y)
        {
            return x.Equals(y);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="x">TBD</param>
        /// <param name="y">TBD</param>
        /// <returns>TBD</returns>
        public static bool operator !=(Result<T> x, Result<T> y)
        {
            return !(x == y);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Result
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> Success<T>(T value)
        {
            return new Result<T>(value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="exception">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> Failure<T>(Exception exception)
        {
            return new Result<T>(exception);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public static Result<T> FromTask<T>(Task<T> task)
        {
            return task.IsCanceled || task.IsFaulted ? new Result<T>(task.Exception) : new Result<T>(task.Result);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
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