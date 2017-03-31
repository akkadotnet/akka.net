//-----------------------------------------------------------------------
// <copyright file="Framing.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.IO;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util;

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
                .Transform(() => new LengthFieldFramingStage(fieldLength, maximumFramelength, fieldOffset, byteOrder))
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
            return LengthField(4, maximumMessageLength + 4, 0, ByteOrder.BigEndian).Select(b => b.Drop(4));
        }

        /// <summary>
        /// Protocol encoder that is used by <see cref="SimpleFramingProtocol"/>
        /// </summary>
        /// <param name="maximumMessageLength">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<ByteString, ByteString, NotUsed> SimpleFramingProtocolEncoder(int maximumMessageLength)
        {
            return Flow.Create<ByteString>().Transform(() => new FramingDecoderStage(maximumMessageLength));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public class FramingException : Exception
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="message">TBD</param>
            public FramingException(string message) : base(message)
            {
            }
        }

        private static readonly Func<ByteIterator, int, int> BigEndianDecoder = (byteString, length) =>
        {
            var count = length;
            var decoded = 0;
            while (count > 0)
            {
                decoded <<= 8;
                decoded |= byteString.Next() & 0xFF;
                count--;
            }

            return decoded;
        };

        private static readonly Func<ByteIterator, int, int> LittleEndianDecoder = (byteString, length) =>
        {
            var highestOcted = (length - 1) << 3;
            var mask = (int) (1L << (length << 3)) - 1;
            var count = length;
            var decoded = 0;

            while (count > 0)
            {
                // decoded >>>= 8 on the jvm
                decoded = (int) ((uint) decoded >> 8);
                decoded += (byteString.Next() & 0xFF) << highestOcted;
                count--;
            }

            return decoded & mask;
        };

        private sealed class FramingDecoderStage : PushStage<ByteString, ByteString>
        {
            private readonly int _maximumMessageLength;

            public FramingDecoderStage(int maximumMessageLength)
            {
                _maximumMessageLength = maximumMessageLength;
            }

            public override ISyncDirective OnPush(ByteString element, IContext<ByteString> context)
            {
                var messageSize = element.Count;
                if (messageSize > _maximumMessageLength)
                    return context.Fail(new FramingException($"Maximum allowed message size is {_maximumMessageLength} but tried to send {messageSize} bytes"));

                var header = ByteString.Create(new[]
                {
                    Convert.ToByte((messageSize >> 24) & 0xFF),
                    Convert.ToByte((messageSize >> 16) & 0xFF),
                    Convert.ToByte((messageSize >> 8) & 0xFF),
                    Convert.ToByte(messageSize & 0xFF)
                });
                return context.Push(header + element);
            }
        }

        private sealed class DelimiterFramingStage : GraphStage<FlowShape<ByteString, ByteString>>
        {
            #region Logic

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly DelimiterFramingStage _stage;
                private readonly byte _firstSeparatorByte;
                private ByteString _buffer;
                private int _nextPossibleMatch;

                public Logic(DelimiterFramingStage stage) : base (stage.Shape)
                {
                    _stage = stage;
                    _firstSeparatorByte = stage._separatorBytes.Head;
                    _buffer = ByteString.Empty;

                    SetHandler(stage.In, this);
                    SetHandler(stage.Out, this);
                }

                public override void OnPush()
                {
                    _buffer += Grab(_stage.In);
                    DoParse();
                }

                public override void OnUpstreamFinish()
                {
                    if (_buffer.IsEmpty)
                        CompleteStage();
                    else if (IsAvailable(_stage.Out))
                        DoParse();

                    // else swallow the termination and wait for pull 
                }

                public override void OnPull() => DoParse();

                private void TryPull()
                {
                    if (IsClosed(_stage.In))
                    {
                        if (_stage._allowTruncation)
                        {
                            Push(_stage.Out, _buffer);
                            CompleteStage();
                        }
                        else
                            FailStage(
                                new FramingException(
                                    "Stream finished but there was a truncated final frame in the buffer"));
                    }
                    else
                        Pull(_stage.In);
                }

                private void DoParse()
                {
                    var possibleMatchPosition = -1;

                    for (var i = _nextPossibleMatch; i < _buffer.Count; i++)
                    {
                        if (_buffer[i] == _firstSeparatorByte)
                        {
                            possibleMatchPosition = i;
                            break;
                        }
                    }

                    if (possibleMatchPosition > _stage._maximumLineBytes)
                        FailStage(
                            new FramingException(
                                $"Read {_buffer.Count} bytes which is more than {_stage._maximumLineBytes} without seeing a line terminator"));
                    else if (possibleMatchPosition == -1)
                    {
                        if (_buffer.Count > _stage._maximumLineBytes)
                            FailStage(
                                new FramingException(
                                    $"Read {_buffer.Count} bytes which is more than {_stage._maximumLineBytes} without seeing a line terminator"));
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
                    else if (Equals(_buffer.Slice(possibleMatchPosition, possibleMatchPosition + _stage._separatorBytes.Count), _stage._separatorBytes))
                    {
                        // Found a match
                        var parsedFrame = _buffer.Slice(0, possibleMatchPosition).Compact();
                        _buffer = _buffer.Drop(possibleMatchPosition + _stage._separatorBytes.Count).Compact();
                        _nextPossibleMatch = 0;
                        Push(_stage.Out, parsedFrame);

                        if (IsClosed(_stage.In) && _buffer.IsEmpty)
                            CompleteStage();
                    }
                    else
                    {
                        // possibleMatchPos was not actually a match 
                        _nextPossibleMatch++;
                        DoParse();
                    }
                }
            }

            #endregion

            private readonly ByteString _separatorBytes;
            private readonly int _maximumLineBytes;
            private readonly bool _allowTruncation;

            public DelimiterFramingStage(ByteString separatorBytes, int maximumLineBytes, bool allowTruncation)
            {
                _separatorBytes = separatorBytes;
                _maximumLineBytes = maximumLineBytes;
                _allowTruncation = allowTruncation;

                Shape = new FlowShape<ByteString, ByteString>(In, Out);
            }
            
            private Inlet<ByteString> In = new Inlet<ByteString>("DelimiterFraming.in");

            private Outlet<ByteString> Out = new Outlet<ByteString>("DelimiterFraming.in");

            public override FlowShape<ByteString, ByteString> Shape { get; }

            protected override Attributes InitialAttributes { get; } = DefaultAttributes.DelimiterFraming;

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            public override string ToString() => "DelimiterFraming";
        }

        private sealed class LengthFieldFramingStage : PushPullStage<ByteString, ByteString>
        {
            private readonly int _lengthFieldLength;
            private readonly int _maximumFramelength;
            private readonly int _lengthFieldOffset;
            private ByteString _buffer = ByteString.Empty;
            private readonly int _minimumChunkSize;
            private int _frameSize;
            private readonly Func<ByteIterator, int, int> _intDecoder; 

            public LengthFieldFramingStage(int lengthFieldLength, int maximumFramelength, int lengthFieldOffset, ByteOrder byteOrder)
            {
                _lengthFieldLength = lengthFieldLength;
                _maximumFramelength = maximumFramelength;
                _lengthFieldOffset = lengthFieldOffset;
                _minimumChunkSize = lengthFieldOffset + lengthFieldLength;
                _frameSize = int.MaxValue;
                _intDecoder = byteOrder == ByteOrder.BigEndian ? BigEndianDecoder : LittleEndianDecoder;
            }

            public override ISyncDirective OnPush(ByteString element, IContext<ByteString> context)
            {
                _buffer += element;
                return DoParse(context);
            }

            public override ISyncDirective OnPull(IContext<ByteString> context) => DoParse(context);

            public override ITerminationDirective OnUpstreamFinish(IContext<ByteString> context)
            {
                return _buffer.NonEmpty ? context.AbsorbTermination() : context.Finish();
            }

            public override void PostStop() => _buffer = null;

            private ISyncDirective TryPull(IContext<ByteString> context)
            {
                if (context.IsFinishing)
                    return context.Fail(new FramingException("Stream finished but there was a truncated final frame in the buffer"));

                return context.Pull();
            }

            private ISyncDirective DoParse(IContext<ByteString> context)
            {
                Func<IContext<ByteString>, ISyncDirective> emitFrame = ctx =>
                {
                    var parsedFrame = _buffer.Take(_frameSize).Compact();
                    _buffer = _buffer.Drop(_frameSize);
                    _frameSize = int.MaxValue;
                    if (ctx.IsFinishing && _buffer.IsEmpty)
                        return ctx.PushAndFinish(parsedFrame);
                    return ctx.Push(parsedFrame);
                };

                var bufferSize = _buffer.Count;
                if (bufferSize >= _frameSize)
                    return emitFrame(context);

                if (bufferSize >= _minimumChunkSize)
                {
                    var parsedLength = _intDecoder(_buffer.Iterator().Drop(_lengthFieldOffset), _lengthFieldLength);
                    _frameSize = parsedLength + _minimumChunkSize;

                    if (_frameSize > _maximumFramelength)
                        return context.Fail(new FramingException($"Maximum allowed frame size is {_maximumFramelength} but decoded frame header reported size {_frameSize}"));
                    if (bufferSize >= _frameSize)
                        return emitFrame(context);

                    return TryPull(context);
                }

                return TryPull(context);
            }
        }
    }
}
