// -----------------------------------------------------------------------
// <copyright file="IsExternalInit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#if NETSTANDARD2_1

// Shim for init-only properties and records on netstandard2.1.
// The type System.Runtime.CompilerServices.IsExternalInit is defined in .NET 5+;
// providing it here allows C# 9+ features (init, record) to compile on netstandard2.1.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}

#endif
