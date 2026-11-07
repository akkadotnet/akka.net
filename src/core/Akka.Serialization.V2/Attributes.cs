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
/// registration. It is purely generator input: the <see cref="AkkaSerializer"/> base class
/// is non-generic and carries no protocol knowledge of its own.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AkkaSerializerAttribute<TProtocol> : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AkkaSerializerAttribute{TProtocol}"/> class.
    /// </summary>
    /// <param name="name">Logical serializer alias used for Akka serializer registration.</param>
    /// <param name="serializerId">Explicit Akka serializer identifier. Must be unique in the actor system.</param>
    /// <remarks>
    /// Both arguments are mandatory and always explicit -- there is no auto-assignment. A source
    /// generator's registration order across a compilation is nondeterministic, and a collision
    /// with a serializer id or alias defined elsewhere (for example in HOCON config) is invisible
    /// to the generator, so there is no value it could safely default either argument to.
    /// </remarks>
    public AkkaSerializerAttribute(string name, int serializerId)
    {
        Name = name;
        SerializerId = serializerId;
    }

    /// <summary>
    /// Logical serializer alias used for Akka serializer registration.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Explicit Akka serializer identifier. Must be unique in the actor system.
    /// </summary>
    public int SerializerId { get; }
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
/// Includes a type in a generated serializer: <typeparamref name="TMessage"/> becomes one of the
/// serializer's top-level messages, with its own dispatch arm and its own binding in the generated
/// <c>CreateSerializerDetails(system)</c>, even though it does not implement the serializer's
/// protocol interface. Apply it to the <c>[AkkaSerializer]</c> class, for example
/// <c>[AkkaSerializerIncludes&lt;ChatResponse&gt;]</c> or
/// <c>[AkkaSerializerIncludes&lt;Wrapper&lt;OrderPlaced&gt;&gt;(Manifest = "wrapper-order-placed")]</c>.
/// </summary>
/// <remarks>
/// <para>
/// The included type still describes itself with <see cref="AkkaSerializableAttribute"/>: its
/// <see cref="AkkaFieldAttribute"/> indices, and, for an ordinary type, its manifest. This attribute
/// only says which serializer owns it. An ordinary type's manifest therefore lives on the type
/// alone; setting <see cref="Manifest"/> here for a non-generic type is a build error.
/// </para>
/// <para>
/// A closed generic construction is the exception. A Roslyn source generator cannot reify open
/// generics: it can only emit concrete serialization code for closed constructions it can see at
/// compile time (the same rule System.Text.Json's source generator enforces by rejecting unbound
/// generics in <c>[JsonSerializable]</c>). A construction such as <c>Wrapper&lt;OrderPlaced&gt;</c>
/// has no declaration of its own to carry a manifest, so it takes its <see cref="Manifest"/> (or a
/// <see cref="ManifestPrefix"/> expansion) from this attribute. The generic definition itself must
/// still be annotated <c>[AkkaSerializable]</c> (that is where the <c>[AkkaField]</c> indices
/// live), but the open definition is never serialized, only its included closed constructions are.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AkkaSerializerIncludesAttribute<TMessage> : Attribute
{
    /// <summary>
    /// Stable serializer-owned manifest for a closed generic construction. Used for top-level
    /// dispatch, and as the union discriminator when the construction is a union member. Only a
    /// closed generic construction takes a manifest here: a non-generic type declares its own
    /// manifest with <see cref="AkkaSerializableAttribute.Manifest"/>.
    /// </summary>
    /// <remarks>
    /// When <typeparamref name="TMessage"/> also has a <see cref="ManifestPrefix"/> set,
    /// <see cref="Manifest"/> includes the literal construction alongside the expansion
    /// <see cref="ManifestPrefix"/> produces, overriding the manifest the formula would otherwise
    /// derive for that one construction.
    /// </remarks>
    public string? Manifest { get; init; }

    /// <summary>
    /// Expands this registration over the closed member set of <typeparamref name="TMessage"/>'s
    /// type argument, instead of registering only the single construction named by
    /// <typeparamref name="TMessage"/> itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A type argument has a closed set when it is the serializer's own protocol interface (see
    /// <see cref="AkkaSerializerAttribute{TProtocol}"/>), any other interface or abstract class, or a
    /// closed hierarchy (a C# 15 <c>closed</c> class or record, or a class whose constructors are all
    /// private with nested derived types). Its members are the <see cref="AkkaSerializableAttribute"/>
    /// types that derive from or implement it. For example,
    /// <c>[AkkaSerializerIncludes&lt;Envelope&lt;ICommsMessage&gt;&gt;(ManifestPrefix = "env")]</c> on a
    /// serializer whose protocol is <c>ICommsMessage</c> registers one closed construction of
    /// <c>Envelope&lt;T&gt;</c> per member of the protocol set -- <c>Envelope&lt;AcceptCassette&gt;</c>,
    /// <c>Envelope&lt;OrderCancelled&gt;</c>, and so on -- each with its own generated dispatch arm,
    /// private helpers, and a manifest derived from <see cref="ManifestPrefix"/> and the member's own
    /// manifest: <c>"env/dmac"</c>, <c>"env/ocan"</c>, and so on. The rule applies to every type
    /// argument, so a multi-argument generic expands to the product of its arguments' closed sets.
    /// </para>
    /// <para>
    /// <see cref="ManifestPrefix"/> alone does not register the literal construction (the one whose
    /// type argument is the closed-set type itself, for example <c>Envelope&lt;ICommsMessage&gt;</c>);
    /// set <see cref="Manifest"/> as well to also register it. A field whose static type is the
    /// closed-set type is treated as a union over that same closed set. Setting <see cref="ManifestPrefix"/> on
    /// a registration whose type argument has no closed set (a concrete class, for example) is a
    /// compile-time error: a concrete class has nothing to expand over.
    /// </para>
    /// </remarks>
    public string? ManifestPrefix { get; init; }
}

