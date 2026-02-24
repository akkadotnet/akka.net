//-----------------------------------------------------------------------
// <copyright file="FSharpDelegateHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Microsoft.FSharp.Core;

// ReSharper disable once CheckNamespace
namespace Akka.Tests.Shared.Internals.Helpers;

/// <summary>
/// Maps F# methods to C# delegates
/// </summary>
public static class FsharpDelegateHelper
{
    public static FSharpFunc<T2, TResult> Create<T2, TResult>(Func<T2, TResult> func)
    {
        return FSharpFunc<T2, TResult>.FromConverter(Conv);
        
        TResult Conv(T2 input) => func(input);
    }

    public static FSharpFunc<T1, FSharpFunc<T2, TResult>> Create<T1, T2, TResult>(Func<T1, T2, TResult> func)
    {
        return FSharpFunc<T1, FSharpFunc<T2, TResult>>.FromConverter(Conv);
        
        FSharpFunc<T2, TResult> Conv(T1 value1)
        {
            return Create<T2, TResult>(value2 => func(value1, value2));
        }
    }

    public static FSharpFunc<T1, FSharpFunc<T2, FSharpFunc<T3, TResult>>> Create<T1, T2, T3, TResult>(
        Func<T1, T2, T3, TResult> func)
    {
        return FSharpFunc<T1, FSharpFunc<T2, FSharpFunc<T3, TResult>>>.FromConverter(Conv);
        
        FSharpFunc<T2, FSharpFunc<T3, TResult>> Conv(T1 value1)
        {
            return Create<T2, T3, TResult>((value2, value3) => func(value1, value2, value3));
        }
    }
}