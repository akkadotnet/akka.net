//-----------------------------------------------------------------------
// <copyright file="Framing.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.IO;
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
        public static Flow<ByteString, ByteString, NotUsed> Delimiter(ByteString delimiter, int maximumFrameLength,
            bool allowTruncation = false)
        {
            return Flow.Create<ByteString>()
                .Via(new DelimiterFramingStage(delimiter, maximumFrameLength, allowTruncation))
                .Named("DelimiterFraming");
        }

        /// <summary>
        /// Creates a Flow that decodes an incoming stream of unstructured byte chunks into a stream of frames, assuming that
        /// incoming frames have a field that encodes their length.
        /// 
        /// If the input stream finishes before the last frame has been fully decoded this Flow will fail the stream reporting
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
        public static Flow<ByteString, ByteString, NotUsed> LengthField(int fieldLength, int maximumFramelength,
            int fieldOffset = 0, ByteOrder byteOrder = ByteOrder.LittleEndian)
        {
            if (fieldLength < 1 || fieldLength > 4)
                throw new ArgumentException("Length field length must be 1,2,3 or 4", nameof(fieldLength));

            return Flow.Create<ByteString>()
                .Via(new LengthFieldFramingStage(fieldLength, maximumFramelength, fieldOffset, byteOrder))
                .Named("LengthFieldFraming");
        }

        /// <summary>
        /// Returns a BidiFlow that implements a simple framing protocol. This is a convenience wrapper over <see cref="LengthField"/>
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
        public static BidiFlow<ByteString, ByteString, ByteString, ByteString, NotUsed> SimpleFramingProtocol(int maximumMessageLength)
        {
            return BidiFlow.FromFlowsMat(SimpleFramingProtocolEncoder(maximumMessageLength),
                SimpleFramingProtocolDecoder(maximumMessageLength), Keep.Left);
        }

        /// <summary>
        /// Protocol decoder that is used by <see cref="SimpleFramingProtocol"/>
        /// </summary>
        /// <param name="maximumMessageLength">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<ByteString, ByteString, NotUsed> SimpleFramingProtocolDecoder(int maximumMessageLength)
        {
            return LengthField(4, maximumMessageLength + 4, 0, ByteOrder.BigEndian).Select(b => b.Slice(4));
        }

        /// <summary>
        /// Protocol encoder that is used by <see cref="SimpleFramingProtocol"/>
        /// </summary>
        /// <param name="maximumMessageLength">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<ByteString, ByteString, NotUsed> SimpleFramingProtocolEncoder(int maximumMessageLength)
        {
            return Flow.Create<ByteString>().Via(new SimpleFramingProtocolEncoderStage(maximumMessageLength));
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
        }

        private static readonly Func<IEnumerator<byte>, int, int> BigEndianDecoder = (enumerator, length) =>
        {
            var count = length;
            var decoded = 0;
            while (count > 0)
            {
                decoded <<= 8;
                if (!enumerator.MoveNext()) throw new IndexOutOfRangeException("LittleEndianDecoder reached end of byte string");
                decoded |= enumerator.Current & 0xFF;
                count--;
            }

            return decoded;
        };

        private static readonly Func<IEnumerator<byte>, int, int> LittleEndianDecoder = (enumerator, length) =>
        {
            var highestOcted = (length - 1) << 3;
            var mask = (int) (1L << (length << 3)) - 1;
            var count = length;
            var decoded = 0;

            while (count > 0)
            {
                // decoded >>>= 8 on the jvm
                decoded = (int) ((uint) decoded >> 8);
                if (!enumerator.MoveNext()) throw new IndexOutOfRangeException("LittleEndianDecoder reached end of byte string");
                decoded += (enumerator.Current & 0xFF) << highestOcted;
                count--;
            }

            return decoded & mask;
        };

        private sealed class SimpleFramingProtocolEncoderStage : SimpleLinearGraphStage<ByteString>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly SimpleFramingProtocolEncoderStage _stage;

                public Logic(SimpleFramingProtocolEncoderStage stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Inlet, stage.Outlet, this);
                }

                public override void OnPush()
                {
                    var message = Grab(_stage.Inlet);
                    var messageSize = message.Count;

                    if (messageSize > _stage._maximumMessageLength)
                        FailStage(new FramingException(
                            $"Maximum allowed message size is {_stage._maximumMessageLength} but tried to send {messageSize} bytes"));
                    else
                    {
                        var header = ByteString.CopyFrom(new[]
                        {
                            Convert.ToByte((messageSize >> 24) & 0xFF),
                            Convert.ToByte((messageSize >> 16) & 0xFF),
                            Convert.ToByte((messageSize >> 8) & 0xFF),
                            Convert.ToByte(messageSize & 0xFF)
                        });
                        Push(_stage.Outlet, header + message);
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

        private sealed class DelimiterFramingStage : SimpleLinearGraphStage<ByteString>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly DelimiterFramingStage _stage;
                private readonly byte _firstSeparatorByte;
                private ByteString _buffer = ByteString.Empty;
                private int _nextPossibleMatch;

                public Logic(DelimiterFramingStage stage) : base (stage.Shape)
                {
                    _stage = stage;
                    _firstSeparatorByte = stage._separatorBytes[0];

                    SetHandler(stage.Inlet, stage.Outlet, this);
                }

                public override void OnPush()
                {
                    _buffer += Grab(_stage.Inlet);
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

                private void DoParse()
                {
                    while (true)
                    {
                        var possibleMatchPosition = _buffer.IndexOf(_firstSeparatorByte, from: _nextPossibleMatch);

                        if (possibleMatchPosition > _stage._maximumLineBytes)
                        {
                            FailStage(new FramingException($"Read {_buffer.Count} bytes which is more than {_stage._maximumLineBytes} without seeing a line terminator"));
                        }
                        else if (possibleMatchPosition == -1)
                        {
                            if (_buffer.Count > _stage._maximumLineBytes)
                                FailStage(new FramingException($"Read {_buffer.Count} bytes which is more than {_stage._maximumLineBytes} without seeing a line terminator"));
                            else
                            {
                                // No matching character, we need to accumulate more bytes into the buffer 
                                _nextPossibleMatch = _buffer.Count;
                                TryPull();
                            }
                        }
                        else if (possibleMatchPosition + _stage._separatorBytes.Count > _buffer.Count)
                        {
                            // We have found a possible match (we found the first character of the terminator 
                            // sequence) but we don't have yet enough bytes. We remember the position to 
                            // retry from next time.
                            _nextPossibleMatch = possibleMatchPosition;
                            TryPull();
                        }
                        else if (_buffer.HasSubstring(_stage._separatorBytes, possibleMatchPosition))
                        {
                            // Found a match
                            var parsedFrame = _buffer.Slice(0, possibleMatchPosition).Compact();
                            _buffer = _buffer.Slice(possibleMatchPosition + _stage._separatorBytes.Count).Compact();
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

            private readonly ByteString _separatorBytes;
            private readonly int _maximumLineBytes;
            private readonly bool _allowTruncation;

            public DelimiterFramingStage(ByteString separatorBytes, int maximumLineBytes, bool allowTruncation) : base("DelimiterFraming")
            {
                _separatorBytes = separatorBytes;
                _maximumLineBytes = maximumLineBytes;
                _allowTruncation = allowTruncation;
            }

            protected override Attributes InitialAttributes { get; } = DefaultAttributes.DelimiterFraming;

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "DelimiterFraming";
        }

        private sealed class LengthFieldFramingStage : SimpleLinearGraphStage<ByteString>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly LengthFieldFramingStage _stage;
                private ByteString _buffer = ByteString.Empty;
                private int _frameSize = int.MaxValue;

                public Logic(LengthFieldFramingStage stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Inlet, stage.Outlet, this);
                }

                public override void OnPush()
                {
                    _buffer += Grab(_stage.Inlet);
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
                    var emit = _buffer.Slice(0, _frameSize).Compact();
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
                    var bufferSize = _buffer.Count;
                    if (bufferSize >= _frameSize)
                        PushFrame();
                    else if (bufferSize >= _stage._minimumChunkSize)
                    {
                        var iterator = _buffer.Slice(_stage._lengthFieldOffset).GetEnumerator();
                        var parsedLength = _stage._intDecoder(iterator, _stage._lengthFieldLength);
                        _frameSize = parsedLength + _stage._minimumChunkSize;

                        if (_frameSize > _stage._maximumFramelength)
                            FailStage(new FramingException(
                                $"Maximum allowed frame size is {_stage._maximumFramelength} but decoded frame header reported size {_frameSize}"));
                        else if (parsedLength < 0)
                            FailStage(new FramingException(
                                $"Decoded frame header reported negative size {parsedLength}"));
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
            private readonly Func<IEnumerator<byte>, int, int> _intDecoder;

            public LengthFieldFramingStage(int lengthFieldLength, int maximumFramelength, int lengthFieldOffset, ByteOrder byteOrder) : base("LengthFieldFramingStage")
            {
                _lengthFieldLength = lengthFieldLength;
                _maximumFramelength = maximumFramelength;
                _lengthFieldOffset = lengthFieldOffset;
                _minimumChunkSize = lengthFieldOffset + lengthFieldLength;
                _intDecoder = byteOrder == ByteOrder.BigEndian ? BigEndianDecoder : LittleEndianDecoder;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
    }
}
