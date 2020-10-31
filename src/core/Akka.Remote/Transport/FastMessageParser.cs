// //-----------------------------------------------------------------------
// // <copyright file="FastMessageParser.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Akka.Actor;
using Akka.Remote.Serialization;

namespace Akka.Remote.Transport
{
    internal class FastMessageParser
    {
        [Conditional("DEBUG")]
        static void log(int bytes = 0, [CallerMemberName] string cn = "")
        {
            //Console.WriteLine($"{Thread.CurrentThread.ManagedThreadId} - {cn} - {bytes} bytes");
        }
        /// <summary>
        /// Given an <see cref="ArraySegment{T}"/> that starts with a protobuf field code
        /// for a Binary (i.e. utf8string/bytestring/message) field,
        /// Returns an arraysegment corresponding to that message field, and
        /// an arraysegment representing the remainder of the protobuf message.
        /// Incorrect input may result in nonsensical output or an exception.
        /// </summary>
        /// <param name="raw">The arraysegment, starting with the field code</param>
        /// <returns>bytes of field, remainder of arraysegment.</returns>
        private static (ArraySegment<byte> first, ArraySegment<byte>
            second) SliceSegment(ArraySegment<byte> raw)
        {
            var count = raw.Count;
            var firstOffset = raw.Offset;
            var startAt = firstOffset + 1;
            var arr = raw.Array;
            //log(raw.Count);
            var nextPos = ReadRawInt32WithNewBufferPos(
                new Span<byte>(arr, startAt, count - 1));
            var readBytes = nextPos.Item1;
            startAt = nextPos.Item2 + startAt;
                    
            var nextPosition = startAt + readBytes;
            return (
                new ArraySegment<byte>(arr, startAt, readBytes),
                new ArraySegment<byte>(arr, nextPosition,
                    count+ firstOffset - nextPosition));
        }
        private static (ReadOnlyMemory<byte> first, ReadOnlyMemory<byte>
            second) SliceSegmentMemory(ArraySegment<byte> raw)
        {
            var count = raw.Count;
            var firstOffset = raw.Offset;
            var startAt = firstOffset + 1;
            var arr = raw.Array;
            //log(raw.Count);
            var nextPos = ReadRawInt32WithNewBufferPos(
                new Span<byte>(arr, startAt, count - 1));
            var readBytes = (int)nextPos.Item1;
            startAt = nextPos.Item2 + startAt;
                    
            var nextPosition = startAt + readBytes;
            return (
                new ReadOnlyMemory<byte>(arr, startAt, readBytes),
                new ReadOnlyMemory<byte>(arr, nextPosition,
                    count+ firstOffset - nextPosition));
        }
        private static (ReadOnlyMemory<byte> first, ReadOnlyMemory<byte>
            second) SliceSegmentMemory(ReadOnlyMemory<byte> raw)
        {
            var count = raw.Length;
            var startAt = 1;
            var arr = raw.Span;
            //log(raw.Count);
            var nextPos = ReadRawInt32WithNewBufferPos(
                arr.Slice(startAt, count - 1));
            var readBytes = nextPos.Item1;
            startAt = nextPos.Item2 + startAt;
                    
            var nextPosition = startAt + readBytes;
            return (
                raw.Slice( startAt, readBytes),
                raw.Slice( nextPosition,
                    count- nextPosition));
        }
        /// <summary>
        /// Reads a Int32 (-not- SInt32) from the buffer, returning the value
        /// as well as the number of bytes advanced.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static (int, int) ReadRawInt32WithNewBufferPos(ReadOnlySpan<byte> buffer)
        {
            //log(buffer.Length);
            int bufferPos = 0;

            int result = buffer[bufferPos++];
            if (result < 128)
            {
                return (result, bufferPos);
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = buffer[bufferPos++];
                result |= (b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return (result, bufferPos);
                }
                shift += 7;
            }
            while (shift < 64);

            throw new Exception();
        }
        /// <summary>
        /// Reads a Raw Unsigned Int64 from a 128, returning both the unsigned int64,
        /// as well as the number of bytes to advance the reader (i.e. if parsing the rest of a message) 
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        /// <exception cref="ProtoParseException"></exception>
        public static (ulong,int) ReadRawInt64WithNewBufferPos(ReadOnlySpan<byte> buffer)
        {
            //log(buffer.Length);
            int bufferPos = 0;

            ulong result = buffer[bufferPos++];
            if (result < 128)
            {
                return (result, bufferPos);
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = buffer[bufferPos++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return (result, bufferPos);
                }
                shift += 7;
            }
            while (shift < 64);

            throw new ProtoParseException();
        }
        //message AckAndEnvelopeContainer {
        //AcknowledgementInfo ack = 1;
        //RemoteEnvelope envelope = 2;
        //}
        //message AcknowledgementInfo {
        //    fixed64 cumulativeAck = 1;
        //    repeated fixed64 nacks = 2;
        //}
        //message RemoteEnvelope {
        //ActorRefData recipient = 1;
        //Payload message = 2;
        //ActorRefData sender = 4;
        //fixed64 seq = 5;
        //}
        //actorefData - Path - 01
        //fixed64 cumulativeAck = 1;
        //repeated fixed64 nacks = 2;
        //
        //message Payload {
        //bytes message = 1;
        //int32 serializerId = 2;
        //bytes messageManifest = 3;
        //}
        //
        //
        /// <summary>
        /// Represents a sliced series of bits corresponding to an
        /// Akka Protobuf Payload.
        /// </summary>
        public class PayloadParser //: IEquatable<PayloadParser>
        {
            //TODO: Be smarter about this.
            //We could probably instead get away with:
            //   - ReadOnlyMemory<byte> of payload
            //   - Int of separator between manifest and payload
            //   - int of Serializer ID
            //   - methods that just work off that.
            private readonly ArraySegment<byte> _manifestBytes;
            private readonly ReadOnlyMemory<byte> _payloadMessageBytes;
            private readonly int _serId;
            private readonly bool _manifestSet;
            /// <summary>
            /// Gets the Message Bytes as a <see cref="ReadOnlySpan{T}"/>
            /// </summary>
            /// <returns></returns>
            public ReadOnlySpan<byte> GetMessageByteSpan()
            {
                return _payloadMessageBytes.Span;
            }
            /// <summary>
            /// Gets the Serializer ID.
            /// </summary>
            public int SerId
            {
                get => _serId;
            }
            /// <summary>
            /// If true, has a manifest.
            /// </summary>
            public bool HasManifest
            {
                get => _manifestSet;
            }

