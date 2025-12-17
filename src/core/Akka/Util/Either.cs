//-----------------------------------------------------------------------
// <copyright file="Either.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util
{
    /// <summary>
    /// Represents a value of one of two possible types (a disjoint union).
    /// An instance of Either is either a Left or a Right.
    /// </summary>
    /// <typeparam name="TA">The type of the value if this is a Left instance.</typeparam>
    /// <typeparam name="TB">The type of the value if this is a Right instance.</typeparam>
    public abstract class Either<TA,TB>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Either{TA, TB}"/> class.
        /// </summary>
        /// <param name="left">The left value.</param>
        /// <param name="right">The right value.</param>
        protected Either(TA left, TB right)
        {
            Left = left;
            Right = right;
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Left.
        /// </summary>
        public abstract bool IsLeft { get; }
        
        /// <summary>
        /// Gets a value indicating whether this instance represents a Right.
        /// </summary>
        public abstract bool IsRight { get; }

        /// <summary>
        /// Gets the right value.
        /// </summary>
        protected TB Right { get; private set; }

        /// <summary>
        /// Gets the left value.
        /// </summary>
        protected TA Left { get; private set; }

        /// <summary>
        /// Gets the contained value, either Left or Right.
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
        /// Converts this instance to a Right instance.
        /// </summary>
        /// <returns>A new Right instance containing the right value.</returns>
        public Right<TA, TB> ToRight()
        {
            return new Right<TA, TB>(Right);
        }

        /// <summary>
        /// Converts this instance to a Left instance.
        /// </summary>
        /// <returns>A new Left instance containing the left value.</returns>
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
        /// Maps the values contained in this Either instance using the provided functions.
        /// </summary>
        /// <typeparam name="TRes1">The type of the left result.</typeparam>
        /// <typeparam name="TRes2">The type of the right result.</typeparam>
        /// <param name="map1">The function to apply to the left value if this is a Left.</param>
        /// <param name="map2">The function to apply to the right value if this is a Right.</param>
        /// <returns>A new Either instance with transformed values.</returns>
        public Either<TRes1, TRes2> Map<TRes1, TRes2>(Func<TA, TRes1> map1, Func<TB, TRes2> map2)
        {
            if (IsLeft)
                return new Left<TRes1, TRes2>(map1(ToLeft().Value));
            return new Right<TRes1, TRes2>(map2(ToRight().Value));
        }

        /// <summary>
        /// Maps the left value contained in this Either instance.
        /// </summary>
        /// <typeparam name="TRes">The type of the transformed left value.</typeparam>
        /// <param name="map">The function to apply to the left value if this is a Left.</param>
        /// <returns>A new Either instance with the transformed left value.</returns>
        public Either<TRes, TB> MapLeft<TRes>(Func<TA, TRes> map)
        {
            return Map(map, x => x);
        }

        /// <summary>
        /// Maps the right value contained in this Either instance.
        /// </summary>
        /// <typeparam name="TRes">The type of the transformed right value.</typeparam>
        /// <param name="map">The function to apply to the right value if this is a Right.</param>
        /// <returns>A new Either instance with the transformed right value.</returns>
        public Either<TA, TRes> MapRight<TRes>(Func<TB, TRes> map)
        {
            return Map(x => x, map);
        }

        /// <summary>
        /// Applies one of the provided functions depending on whether this is a Left or Right.
        /// </summary>
        /// <typeparam name="TRes">The type of the result.</typeparam>
        /// <param name="left">The function to apply if this is a Left.</param>
        /// <param name="right">The function to apply if this is a Right.</param>
        /// <returns>The result of applying the corresponding function.</returns>
        public TRes Fold<TRes>(Func<TA, TRes> left, Func<TB, TRes> right)
        {
            return IsLeft ? left(ToLeft().Value) : right(ToRight().Value);
        }
    }

    /// <summary>
    /// Static factory methods for creating Either instances.
    /// </summary>
    public static class Either
    {
        /// <summary>
        /// Creates a new Left instance with the specified value.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value to contain.</param>
        /// <returns>A new Left instance containing the specified value.</returns>
        public static Left<T> Left<T>(T value)
        {
            return new Left<T>(value);
        }

        /// <summary>
        /// Creates a new Right instance with the specified value.
        /// </summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="value">The value to contain.</param>
        /// <returns>A new Right instance containing the specified value.</returns>
        public static Right<T> Right<T>(T value)
        {
            return new Right<T>(value);
        }
    }


    /// <summary>
    /// Represents the right side of an Either instance.
    /// </summary>
    /// <typeparam name="TA">The type of the left value (not used in this class).</typeparam>
    /// <typeparam name="TB">The type of the right value contained in this instance.</typeparam>
    public class Right<TA, TB> : Either<TA, TB>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Right{TA, TB}"/> class.
        /// </summary>
        /// <param name="b">The right value to contain.</param>
        public Right(TB b) : base(default(TA), b)
        {
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Left.
        /// Always returns false for Right instances.
        /// </summary>
        public override bool IsLeft
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Right.
        /// Always returns true for Right instances.
        /// </summary>
        public override bool IsRight
        {
            get { return true; }
        }

        /// <summary>
        /// Gets the right value contained in this instance.
        /// </summary>
        public new TB Value
        {
            get { return Right; }
        }
    }

    /// <summary>
    /// Represents a standalone right value that can be implicitly converted to an Either.
    /// </summary>
    /// <typeparam name="T">The type of the right value.</typeparam>
    public class Right<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Right{T}"/> class.
        /// </summary>
        /// <param name="value">The right value to contain.</param>
        public Right(T value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the right value contained in this instance.
        /// </summary>
        public T Value { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Left.
        /// Always returns false for Right instances.
        /// </summary>
        public bool IsLeft
        {
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Right.
        /// Always returns true for Right instances.
        /// </summary>
        public bool IsRight
        {
            get { return true; }
        }
    }

    /// <summary>
    /// Represents the left side of an Either instance.
    /// </summary>
    /// <typeparam name="TA">The type of the left value contained in this instance.</typeparam>
    /// <typeparam name="TB">The type of the right value (not used in this class).</typeparam>
    public class Left<TA, TB> : Either<TA, TB>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Left{TA, TB}"/> class.
        /// </summary>
        /// <param name="a">The left value to contain.</param>
        public Left(TA a) : base(a, default(TB))
        {
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Left.
        /// Always returns true for Left instances.
        /// </summary>
        public override bool IsLeft
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Right.
        /// Always returns false for Left instances.
        /// </summary>
        public override bool IsRight
        {
            get { return false; }
        }

        /// <summary>
        /// Gets the left value contained in this instance.
        /// </summary>
        public new TA Value
        {
            get { return Left; }
        }
    }

    /// <summary>
    /// Represents a standalone left value that can be implicitly converted to an Either.
    /// </summary>
    /// <typeparam name="T">The type of the left value.</typeparam>
    public class Left<T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Left{T}"/> class.
        /// </summary>
        /// <param name="value">The left value to contain.</param>
        public Left(T value)
        {
            Value = value;
        }

        /// <summary>
        /// Gets the left value contained in this instance.
        /// </summary>
        public T Value { get; private set; }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Left.
        /// Always returns true for Left instances.
        /// </summary>
        public bool IsLeft
        {
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance represents a Right.
        /// Always returns false for Left instances.
        /// </summary>
        public bool IsRight
        {
            get { return false; }
        }
    }
}

