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
/// Marks a partial serializer module that the source generator should implement, bound to the
/// protocol marker interface <typeparamref name="TProtocol"/>.
/// </summary>
/// <remarks>
/// <typeparamref name="TProtocol"/> selects which <see cref="AkkaSerializableAttribute"/> messages
/// this serializer dispatches at the top level (those implementing the marker) and becomes the
/// type this serializer is bound to in Akka's <c>serialization-bindings</c> via the generated
/// registration. It is purely generator input: the <see cref="MessagePackSerializer"/> base class
/// is non-generic and carries no protocol knowledge of its own.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AkkaSerializerAttribute<TProtocol> : Attribute
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

    /// <summary>
    /// Opts a deliberately fieldless message into codegen. By default, an <see cref="AkkaSerializableAttribute"/>
    /// type with no <see cref="AkkaFieldAttribute"/> properties is rejected (AKKASG004): almost always a mistake
    /// (a forgotten <see cref="AkkaFieldAttribute"/>), but some protocol messages are legitimately fieldless --
    /// for example, a heartbeat whose arrival IS the signal, with no payload to carry. Set this to
    /// <see langword="true"/> to generate an empty-map write and a skip-loop read (tolerating unknown fields for
    /// forward compatibility) instead of failing compilation.
    /// </summary>
    public bool AllowEmpty { get; init; }
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
/// Declares a closed, explicitly-enumerated union of concrete
/// <see cref="AkkaSerializableAttribute"/> member types for an interface or abstract base -- or,
/// applied to an <see cref="AkkaFieldAttribute"/> property, overrides the member set for that one
/// field.
/// </summary>
/// <remarks>
/// <para>
/// The natural declaration site is the union's base TYPE (the interface or abstract class), where
/// the member set is stated once -- mirroring <c>System.Text.Json</c>'s <c>[JsonDerivedType]</c>
/// and the case list of the proposed C# language unions. Every field whose static type carries a
/// type-level union inherits its member set. A field-level application overrides the type-level
/// set for that field only (for example, to narrow the members a particular schema accepts).
/// </para>
/// <para>
/// Unlike <see cref="AkkaEnvelopePayloadAttribute"/> (a runtime serializer boundary for payloads
/// whose concrete type may live in an assembly unknown at compile time), a union field is encoded
/// structurally inline: the generator emits compile-time dispatch over the declared member set,
/// discriminated by each member's <see cref="AkkaSerializableAttribute.Manifest"/>. Every member
/// must be <c>[AkkaSerializable]</c>, declare a manifest unique within the union, and be assignable
/// to the field's static type. A runtime value whose exact type is not a declared member fails
/// serialization. When both this attribute and <see cref="AkkaEnvelopePayloadAttribute"/> are
/// present on a field, the envelope payload marker wins (consistent with its precedence over
/// formatter registrations).
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Interface | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AkkaUnionAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AkkaUnionAttribute"/> class.
    /// </summary>
    /// <param name="memberTypes">The closed set of concrete member types this field may hold.</param>
    public AkkaUnionAttribute(params Type[] memberTypes)
    {
        MemberTypes = memberTypes;
    }

    /// <summary>
    /// The closed set of concrete member types this field may hold.
    /// </summary>
    public Type[] MemberTypes { get; }
}

/// <summary>
/// Registers a CLOSED generic construction of a generic <see cref="AkkaSerializableAttribute"/>
/// type (for example <c>[AkkaSerializable&lt;Wrapper&lt;OrderPlaced&gt;&gt;]</c>) with a generated
/// serializer. Everything on the wire is marked <c>[AkkaSerializable]</c>: ordinary types carry
/// the non-generic form on their own declaration; a closed generic construction has no
/// declaration site of its own, so its marking lives here, on the <c>[AkkaSerializer]</c> class.
/// </summary>
/// <remarks>
/// A Roslyn source generator cannot reify open generics: it can only emit concrete serialization
/// code for closed constructions it can see at compile time (the same rule System.Text.Json's
/// source generator enforces by rejecting unbound generics in <c>[JsonSerializable]</c>). Each
/// registered construction behaves exactly like its own top-level message: it needs a distinct
/// <see cref="Manifest"/>, participates in ordinary manifest dispatch, and its generic fields are
/// resolved against the concrete type arguments. The generic type definition itself must still be
/// annotated <c>[AkkaSerializable]</c> (that is where the <c>[AkkaField]</c> indices live), but
/// the open definition is never serialized — only its registered closed constructions are.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AkkaSerializableAttribute<TMessage> : Attribute
{
    /// <summary>
    /// Stable serializer-owned manifest for this closed construction. Required when the
    /// construction implements the serializer's protocol interface (top-level dispatch); also the
    /// union discriminator when the construction is an <see cref="AkkaUnionAttribute"/> member.
    /// </summary>
    public string? Manifest { get; init; }
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
