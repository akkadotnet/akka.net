//-----------------------------------------------------------------------
// <copyright file="Keep.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Convenience functions for often-encountered purposes like keeping only the
    /// left (first) or only the right (second) of two input values.
    /// </summary> 
    public static class Keep
    {
        public static TLeft Left<TLeft, TRight>(TLeft left, TRight right) => left;

        public static TRight Right<TLeft, TRight>(TLeft left, TRight right) => right;

        public static Tuple<TLeft, TRight> Both<TLeft, TRight>(TLeft left, TRight right) => Tuple.Create(left, right);

        public static NotUsed None<TLeft, TRight>(TLeft left, TRight right) => NotUsed.Instance;

        private static readonly RuntimeMethodHandle KeepRightMethodhandle = typeof(Keep).GetMethod(nameof(Right)).MethodHandle;

        public static bool IsRight<T1, T2, T3>(Func<T1, T2, T3> fn)
        {
            return fn.Method.IsGenericMethod && fn.Method.GetGenericMethodDefinition().MethodHandle.Value == KeepRightMethodhandle.Value;
        }

        private static readonly RuntimeMethodHandle KeepLeftMethodhandle = typeof(Keep).GetMethod(nameof(Left)).MethodHandle;

        public static bool IsLeft<T1, T2, T3>(Func<T1, T2, T3> fn)
        {
            return fn.Method.IsGenericMethod && fn.Method.GetGenericMethodDefinition().MethodHandle.Value == KeepLeftMethodhandle.Value;
        }
    }
}