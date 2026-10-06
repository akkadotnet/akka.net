//-----------------------------------------------------------------------
// <copyright file="OptionExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util.Extensions
{
    public static class OptionExtensions
    {
        /// <summary>
        /// Applies <paramref name="some"/> to the value of <paramref name="option"/> if it has one,
        /// otherwise invokes <paramref name="none"/>, and returns the result.
        /// </summary>
        public static TOut Match<TIn, TOut>(this Option<TIn> option, Func<TIn, TOut> some, Func<TOut> none)
        {
            return option.HasValue ? some(option.Value) : none();
        }

        /// <summary>
        /// Invokes <paramref name="some"/> with the value of <paramref name="option"/> if it has one,
        /// otherwise invokes <paramref name="none"/>.
        /// </summary>
        public static void Match<T>(this Option<T> option, Action<T> some, Action none)
        {
            if (option.HasValue)
                some(option.Value);
            else
                none();
        }
    }
}
