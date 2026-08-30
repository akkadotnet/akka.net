//-----------------------------------------------------------------------
// <copyright file="IsExternalInitPolyfill.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#if NETFRAMEWORK
namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Reserved to be used by the compiler for tracking metadata. This
    /// type should not be used by developers in source code. Compiler-
    /// emitted modreq on init-only properties references this type;
    /// on .NET Framework targets the type is missing from the BCL, so
    /// we provide it locally to keep records / <c>with</c> expressions
    /// usable in this test assembly.
    /// </summary>
    internal static class IsExternalInit { }
}
#endif