            /// <summary>
            /// Gets the Arraysegment corresponding to the manifest bytes.
            /// </summary>
            /// <returns></returns>
            public ReadOnlyMemory<byte> Manifest()
            {
                return _manifestBytes;
            }
            /// <summary>
            /// Gets the manifest as a string from the arraysegment.
            /// </summary>
            /// <returns></returns>
            public string ManifestString()
            {
                
                return Encoding.UTF8.GetString( _manifestBytes.Array, _manifestBytes.Offset+2,_manifestBytes.Count);
            }
            /// <summary>
            /// returns a Span corresponding to the manifest's UTF8 Bytes.
            /// </summary>
            /// <returns></returns>
            public ReadOnlySpan<byte> GetManifestSpan()
            {
                return new ReadOnlySpan<byte>(_manifestBytes.Array,_manifestBytes.Offset+2,_manifestBytes.Count);
            }
                    
            public PayloadParser(ReadOnlyMemory<byte> raw)
            {
                var array = raw.ToArray();
                var _messageAndRest = SliceSegment(new ArraySegment<byte>(array));
                _payloadMessageBytes = _messageAndRest.first;
                var serIdAndBufferPos = ReadRawInt32WithNewBufferPos(
                    new Span<byte>(_messageAndRest.second.Array,
                        _messageAndRest.second.Offset+1,
                        _messageAndRest.second.Count-1));
                _serId = serIdAndBufferPos.Item1;
                var nextItem = serIdAndBufferPos.Item2;
                var second = _messageAndRest.second;
                if (nextItem + 1 >=
                    second.Count)
                {
                    _manifestSet = false;
                    _manifestBytes = default;
                    //Console.WriteLine("hyh");
                }
                else
                {
                    _manifestSet = true;
                    var nextVarInt = ReadRawInt32WithNewBufferPos(
                        second.AsSpan(nextItem + 2));
                        //new Span<byte>(second.Array,
                        //    second.Offset + nextItem + 2,
                        //    second.Count - (nextItem + 2)));
                    var nextSegment =
                        new ArraySegment<byte>(
                            second.Array,
                            second.Offset + 1 +
                            nextVarInt.Item2, (int)nextVarInt.Item1);
                    _manifestBytes = nextSegment;
                }
            }

                    
        }
        /// <summary>
        /// 
        /// </summary>
        /// <remarks>
        /// We keep this as a struct because this is only used
        /// inside one method for a few lines
        /// </remarks>
        public struct EnvelopeContainerParser
        {
                
