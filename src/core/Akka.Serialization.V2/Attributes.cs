//-----------------------------------------------------------------------
// <copyright file="Attributes.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;

namespace Akka.Serialization.V2;

/// <summary>
/// Marks a partial serializer module that the source generator should implement.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AkkaSerializerAttribute : Attribute
{
    /// <summary>
    /// Logical serializer alias used for Akka serializer registration.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Explicit Akka serializer identifier. Must be unique in the actor system.
    /// </summary>
    public int SerializerId { get; init; }
}

/// <summary>
/// Marks a type that should be handled by a generated serializer.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class AkkaSerializableAttribute : Attribute
{
    /// <summary>
    /// Stable serializer-owned manifest for top-level protocol messages.
    /// </summary>
    public string? Manifest { get; init; }
}

/// <summary>
/// Marks a property as a serialized field with a stable field index.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class AkkaFieldAttribute : Attribute
{
    public AkkaFieldAttribute(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Stable zero-based field index.
    /// </summary>
    public int Index { get; }
}

/// <summary>
/// Marks an <see cref="AkkaFieldAttribute"/> property as an Akka serializer boundary.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class AkkaEnvelopePayloadAttribute : Attribute
{
}
