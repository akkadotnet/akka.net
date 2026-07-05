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

/// <summary>
/// Registers a hand-written <see cref="IAkkaMessagePackFormatter{T}"/> for a foreign type that
/// cannot be annotated with <see cref="AkkaSerializableAttribute"/> (for example, a core Akka type
/// that cannot reference <c>Akka.Serialization.V2</c>).
/// </summary>
/// <remarks>
/// Apply to the <c>[AkkaSerializer]</c> partial class. The registration is serializer-scoped: the
/// same foreign type may be handled by different formatters (or not at all) in different
/// serializers. A formatter registration overrides every field-kind resolution the generator would
/// otherwise infer for <see cref="SerializedType"/> (including <c>Nullable&lt;T&gt;</c> of a value
/// type), except an <see cref="AkkaEnvelopePayloadAttribute"/>-marked field, which always wins.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AkkaSerializerFormatterAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AkkaSerializerFormatterAttribute"/> class.
    /// </summary>
    /// <param name="serializedType">The foreign type handled by <paramref name="formatterType"/>.</param>
    /// <param name="formatterType">
    /// A non-abstract, non-generic class implementing <see cref="IAkkaMessagePackFormatter{T}"/>
    /// for <paramref name="serializedType"/>, with either a public parameterless constructor or a
    /// public constructor taking an <see cref="Akka.Actor.ExtendedActorSystem"/>. When both
    /// constructors are present, the generated serializer prefers the
    /// <see cref="Akka.Actor.ExtendedActorSystem"/> constructor: the serializer always has the
    /// system in hand, and system context is why a formatter declares that constructor.
    /// </param>
    public AkkaSerializerFormatterAttribute(Type serializedType, Type formatterType)
    {
        SerializedType = serializedType;
        FormatterType = formatterType;
    }

    /// <summary>
    /// The foreign type handled by <see cref="FormatterType"/>.
    /// </summary>
    public Type SerializedType { get; }

    /// <summary>
    /// The formatter type implementing <see cref="IAkkaMessagePackFormatter{T}"/> for
    /// <see cref="SerializedType"/>.
    /// </summary>
    public Type FormatterType { get; }
}
