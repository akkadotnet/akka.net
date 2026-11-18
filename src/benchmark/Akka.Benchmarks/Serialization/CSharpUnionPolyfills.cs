//-----------------------------------------------------------------------
// <copyright file="CSharpUnionPolyfills.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

// The C# 15 union types a net10.0 project declares itself (net11.0 ships them in the BCL). Used by
// GeneratedMessagePackCSharpUnionBenchmarks; internal so they never leak out of this assembly.
#if !NET11_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    internal sealed class UnionAttribute : Attribute
    {
    }

    internal interface IUnion
    {
        object? Value { get; }
    }
}
#endif
