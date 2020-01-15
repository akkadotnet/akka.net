//-----------------------------------------------------------------------
// <copyright file="FSharpDelegateHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if FSCHECK
using System;
using Microsoft.FSharp.Core;

namespace Akka.Tests.Shared.Internals.Helpers
{
    /// <summary>
    /// Maps F# methods to C# delegates
    /// </summary>
    public static class FsharpDelegateHelper
    {
        public static FSharpFunc<T2, TResult> Create<T2, TResult>(Func<T2, TResult> func)
        {
            Converter<T2, TResult> conv = input => func(input);
            return FSharpFunc<T2, TResult>.FromConverter(conv);
        }

        public static FSharpFunc<T1, FSharpFunc<T2, TResult>> Create<T1, T2, TResult>(Func<T1, T2, TResult> func)
        {
            Converter<T1, FSharpFunc<T2, TResult>> conv =
                value1 => { return Create<T2, TResult>(value2 => func(value1, value2)); };
            return FSharpFunc<T1, FSharpFunc<T2, TResult>>.FromConverter(conv);
        }

        public static FSharpFunc<T1, FSharpFunc<T2, FSharpFunc<T3, TResult>>> Create<T1, T2, T3, TResult>(
            Func<T1, T2, T3, TResult> func)
        {
            Converter<T1, FSharpFunc<T2, FSharpFunc<T3, TResult>>> conv =
                value1 => { return Create<T2, T3, TResult>((value2, value3) => func(value1, value2, value3)); };
            return FSharpFunc<T1, FSharpFunc<T2, FSharpFunc<T3, TResult>>>.FromConverter(conv);
        }
    }
}
#endif
