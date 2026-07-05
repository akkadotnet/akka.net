//-----------------------------------------------------------------------
// <copyright file="IAkkaMessagePackFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// A hand-written MessagePack formatter for a foreign type that the <c>Akka.Serialization.V2</c>
/// source generator cannot annotate with <see cref="AkkaSerializableAttribute"/> (for example, a
/// core Akka type such as <see cref="Akka.Actor.Address"/>, which cannot reference this assembly).
/// </summary>
/// <remarks>
/// <para>
/// Register an implementation for a specific <typeparamref name="T"/> on a generated serializer
/// using <see cref="AkkaSerializerFormatterAttribute"/>. The generator then routes every field of
/// type <typeparamref name="T"/> (including <c>Nullable&lt;T&gt;</c> for value types) through this
/// formatter instead of requiring a nested <see cref="AkkaSerializableAttribute"/> definition.
/// </para>
/// <para>
/// <see cref="Write"/> and <see cref="Read"/> must be symmetric: reading the bytes written by
/// <see cref="Write"/> must reproduce an equivalent <typeparamref name="T"/> value.
/// </para>
/// <para>
/// The <c>value</c> passed to <see cref="Write"/> is never a representation of "absent" when the
/// declaring field is non-nullable. The generator owns MessagePack nil encoding for nullable
/// fields and only calls into the formatter for present values.
/// </para>
/// <para>
/// <see cref="SizeOf"/> must return the exact number of bytes <see cref="Write"/> will produce for
/// the same value, or <see cref="Akka.Serialization.SerializerV2.UnknownSize"/> when the exact size
/// cannot be cheaply computed. Returning an incorrect non-negative value corrupts the enclosing
/// generated serializer's <c>SizeHint</c> contract.
/// </para>
/// </remarks>
/// <typeparam name="T">The foreign type this formatter reads and writes.</typeparam>
public interface IAkkaMessagePackFormatter<T>
{
    /// <summary>
    /// Writes <paramref name="value"/> to <paramref name="writer"/>.
    /// </summary>
    /// <remarks>
    /// Write MUST produce exactly ONE top-level MessagePack value; wrap multiple values in a
    /// single array or map. The generated map framing and the unknown-field forward-compatibility
    /// path (<c>reader.Skip()</c>) both depend on one field id mapping to one MessagePack value —
    /// multiple top-level values desync older readers during rolling upgrades.
    /// </remarks>
    /// <param name="writer">The MessagePack writer cursor.</param>
    /// <param name="value">The value to write. Never null/absent for non-nullable fields.</param>
    void Write(ref MessagePackWriter writer, T value);

    /// <summary>
    /// Reads a <typeparamref name="T"/> value previously written by <see cref="Write"/>.
    /// </summary>
    /// <param name="reader">The MessagePack reader cursor.</param>
    T Read(ref MessagePackReader reader);

    /// <summary>
    /// Returns the exact encoded byte count for <paramref name="value"/>, or
    /// <see cref="Akka.Serialization.SerializerV2.UnknownSize"/> when the exact size cannot be
    /// cheaply computed.
    /// </summary>
    /// <remarks>
    /// For transport-sensitive formatters (anything whose encoding consults the thread-static
    /// transport context, such as <see cref="ActorPathFormatter"/>), <see cref="SizeOf"/> and
    /// <see cref="Write"/> read that context independently: both calls must run under the same
    /// transport scope on the same thread for the exact-size contract to hold. The generated
    /// serializers and the Artery encode path satisfy this naturally by sizing and writing within
    /// one serialization scope.
    /// </remarks>
    /// <param name="value">The value whose encoded size is being computed.</param>
    int SizeOf(T value);
}
