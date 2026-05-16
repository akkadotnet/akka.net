//-----------------------------------------------------------------------
// <copyright file="MsgPackSerializedMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Allocation-free equivalent of <c>SerializedMessage</c> (protobuf <c>Payload</c>) for the
    /// MessagePack inbound hot path.
    ///
    /// <para>
    /// The protobuf <c>Payload</c> type stores message bytes and manifest in <c>ByteString</c>
    /// fields, which requires a heap allocation and a copy on every inbound message.
    /// This struct instead holds <see cref="ReadOnlyMemory{T}"/> slices that point directly
    /// into the buffers owned by the MessagePack deserializer — no copy, no extra allocation.
    /// </para>
    ///
    /// <para>
    /// On the dispatch path (<see cref="IInboundMessageDispatcher"/>) this struct is passed as
    /// <c>in</c> (by ref-to-const) so the 24-byte value is never copied to the heap either.
    /// </para>
    ///
    /// <!-- CopilotNotes: ManifestString is computed lazily via a property; for the common
    ///      case where manifest is empty (primitive serializers) the UTF-8 decode is skipped
    ///      entirely. Direct callers of Serialization.Deserialize(ReadOnlyMemory<byte>, int, string?)
    ///      use this to avoid the ByteString.ToStringUtf8() allocation present in the    
    ///      SerializedMessage code path. -->
    /// </summary>
    internal class MsgPackSerializedMessage
    {
        /// <summary>
        /// Creates a new <see cref="MsgPackSerializedMessage"/>.
        /// </summary>
        /// <param name="bytes">Raw serialized actor message bytes (zero-copy from MpPayload).</param>
        /// <param name="serializerId">Akka serializer identifier.</param>
        /// <param name="manifest">
        /// UTF-8-encoded type manifest; pass <see cref="ReadOnlyMemory{T}.Empty"/> when no manifest is needed.
        /// </param>
        public MsgPackSerializedMessage(ReadOnlyMemory<byte> bytes, int serializerId, ReadOnlyMemory<byte> manifest)
        {
            Bytes = bytes;
            SerializerId = serializerId;
            Manifest = manifest;
        }

        /// <summary>The raw serialized actor message bytes.</summary>
        public readonly ReadOnlyMemory<byte> Bytes;

        /// <summary>Akka serializer identifier (matches <c>Payload.serializerId</c>).</summary>
        public readonly int SerializerId;

        /// <summary>
        /// Optional UTF-8-encoded type manifest; <see cref="ReadOnlyMemory{T}.Empty"/> when no manifest
        /// is present (matches the <c>Payload.messageManifest</c> absent case).
        /// </summary>
        public readonly ReadOnlyMemory<byte> Manifest;

        /// <summary>
        /// Lazily decodes <see cref="Manifest"/> to a <see cref="string"/>, returning
        /// <c>null</c> when the manifest is empty.  Avoids heap allocation for the common
        /// no-manifest case.
        /// </summary>
        public string? ManifestString =>
            Manifest.IsEmpty ? null : Encoding.UTF8.GetString(Manifest.Span);

        /// <summary>Returns a diagnostic string for log messages.</summary>
        public override string ToString() =>
            $"MsgPackSerializedMessage(SerializerId={SerializerId}, PayloadBytes={Bytes.Length}, Manifest={ManifestString ?? "<none>"})";
    }
}

