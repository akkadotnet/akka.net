//-----------------------------------------------------------------------
// <copyright file="Either.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util
{
    public abstract class Either<TA,TB>
    {
        protected Either(TA left, TB right)
        {
            Left = left;
            Right = right;
        }

        public abstract bool IsLeft { get; }
        public abstract bool IsRight { get; }

        protected TB Right { get; private set; }

        protected TA Left { get; private set; }

        public object Value
        {
            get
            {
                if (IsLeft) return Left;
                return Right;
            }
        }
        public Right<TA, TB> ToRight()
        {
            return new Right<TA, TB>(Right);
        }

        public Left<TA, TB> ToLeft()
        {
            return new Left<TA, TB>(Left);
        }

        public static implicit operator Either<TA, TB>(Left<TA> left)
        {
            return new Left<TA, TB>(left.Value);
        }

        public static implicit operator Either<TA, TB>(Right<TB> right)
        {
            return new Right<TA, TB>(right.Value);
        }

        public Either<TRes1, TRes2> Map<TRes1, TRes2>(Func<TA, TRes1> map1, Func<TB, TRes2> map2)
        {
            if (IsLeft)
                return new Left<TRes1, TRes2>(map1(ToLeft().Value));
            return new Right<TRes1, TRes2>(map2(ToRight().Value));
        }

        public Either<TRes, TB> MapLeft<TRes>(Func<TA, TRes> map)
        {
            return Map(map, x => x);
        }

        public Either<TA, TRes> MapRight<TRes>(Func<TB, TRes> map)
        {
            return Map(x => x, map);
        }

        public TRes Fold<TRes>(Func<TA, TRes> left, Func<TB, TRes> right)
        {
            return IsLeft ? left(ToLeft().Value) : right(ToRight().Value);
        }
    }

    public static class Either
    {
        public static Left<T> Left<T>(T value)
        {
            return new Left<T>(value);
        }

        public static Right<T> Right<T>(T value)
        {
            return new Right<T>(value);
        }
    }


    public class Right<TA, TB> : Either<TA, TB>
    {
        public Right(TB b) : base(default(TA), b)
        {
        }

        public override bool IsLeft
        {
            get { return false; }
        }

        public override bool IsRight
        {
            get { return true; }
        }

        public new TB Value
        {
            get { return Right; }
        }
    }

    public class Right<T>
    {
        public Right(T value)
        {
            Value = value;
        }

        public T Value { get; private set; }

        public bool IsLeft
        {
            get { return false; }
        }

        public bool IsRight
        {
            get { return true; }
        }
    }

    public class Left<TA, TB> : Either<TA, TB>
    {
        public Left(TA a) : base(a, default(TB))
        {
        }

        public override bool IsLeft
        {
            get { return true; }
        }

        public override bool IsRight
        {
            get { return false; }
        }

        public new TA Value
        {
            get { return Left; }
        }
    }

    public class Left<T>
    {
        public Left(T value)
        {
            Value = value;
        }

        public T Value { get; private set; }

        public bool IsLeft
        {
            get { return true; }
        }

        public bool IsRight
        {
            get { return false; }
        }
    }
}

