//-----------------------------------------------------------------------
// <copyright file="AtomicBoolean.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Akka.Util
{
    //A generic type can't have a explicit layout
    //[StructLayout(LayoutKind.Explicit)]
    public struct Result<T> : IEquatable<Result<T>>
    {
        //[FieldOffset(0)]
        public readonly bool IsSuccess;
        //[FieldOffset(1)]
        public readonly T Value;
        //[FieldOffset(1)]
        public readonly Exception Exception;

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

        public bool Equals(Result<T> other)
        {
            if (IsSuccess ^ other.IsSuccess) return false;
            return IsSuccess
                ? Equals(Value, other.Value)
                : Equals(Exception, other.Exception);
        }

        public override bool Equals(object obj)
        {
            if (obj is Result<T>) return Equals((Result<T>) obj);
            return false;
        }

        public override int GetHashCode()
        {
            return IsSuccess
                ? (Value == null ? 0 : Value.GetHashCode())
                : (Exception == null ? 0 : Exception.GetHashCode());
        }

        public static bool operator ==(Result<T> x, Result<T> y)
        {
            return x.Equals(y);
        }

        public static bool operator !=(Result<T> x, Result<T> y)
        {
            return !(x == y);
        }
    }

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
            return task.IsCanceled || task.IsFaulted ? new Result<T>(task.Exception) : new Result<T>(task.Result);
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