/// <summary>
/// Registers a hand-written <see cref="IAkkaMessagePackFormatter{T}"/> for a foreign type that
/// cannot be annotated with <see cref="AkkaSerializableAttribute"/> (for example, a core Akka type
/// that cannot reference <c>Akka.Serialization.V2</c>).
/// </summary>
/// <remarks>
/// Apply to the <c>[AkkaSerializer]</c> partial class, for example
/// <c>[AkkaSerializerFormatter&lt;Address, AddressFormatter&gt;]</c>. The registration is
/// serializer-scoped: the same foreign type may be handled by different formatters (or not at all)
/// in different serializers. A formatter registration overrides every field-kind resolution the
/// generator would otherwise infer for <typeparamref name="TTarget"/> (including
/// <c>Nullable&lt;T&gt;</c> of a value type), except a field whose static type is <c>object</c>
/// (or <c>object?</c>), which is always the envelope boundary and always wins.
/// The <c>where TFormatter : IAkkaMessagePackFormatter&lt;TTarget&gt;</c> constraint is enforced by
/// the compiler at the attribute usage site: <typeparamref name="TFormatter"/> can never be
/// something that does not implement the formatter interface for <typeparamref name="TTarget"/>.
/// Deliberately no <c>new()</c> constraint: a formatter with only a public constructor taking an
/// <see cref="Akka.Actor.ExtendedActorSystem"/> (no parameterless constructor) is legitimate (see
/// <typeparamref name="TFormatter"/>).
/// </remarks>
/// <typeparam name="TTarget">The foreign type handled by <typeparamref name="TFormatter"/>.</typeparam>
/// <typeparam name="TFormatter">
/// A non-abstract class implementing <see cref="IAkkaMessagePackFormatter{T}"/> for
/// <typeparamref name="TTarget"/>, with either a public parameterless constructor or a public
/// constructor taking an <see cref="Akka.Actor.ExtendedActorSystem"/>. When both constructors are
/// present, the generated serializer prefers the <see cref="Akka.Actor.ExtendedActorSystem"/>
/// constructor: the serializer always has the system in hand, and system context is why a
/// formatter declares that constructor.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class AkkaSerializerFormatterAttribute<TTarget, TFormatter> : Attribute
    where TFormatter : IAkkaMessagePackFormatter<TTarget>
{
}
