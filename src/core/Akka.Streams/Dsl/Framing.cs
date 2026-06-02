//-----------------------------------------------------------------------
// <copyright file="Framing.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal.Collections;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class Framing
    {
        /// <summary>
        /// Creates a Flow that handles decoding a stream of unstructured byte chunks into a stream of frames where the
        /// incoming chunk stream uses a specific byte-sequence to mark frame boundaries.
        ///
        /// The decoded frames will not include the separator sequence.
        ///
        /// If there are buffered bytes (an incomplete frame) when the input stream finishes and <paramref name="allowTruncation"/> is set to
        /// false then this Flow will fail the stream reporting a truncated frame.
        /// </summary>
        /// <param name="delimiter">The byte sequence to be treated as the end of the frame.</param>
        /// <param name="maximumFrameLength">The maximum length of allowed frames while decoding. If the maximum length is exceeded this Flow will fail the stream.</param>
        /// <param name="allowTruncation">If false, then when the last frame being decoded contains no valid delimiter this Flow fails the stream instead of returning a truncated frame.</param>
        /// <returns>TBD</returns>
        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> Delimiter(ReadOnlySequence<byte> delimiter, int maximumFrameLength,
            bool allowTruncation = false)
        {
            return Flow.Create<ReadOnlySequence<byte>>()
                .Via(new DelimiterFramingStage(delimiter, maximumFrameLength, allowTruncation))
                .Named("DelimiterFraming");
        }

        /// <summary>
        /// Convenience overload accepting <see cref="ReadOnlyMemory{T}"/> for the delimiter.
        /// </summary>
        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> Delimiter(ReadOnlyMemory<byte> delimiter, int maximumFrameLength,
            bool allowTruncation = false)
            => Delimiter(new ReadOnlySequence<byte>(delimiter), maximumFrameLength, allowTruncation);

        /// <summary>
        /// Creates a Flow that decodes an incoming stream of unstructured byte chunks into a stream of frames, assuming that
        /// incoming frames have a field that encodes their length.
        ///
        /// If the input stream finishes before the last frame has been fully decoded, this Flow will fail the stream reporting
        /// a truncated frame.
        /// </summary>
        /// <param name="fieldLength">The length of the "Count" field in bytes</param>
        /// <param name="maximumFramelength">The maximum length of allowed frames while decoding. If the maximum length is exceeded this Flow will fail the stream. This length *includes* the header (i.e the offset and the length of the size field)</param>
        /// <param name="fieldOffset">The offset of the field from the beginning of the frame in bytes</param>
        /// <param name="byteOrder">The <see cref="ByteOrder"/> to be used when decoding the field</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="fieldLength"/> is not equal to either 1, 2, 3 or 4.
        /// </exception>
        /// <returns>TBD</returns>
        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> LengthField(int fieldLength, int maximumFramelength,
            int fieldOffset = 0, ByteOrder byteOrder = ByteOrder.LittleEndian)
        {
            if (fieldLength is < 1 or > 4)
                throw new ArgumentException("Length field length must be 1,2,3 or 4", nameof(fieldLength));

            return Flow.Create<ReadOnlySequence<byte>>()
                .Via(new LengthFieldFramingStage(fieldLength, maximumFramelength, fieldOffset, byteOrder))
                .Named("LengthFieldFraming");
        }

        /// <summary>
        /// Creates a Flow that decodes an incoming stream of unstructured byte chunks into a stream of frames, assuming that
        /// incoming frames have a field that encodes their length.
        /// <para>
        /// If the input stream finishes before the last frame has been fully decoded, this Flow will fail the stream reporting
        /// a truncated frame.
        /// </para>
        /// </summary>
        /// <param name="fieldLength">The length of the "Count" field in bytes</param>
        /// <param name="maximumFrameLength">The maximum length of allowed frames while decoding. If the maximum length is exceeded this Flow will fail the stream. This length *includes* the header (i.e the offset and the length of the size field)</param>
        /// <param name="fieldOffset">The offset of the field from the beginning of the frame in bytes.</param>
        /// <param name="byteOrder">The <see cref="ByteOrder"/> to be used when decoding the field.</param>
        /// <param name="computeFrameSize">
        /// This function can be supplied if frame size is varied or needs to be computed in a special fashion.
        /// For example, frame can have a shape like this: `[offset bytes][body size bytes][body bytes][footer bytes]`.
        /// Then computeFrameSize can be used to compute the frame size: `(offset bytes, computed size) => (actual frame size)`.
        /// "Actual frame size" must be equal or bigger than sum of `fieldOffset` and `fieldLength`, the operator fails otherwise.
        /// </param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="fieldLength"/> is not equal to either 1, 2, 3 or 4.
        /// </exception>
        /// <returns>TBD</returns>
        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> LengthField(
            int fieldLength,
            int fieldOffset,
            int maximumFrameLength,
            ByteOrder byteOrder,
            Func<IReadOnlyList<byte>, int, int> computeFrameSize)
        {
            if (fieldLength is < 1 or > 4)
                throw new ArgumentException("Length field length must be 1,2,3 or 4", nameof(fieldLength));

            return Flow.Create<ReadOnlySequence<byte>>()
                .Via(new LengthFieldFramingStage(fieldLength, maximumFrameLength, fieldOffset, byteOrder, computeFrameSize))
                .Named("LengthFieldFraming");
        }

        /// <summary>
        /// Returns a BidiFlow that implements a simple framing protocol. This is a convenience wrapper over <see cref="LengthField(int, int, int, ByteOrder)"/>
        /// and simply attaches a length field header of four bytes (using big endian encoding) to outgoing messages, and decodes
        /// such messages in the inbound direction. The decoded messages do not contain the header.
        ///
        /// This BidiFlow is useful if a simple message framing protocol is needed (for example when TCP is used to send
        /// individual messages) but no compatibility with existing protocols is necessary.
        ///
        /// The encoded frames have the layout
        /// {{{
        ///     [4 bytes length field, Big Endian][User Payload]
        /// }}}
        /// The length field encodes the length of the user payload excluding the header itself.
        /// </summary>
        /// <param name="maximumMessageLength">Maximum length of allowed messages. If sent or received messages exceed the configured limit this BidiFlow will fail the stream. The header attached by this BidiFlow are not included in this limit.</param>
        /// <returns>TBD</returns>
        public static BidiFlow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> SimpleFramingProtocol(int maximumMessageLength)
        {
            return BidiFlow.FromFlowsMat(SimpleFramingProtocolEncoder(maximumMessageLength),
                SimpleFramingProtocolDecoder(maximumMessageLength), Keep.Left);
        }

        /// <summary>
        /// Protocol decoder that is used by <see cref="SimpleFramingProtocol"/>
        /// </summary>
        /// <param name="maximumMessageLength">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> SimpleFramingProtocolDecoder(int maximumMessageLength)
        {
            return LengthField(4, maximumMessageLength + 4, 0, ByteOrder.BigEndian).Select(b => b.Slice(4));
        }

        /// <summary>
        /// Protocol encoder that is used by <see cref="SimpleFramingProtocol"/>
        /// </summary>
        /// <param name="maximumMessageLength">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> SimpleFramingProtocolEncoder(int maximumMessageLength)
        {
            return Flow.Create<ReadOnlySequence<byte>>().Via(new SimpleFramingProtocolEncoderStage(maximumMessageLength));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class FramingException : Exception
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="FramingException" /> class.
            /// </summary>
            /// <param name="message">The message that describes the error. </param>
            public FramingException(string message) : base(message)
            {
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="FramingException"/> class.
            /// </summary>
            /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
            /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
            protected FramingException(SerializationInfo info, StreamingContext context) : base(info, context) { }
        }

        private delegate int IntDecoder(ReadOnlySpan<byte> span, int length);

        private static int BigEndianDecode(ReadOnlySpan<byte> span, int length) => length switch
        {
            1 => span[0],
            2 => BinaryPrimitives.ReadUInt16BigEndian(span),
            3 => (span[0] << 16) | BinaryPrimitives.ReadUInt16BigEndian(span.Slice(1)),
            4 => BinaryPrimitives.ReadInt32BigEndian(span),
            _ => throw new ArgumentOutOfRangeException(nameof(length), length, "Length field length must be 1, 2, 3, or 4")
        };

        private static int LittleEndianDecode(ReadOnlySpan<byte> span, int length) => length switch
        {
            1 => span[0],
            2 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            3 => span[0] | (BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(1)) << 8),
            4 => BinaryPrimitives.ReadInt32LittleEndian(span),
            _ => throw new ArgumentOutOfRangeException(nameof(length), length, "Length field length must be 1, 2, 3, or 4")
        };

        /// <summary>
        /// Appends <paramref name="second"/> to <paramref name="first"/> and returns a new
        /// <see cref="ReadOnlySequence{T}"/> by chaining the underlying segments. No data copy.
        /// </summary>
        private static ReadOnlySequence<byte> Concat(ReadOnlySequence<byte> first, ReadOnlySequence<byte> second)
            => BufferSegment.Concat(first, second);

        private sealed class SimpleFramingProtocolEncoderStage : SimpleLinearGraphStage<ReadOnlySequence<byte>>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly SimpleFramingProtocolEncoderStage _stage;

                public Logic(SimpleFramingProtocolEncoderStage stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandlers(stage.Inlet, stage.Outlet, this);
                }

                public override void OnPush()
                {
                    var message = Grab(_stage.Inlet);
                    var messageSize = message.Length;

                    if (messageSize > _stage._maximumMessageLength)
                        FailStage(new FramingException(
                            $"Maximum allowed message size is {_stage._maximumMessageLength} but tried to send {messageSize} bytes"));
                    else
                    {
                        var header = new byte[4];
                        BinaryPrimitives.WriteInt32BigEndian(header, (int)messageSize);
                        Push(_stage.Outlet, Concat(new ReadOnlySequence<byte>(header), message));
                    }

                }

                public override void OnPull() => Pull(_stage.Inlet);
            }

            #endregion

            private readonly long _maximumMessageLength;

            public SimpleFramingProtocolEncoderStage(long maximumMessageLength) : base("SimpleFramingProtocolEncoder")
            {
                _maximumMessageLength = maximumMessageLength;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class DelimiterFramingStage : SimpleLinearGraphStage<ReadOnlySequence<byte>>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly DelimiterFramingStage _stage;
                private readonly byte _firstSeparatorByte;
                private ReadOnlySequence<byte> _buffer = ReadOnlySequence<byte>.Empty;
                private int _nextPossibleMatch;

                public Logic(DelimiterFramingStage stage) : base (stage.Shape)
                {
                    _stage = stage;
                    _firstSeparatorByte = stage._separatorBytes.FirstSpan[0];

                    SetHandlers(stage.Inlet, stage.Outlet, this);
                }

                public override void OnPush()
                {
                    _buffer = Concat(_buffer, Grab(_stage.Inlet));
                    DoParse();
                }

                public override void OnUpstreamFinish()
                {
                    if (_buffer.IsEmpty)
                        CompleteStage();
                    else if (IsAvailable(_stage.Outlet))
                        DoParse();

                    // else swallow the termination and wait for pull
                }

                public override void OnPull() => DoParse();

                private void TryPull()
                {
                    if (IsClosed(_stage.Inlet))
                    {
                        if (_stage._allowTruncation)
                        {
                            Push(_stage.Outlet, _buffer);
                            CompleteStage();
                        }
                        else
                            FailStage(
                                new FramingException(
                                    "Stream finished but there was a truncated final frame in the buffer"));
                    }
                    else
                        Pull(_stage.Inlet);
                }

                /// <summary>
                /// Locates the first occurrence of <paramref name="value"/> in <paramref name="buffer"/>
                /// starting at index <paramref name="from"/>. Works on multi-segment sequences without
                /// materializing.
                /// </summary>
                private static int IndexOf(ReadOnlySequence<byte> buffer, byte value, int from)
                {
                    if (from >= buffer.Length) return -1;
                    var reader = new SequenceReader<byte>(buffer);
                    if (from > 0) reader.Advance(from);
                    return reader.TryAdvanceTo(value, advancePastDelimiter: false)
                        ? (int)reader.Consumed
                        : -1;
                }

                /// <summary>
                /// Returns whether <paramref name="buffer"/> contains <paramref name="pattern"/> at
                /// position <paramref name="offset"/>. Works on multi-segment sequences via
                /// <see cref="SequenceReader{T}.IsNext(ReadOnlySpan{T}, bool)"/>.
                /// </summary>
                private static bool HasSubstring(ReadOnlySequence<byte> buffer, ReadOnlySequence<byte> pattern, int offset)
                {
                    var patternLength = (int)pattern.Length;
                    if (offset + patternLength > buffer.Length) return false;
                    var reader = new SequenceReader<byte>(buffer);
                    if (offset > 0) reader.Advance(offset);
                    // Pattern is small (usually 1–4 bytes); compare via stack span when not single-segment.
                    if (pattern.IsSingleSegment)
                        return reader.IsNext(pattern.FirstSpan, advancePast: false);
                    Span<byte> patternBuf = patternLength <= 64 ? stackalloc byte[patternLength] : new byte[patternLength];
                    pattern.CopyTo(patternBuf);
                    return reader.IsNext(patternBuf, advancePast: false);
                }

                private void DoParse()
                {
                    while (true)
                    {
                        var possibleMatchPosition = IndexOf(_buffer, _firstSeparatorByte, _nextPossibleMatch);
                        var bufferLength = (int)_buffer.Length;
                        var separatorLength = (int)_stage._separatorBytes.Length;

                        if (possibleMatchPosition > _stage._maximumLineBytes)
                        {
                            FailStage(new FramingException($"Read {bufferLength} bytes which is more than {_stage._maximumLineBytes} without seeing a line terminator"));
                        }
                        else if (possibleMatchPosition == -1)
                        {
                            if (bufferLength > _stage._maximumLineBytes)
                                FailStage(new FramingException($"Read {bufferLength} bytes which is more than {_stage._maximumLineBytes} without seeing a line terminator"));
                            else
                            {
                                // No matching character, we need to accumulate more bytes into the buffer
                                _nextPossibleMatch = bufferLength;
                                TryPull();
                            }
                        }
                        else if (possibleMatchPosition + separatorLength > bufferLength)
                        {
                            // We have found a possible match (we found the first character of the terminator
                            // sequence) but we don't have yet enough bytes. We remember the position to
                            // retry from next time.
                            _nextPossibleMatch = possibleMatchPosition;
                            TryPull();
                        }
                        else if (HasSubstring(_buffer, _stage._separatorBytes, possibleMatchPosition))
                        {
                            // Found a match — slice without copying. Slice is a struct view over the
                            // existing segments; consumed segments fall out of scope when the slice
                            // is reassigned to _buffer.
                            var parsedFrame = _buffer.Slice(0, possibleMatchPosition);
                            _buffer = _buffer.Slice(possibleMatchPosition + separatorLength);
                            _nextPossibleMatch = 0;
                            Push(_stage.Outlet, parsedFrame);

                            if (IsClosed(_stage.Inlet) && _buffer.IsEmpty)
                                CompleteStage();
                        }
                        else
                        {
                            // possibleMatchPos was not actually a match
                            _nextPossibleMatch++;
                            continue;
                        }

                        break;
                    }
                }
            }

            #endregion

            private readonly ReadOnlySequence<byte> _separatorBytes;
            private readonly int _maximumLineBytes;
            private readonly bool _allowTruncation;

            public DelimiterFramingStage(ReadOnlySequence<byte> separatorBytes, int maximumLineBytes, bool allowTruncation) : base("DelimiterFraming")
            {
                _separatorBytes = separatorBytes;
                _maximumLineBytes = maximumLineBytes;
                _allowTruncation = allowTruncation;
            }

            protected override Attributes InitialAttributes { get; } = DefaultAttributes.DelimiterFraming;

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "DelimiterFraming";
        }

        private sealed class LengthFieldFramingStage : SimpleLinearGraphStage<ReadOnlySequence<byte>>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly LengthFieldFramingStage _stage;
                private ReadOnlySequence<byte> _buffer = ReadOnlySequence<byte>.Empty;
                private int _frameSize = int.MaxValue;

                public Logic(LengthFieldFramingStage stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandlers(stage.Inlet, stage.Outlet, this);
                }

                public override void OnPush()
                {
                    _buffer = Concat(_buffer, Grab(_stage.Inlet));
                    TryPushFrame();
                }

                public override void OnPull() => TryPushFrame();

                public override void OnUpstreamFinish()
                {
                    if (_buffer.IsEmpty)
                        CompleteStage();
                    else if (IsAvailable(_stage.Outlet))
                        TryPushFrame();

                    // else swallow the termination and wait for pull
                }

                /// <summary>
                /// push, and reset frameSize and buffer
                /// </summary>
                private void PushFrame()
                {
                    // Slice the frame and remainder without copying.
                    var emit = _buffer.Slice(0, _frameSize);
                    _buffer = _buffer.Slice(_frameSize);
                    _frameSize = int.MaxValue;
                    Push(_stage.Outlet, emit);
                    if (_buffer.IsEmpty && IsClosed(_stage.Inlet))
                        CompleteStage();
                }

                /// <summary>
                /// try to push downstream, if failed then try to pull upstream
                /// </summary>
                private void TryPushFrame()
                {
                    var bufferSize = (int)_buffer.Length;
                    if (bufferSize >= _frameSize)
                        PushFrame();
                    else if (bufferSize >= _stage._minimumChunkSize)
                    {
                        // Length field is at most 4 bytes — copy into a stack buffer to give the
                        // decoder a contiguous span regardless of the underlying segment layout.
                        Span<byte> lengthFieldSpan = stackalloc byte[_stage._lengthFieldLength];
                        _buffer.Slice(_stage._lengthFieldOffset, _stage._lengthFieldLength).CopyTo(lengthFieldSpan);
                        var parsedLength = _stage._intDecoder(lengthFieldSpan, _stage._lengthFieldLength);

                        _frameSize = _stage._computeFrameSize.HasValue
                            ? _stage._computeFrameSize.Value(_buffer.Slice(0, _stage._lengthFieldOffset).ToArray(), parsedLength)
                            : parsedLength + _stage._minimumChunkSize;

                        if (_frameSize > _stage._maximumFramelength)
                            FailStage(new FramingException(
                                $"Maximum allowed frame size is {_stage._maximumFramelength} but decoded frame header reported size {_frameSize}"));
                        else if (_stage._computeFrameSize.IsEmpty && parsedLength < 0)
                            FailStage(new FramingException(
                                $"Decoded frame header reported negative size {parsedLength}"));
                        else if (_frameSize < _stage._minimumChunkSize)
                            FailStage(new FramingException(
                                $"Computed frame size {_frameSize} is less than minimum chunk size {_stage._minimumChunkSize}"));
                        else if (bufferSize >= _frameSize)
                            PushFrame();
                        else
                            TryPull();
                    }
                    else
                        TryPull();
                }

                private void TryPull()
                {
                    if (IsClosed(_stage.Inlet))
                        FailStage(new FramingException("Stream finished but there was a truncated final frame in the buffer"));
                    else
                        Pull(_stage.Inlet);
                }
            }

            #endregion

            private readonly int _lengthFieldLength;
            private readonly int _maximumFramelength;
            private readonly int _lengthFieldOffset;
            private readonly int _minimumChunkSize;
            private readonly IntDecoder _intDecoder;
            private readonly Option<Func<IReadOnlyList<byte>, int, int>> _computeFrameSize;

            // For the sake of binary compatibility
            public LengthFieldFramingStage(int lengthFieldLength, int maximumFramelength, int lengthFieldOffset, ByteOrder byteOrder)
                : this(lengthFieldLength, maximumFramelength, lengthFieldOffset, byteOrder, Option<Func<IReadOnlyList<byte>, int, int>>.None)
            { }

            public LengthFieldFramingStage(int lengthFieldLength, int maximumFramelength, int lengthFieldOffset, ByteOrder byteOrder, Func<IReadOnlyList<byte>, int, int> computeFrameSize)
                : this(lengthFieldLength, maximumFramelength, lengthFieldOffset, byteOrder, Option<Func<IReadOnlyList<byte>, int, int>>.Create(computeFrameSize))
            { }

            public LengthFieldFramingStage(
                int lengthFieldLength,
                int maximumFrameLength,
                int lengthFieldOffset,
                ByteOrder byteOrder,
                Option<Func<IReadOnlyList<byte>, int, int>> computeFrameSize) : base("LengthFieldFramingStage")
            {
                _lengthFieldLength = lengthFieldLength;
                _maximumFramelength = maximumFrameLength;
                _lengthFieldOffset = lengthFieldOffset;
                _minimumChunkSize = lengthFieldOffset + lengthFieldLength;
                _computeFrameSize = computeFrameSize;
                _intDecoder = byteOrder == ByteOrder.BigEndian ? BigEndianDecode : LittleEndianDecode;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
    }
}
