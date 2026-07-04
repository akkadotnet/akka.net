//-----------------------------------------------------------------------
// <copyright file="ArteryConnectionHeader.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Result of <see cref="ArteryConnectionHeader.TryParse"/>. Together with the exception
    /// thrown for malformed input, this makes the parse outcome a tri-state:
    /// <see cref="NeedMoreData"/> (not an error — the caller should append more bytes and
    /// retry), <see cref="Success"/>, or an <see cref="ArteryFramingException"/> thrown
    /// synchronously for a structurally invalid preamble (bad magic / unknown stream id).
    /// </summary>
    internal enum ArteryConnectionHeaderParseResult
    {
        /// <summary>Fewer than <see cref="ArteryConnectionHeader.Length"/> bytes were available; not an error.</summary>
        NeedMoreData = 0,

        /// <summary>A well-formed preamble was parsed; <c>streamId</c>/<c>bytesConsumed</c> out-parameters are valid.</summary>
        Success = 1
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Codec for the Artery TCP connection preamble — written exactly once per TCP
    /// connection, before any framed messages: 4 bytes of ASCII <c>AKKA</c> magic followed
    /// by a 1-byte <see cref="ArteryStreamId"/>. See
    /// <c>openspec/changes/artery-tcp-remoting/design.md</c> ("Envelope wire layout" /
    /// Decision 3): <c>Connection preamble (once per TCP connection): AKKA magic (4B) +
    /// stream id (1B: 1=control, 2=ordinary, 3=large)</c>.
    ///
    /// <para>
    /// This type is a pure codec with no buffering of its own; it is intended to be wrapped
    /// by a stream/GraphStage in a later Artery task that owns the incoming byte buffer and
    /// retries <see cref="TryParse"/> as more bytes arrive.
    /// </para>
    /// </summary>
    internal static class ArteryConnectionHeader
    {
        /// <summary>ASCII magic byte 'A'.</summary>
        private const byte MagicA = (byte)'A';

        /// <summary>ASCII magic byte 'K'.</summary>
        private const byte MagicK = (byte)'K';

        /// <summary>Total length of the connection preamble, in bytes: 4-byte magic + 1-byte stream id.</summary>
        public const int Length = 5;

        /// <summary>Byte offset of the stream-id byte within the preamble.</summary>
        private const int StreamIdOffset = 4;

        /// <summary>
        /// Writes the 5-byte connection preamble (<c>AKKA</c> magic + stream id) to
        /// <paramref name="destination"/>.
        /// </summary>
        /// <param name="destination">
        /// The destination span. Must be at least <see cref="Length"/> bytes long.
        /// </param>
        /// <param name="streamId">The stream identifier to write.</param>
        /// <exception cref="ArgumentException">
        /// <paramref name="destination"/> is shorter than <see cref="Length"/>.
        /// </exception>
        public static void WriteTo(Span<byte> destination, ArteryStreamId streamId)
        {
            if (destination.Length < Length)
                throw new ArgumentException(
                    $"Destination span must be at least {Length} bytes long to hold the Artery connection preamble, but was {destination.Length}.",
                    nameof(destination));

            destination[0] = MagicA;
            destination[1] = MagicK;
            destination[2] = MagicK;
            destination[3] = MagicA;
            destination[StreamIdOffset] = (byte)streamId;
        }

        /// <summary>
        /// Attempts to parse a connection preamble from the front of <paramref name="buffer"/>.
        /// </summary>
        /// <param name="buffer">
        /// The accumulated, not-yet-consumed bytes read from the connection so far. Only the
        /// first <see cref="Length"/> bytes are inspected; any bytes beyond that are left
        /// untouched (the caller advances its own buffer by <paramref name="bytesConsumed"/>
        /// on <see cref="ArteryConnectionHeaderParseResult.Success"/>).
        /// </param>
        /// <param name="streamId">
        /// On <see cref="ArteryConnectionHeaderParseResult.Success"/>, the parsed stream id.
        /// Undefined (default) otherwise.
        /// </param>
        /// <param name="bytesConsumed">
        /// On <see cref="ArteryConnectionHeaderParseResult.Success"/>, always <see cref="Length"/>.
        /// Zero otherwise.
        /// </param>
        /// <returns>
        /// <see cref="ArteryConnectionHeaderParseResult.NeedMoreData"/> if fewer than
        /// <see cref="Length"/> bytes are available (not an error — the caller should append
        /// more data and retry); <see cref="ArteryConnectionHeaderParseResult.Success"/> once
        /// a well-formed preamble is parsed.
        /// </returns>
        /// <exception cref="ArteryFramingException">
        /// The magic bytes are not <c>AKKA</c>, or the stream-id byte is not one of the
        /// values defined by <see cref="ArteryStreamId"/>.
        /// </exception>
        public static ArteryConnectionHeaderParseResult TryParse(
            ReadOnlySequence<byte> buffer,
            out ArteryStreamId streamId,
            out int bytesConsumed)
        {
            streamId = default;
            bytesConsumed = 0;

            if (buffer.Length < Length)
                return ArteryConnectionHeaderParseResult.NeedMoreData;

            Span<byte> header = stackalloc byte[Length];
            buffer.Slice(0, Length).CopyTo(header);

            if (header[0] != MagicA || header[1] != MagicK || header[2] != MagicK || header[3] != MagicA)
                throw new ArteryFramingException(
                    $"Invalid Artery connection preamble: expected 'AKKA' magic bytes, got " +
                    $"[0x{header[0]:X2} 0x{header[1]:X2} 0x{header[2]:X2} 0x{header[3]:X2}].");

            var streamIdByte = header[StreamIdOffset];
            streamId = streamIdByte switch
            {
                (byte)ArteryStreamId.Control => ArteryStreamId.Control,
                (byte)ArteryStreamId.Ordinary => ArteryStreamId.Ordinary,
                (byte)ArteryStreamId.Large => ArteryStreamId.Large,
                _ => throw new ArteryFramingException(
                    $"Unknown Artery stream id byte {streamIdByte}; expected 1 (Control), 2 (Ordinary), or 3 (Large).")
            };

            bytesConsumed = Length;
            return ArteryConnectionHeaderParseResult.Success;
        }
    }
}
