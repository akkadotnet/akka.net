//-----------------------------------------------------------------------
// <copyright file="IsExternalInit.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

// Shim for init-only properties and records on netstandard2.0, this project's only target
// framework. System.Runtime.CompilerServices.IsExternalInit is defined in .NET 5+; providing it
// here lets the C# 12 compiler (see Directory.Build.props' LangVersion) emit `init` accessors and
// `record`/`record struct` types against netstandard2.0. Used by the small, immutable location
// models in AkkaSerializerGenerator.Locations.cs (LocationSpec, LocationKey, ExtractedSerializer,
// ExtractedMessage) -- everything else in this generator predates C# 9 record syntax and stays as
// hand-written structs/classes, so this shim is scoped to exactly the types that need it.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
