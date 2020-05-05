//-----------------------------------------------------------------------
// <copyright file="Either.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TA">TBD</typeparam>
    /// <typeparam name="TB">TBD</typeparam>
    public abstract class Either<TA,TB>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="left">TBD</param>
        /// <param name="right">TBD</param>
        protected Either(TA left, TB right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsLeft { get; }
        /// <summary>
        /// TBD
        /// </summary>
        public abstract bool IsRight { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected TB Right { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        protected TA Left { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public object Value
        {
            get
            {
                if (IsLeft) return Left;
                return Right;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public Right<TA, TB> ToRight()
        {
            return new Right<TA, TB>(Right);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public Left<TA, TB> ToLeft()
        {
            return new Left<TA, TB>(Left);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="Left{TA}"/> to <see cref="Either{TA, TB}"/>.
        /// </summary>
        /// <param name="left">The object to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator Either<TA, TB>(Left<TA> left)
        {
            return new Left<TA, TB>(left.Value);
        }

        /// <summary>
        /// Performs an implicit conversion from <see cref="Right{TB}"/> to <see cref="Either{TA, TB}"/>.
        /// </summary>
        /// <param name="right">The object to convert</param>
        /// <returns>The result of the conversion.</returns>
        public static implicit operator Either<TA, TB>(Right<TB> right)
        {
            return new Right<TA, TB>(right.Value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TRes1">TBD</typeparam>
        /// <typeparam name="TRes2">TBD</typeparam>
        /// <param name="map1">TBD</param>
        /// <param name="map2">TBD</param>
        /// <returns>TBD</returns>
        public Either<TRes1, TRes2> Map<TRes1, TRes2>(Func<TA, TRes1> map1, Func<TB, TRes2> map2)
        {
            if (IsLeft)
                return new Left<TRes1, TRes2>(map1(ToLeft().Value));
            return new Right<TRes1, TRes2>(map2(ToRight().Value));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TRes">TBD</typeparam>
        /// <param name="map">TBD</param>
        /// <returns>TBD</returns>
        public Either<TRes, TB> MapLeft<TRes>(Func<TA, TRes> map)
        {
            return Map(map, x => x);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TRes">TBD</typeparam>
        /// <param name="map">TBD</param>
        /// <returns>TBD</returns>
        public Either<TA, TRes> MapRight<TRes>(Func<TB, TRes> map)
        {
            return Map(x => x, map);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TRes">TBD</typeparam>
        /// <param name="left">TBD</param>
        /// <param name="right">TBD</param>
        /// <returns>TBD</returns>
        public TRes Fold<TRes>(Func<TA, TRes> left, Func<TB, TRes> right)
        {
            return IsLeft ? left(ToLeft().Value) : right(ToRight().Value);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Either
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static Left<T> Left<T>(T value)
        {
            return new Left<T>(value);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        public static Right<T> Right<T>(T value)
        {
            return new Right<T>(value);
        }
    }


    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TA">TBD</typeparam>
    /// <typeparam name="TB">TBD</typeparam>
    public class Right<TA, TB> : Either<TA, TB>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="b">TBD</param>
        public Right(TB b) : base(default(TA), b)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsLeft
        {
            get { return false; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsRight
        {
            get { return true; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public new TB Value
        {
            get { return Right; }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class Right<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        public Right(T value)
        {
            Value = value;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public T Value { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsLeft
        {
            get { return false; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsRight
        {
            get { return true; }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TA">TBD</typeparam>
    /// <typeparam name="TB">TBD</typeparam>
    public class Left<TA, TB> : Either<TA, TB>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="a">TBD</param>
        public Left(TA a) : base(a, default(TB))
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsLeft
        {
            get { return true; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool IsRight
        {
            get { return false; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public new TA Value
        {
            get { return Left; }
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public class Left<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        public Left(T value)
        {
            Value = value;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public T Value { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsLeft
        {
            get { return true; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsRight
        {
            get { return false; }
        }
    }
}