            private readonly ReadOnlyMemory<byte> _rec;
            private readonly  ReadOnlyMemory<byte> _msg;
            private readonly ReadOnlyMemory<byte> _sender;
            private readonly long _seq;
            private readonly  bool _hasSender;

            /// <summary>
            /// Pulls out the PayloadParser from this Envelope.
            /// </summary>
            /// <returns></returns>
            public PayloadParser GetPayloadParser()
            {
                return new PayloadParser(_msg);
            }
            /// <summary>
            /// Gets the Sender as a series of UTF8 bytes.
            /// </summary>
            /// <returns></returns>
            public ReadOnlySpan<byte> SenderUtf8Bytes()
            {
                return _sender.Span;
            }
            /// <summary>
            /// Gets the Sender's UTF8 string as an Arraysegment.
            /// </summary>
            /// <returns></returns>
            public ReadOnlyMemory<byte> SenderSegment()
            {
                return _sender;
            }
            /// <summary>
            /// Envelope has a Sender.
            /// </summary>
            public bool HasSender
            {
                get => _hasSender;
            }

            public ReadOnlySpan<byte> ReceiverUtf8Bytes()
            {
                return _rec.Span;
            }

            public ReadOnlyMemory<byte> ReceiverSegment()
            {
                return _rec;
            }

            public ReadOnlyMemory<byte> MessageArraySegment()
            {
                return _msg;
            }

            public long GetSequenceNumber()
            {
                return _seq;
            }

            public EnvelopeContainerParser(ArraySegment<byte> raw)
            {
                //log(raw.Count);
                //_raw = raw;
                var recAndRest = SliceSegment(raw);
                _rec = SliceSegment(recAndRest.first).first;
                var msgAndRest = SliceSegment(recAndRest.second);
                _msg = msgAndRest.first;
                var second = msgAndRest.second;
                if (second.Array[second.Offset] ==
                    (4 << 3 | 2))
                {
                    //have sender.
                    _hasSender = true;
                    var senderAndRest = SliceSegment(second);
                    _sender = SliceSegment(senderAndRest.first).first;
                    _seq = (long)BinaryPrimitives.ReadUInt64LittleEndian(
                        new ReadOnlySpan<byte>(senderAndRest.second.Array,
                            senderAndRest.second.Offset + 1,
                            senderAndRest.second.Count - 1));
                }
                else
                {
                    _hasSender = false;
                    _sender = default;
                    _seq = (long)BinaryPrimitives.ReadUInt64LittleEndian(
                        new ReadOnlySpan<byte>(second.Array,
                            second.Offset + 1, second.Count - 1));
                }
            }

                
        }
        public class AckAndMessageParser
        {
            
            public AckAndMessageAS BuildAckAndMessage(IRemoteActorRefProvider rarp, Address localAddress,AddressThreadLocalCache act)
            {
                var ack = _ackSet ? BuildAck() : null;

                MessageAS msg = null;
                if (_msgSet)
                {
                    var parse = GetEnvelopeContainerParser();
                    var innerParse = parse.GetPayloadParser();
                    var addy = rarp.ResolveActorRefWithLocalAddress(parse.ReceiverSegment(),
                        localAddress);
                    Address recipientAddress;
                    if (act != null)
                    {
                        var rs = parse.ReceiverSegment();
                        recipientAddress =
                            act.CacheAS.GetOrCompute( rs.Span);
                    }
                    else
                    {
                        var rc = parse.ReceiverSegment();
                        ActorPath.TryParseAddress(Encoding.UTF8.GetString(rc.ToArray()),
                            out recipientAddress);
                    }

                    IInternalActorRef sendre = null;
                    if (parse.HasSender)
                    {
                        sendre =
                            rarp.ResolveActorRefWithLocalAddress(parse.SenderSegment(), localAddress);
                    }

                    SeqNo ackOption = null;
                    if ((ulong)parse.GetSequenceNumber() != AkkaPduProtobuffCodec.SeqUndefined)
                    {
                        ackOption = parse.GetSequenceNumber();
                    }

                    msg = new MessageAS(addy, recipientAddress,innerParse, sendre, ackOption);
                }
                //var msg = _msgSet ? new Message(,,new Payload(innerParse.GetMessageByteSpan())) : null;
                return new AckAndMessageAS(ack, msg);
            }
            
            public EnvelopeContainerParser GetEnvelopeContainerParser()
            {
                return new EnvelopeContainerParser(_msgBytes);
            }
            
            internal ArraySegment<byte> _ackBytes;
            private ArraySegment<byte> _msgBytes;
            private bool _ackSet;
            //private ArraySegment<byte> _rawBytes;
            private bool _msgSet;

            public AckAndMessageParser(ArraySegment<byte> raw)
            {
                //  _rawBytes = raw;
                if (raw.Count < 256 && raw.Array.Length > 1024)
                {
                    //Console.WriteLine("copy");
                    var array = new byte[raw.Count];
                    Array.Copy(raw.Array,raw.Offset,array,0,raw.Count);
                  
                    raw =new ArraySegment<byte>(array); 
                }
                var firstByte = raw.Array[raw.Offset];
                if (firstByte == (01 << 3 | 2))
                {
                    var remaining = GetAndSetAckSection(raw);
                    if (remaining.Count > 0)
                    {
                        GetAndSetMsgSection(remaining);
                    }
                }
                else
                {
                    var remaining = GetAndSetMsgSection(raw);
                    if (remaining.Count > 0)
                    {
                        GetAndSetAckSection(remaining);
                    }
                }
            }

            public Ack BuildAck()
            {
                if (_ackSet)
                {
                    var seq = new SeqNo((long)BinaryPrimitives.ReadUInt64LittleEndian(
                        new ReadOnlySpan<byte>(_ackBytes.Array,
                            _ackBytes.Offset + 1, 8)));
                    var nacks = new SeqNo[0];
                    if (_ackBytes.Count - 10 > 0)
                    {
                        var nextLength = ReadRawInt32WithNewBufferPos(
                            new Span<byte>(_ackBytes.Array,
                                _ackBytes.Offset + 10, _ackBytes.Count - 10));
                        var numEntries = nextLength.Item1;
                        var unAcks = new Span<byte>(_ackBytes.Array,
                            _ackBytes.Offset + nextLength.Item2, numEntries * 8);
                        var acks= MemoryMarshal.Cast<byte, long>(unAcks);
                        nacks = new SeqNo[acks.Length];
                        for (int i = 0; i < acks.Length; i++)
                        {
                            nacks[i] = acks[i];
                        }    
                    }
                    
                    return  new Ack(seq,nacks);
                }

                return null;
            }

            /// <summary>
            /// Reads the message section from the segment given,
            /// Sets it in the field,
            /// Returns the rest of the segment after the message.
            /// </summary>
            /// <param name="raw"></param>
            /// <returns></returns>
            private ArraySegment<byte> GetAndSetMsgSection(ArraySegment<byte> raw)
            {
                _msgSet = true;
                var nextPos = ReadRawInt32WithNewBufferPos(
                    new Span<byte>(raw.Array, raw.Offset + 1, raw.Count - 1));
                var readBytes = (int)nextPos.Item1;
                var startAt = nextPos.Item2 + raw.Offset + 1;
                _msgBytes = new ArraySegment<byte>(raw.Array, startAt, readBytes);
                var nextPosition = startAt + readBytes;
                return new ArraySegment<byte>(raw.Array,nextPosition,raw.Count+raw.Offset-nextPosition);
            }

            /// <summary>
            /// Reads the Ack section of the message and sets the field,
            /// returning the rest of the payload.
            /// </summary>
            /// <param name="raw"></param>
            /// <returns></returns>
            private ArraySegment<byte> GetAndSetAckSection(ArraySegment<byte> raw)
            {
                _ackSet = true;
                var nextPos = ReadRawInt32WithNewBufferPos(
                    new Span<byte>(raw.Array, raw.Offset + 1, raw.Count - 1));
                var readBytes = nextPos.Item1;
                var startAt = nextPos.Item2 + raw.Offset + 1;
                _ackBytes = new ArraySegment<byte>(raw.Array, startAt, readBytes);
                var nextPosition = startAt + readBytes;
                return new ArraySegment<byte>(raw.Array, nextPosition,
                    raw.Count + raw.Offset - nextPosition);
            }
        }
    }
}