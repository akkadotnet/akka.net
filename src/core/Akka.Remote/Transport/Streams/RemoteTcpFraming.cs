//-----------------------------------------------------------------------
// <copyright file="RemoteTcpFraming.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Buffers.Binary;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using DotNettyByteOrder = DotNetty.Buffers.ByteOrder;

namespace Akka.Remote.Transport.Streams
{
    /// <summary>
    /// INTERNAL API.
    /// DotNetty-compatible Remote TCP length framing for the stream transport.
    /// Frames are encoded as a 4-byte payload length followed by payload bytes.
    /// </summary>
    internal static class RemoteTcpFraming
    {
        private const int FrameHeaderBytes = 4;

        public static Flow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> Decoder(
            int maxFrameSize,
            DotNettyByteOrder byteOrder)
        {
            return Flow.Create<ReadOnlySequence<byte>>()
                .Via(new RemoteTcpFrameDecoder(maxFrameSize, byteOrder))
                .Named("RemoteTcpFrameDecoder");
        }

        public static ReadOnlySequence<byte> Encode(
            ReadOnlySequence<byte> payload,
            int maxFrameSize,
            DotNettyByteOrder byteOrder)
        {
            if (payload.Length > maxFrameSize)
                throw new Framing.FramingException($"Remote frame size [{payload.Length}] exceeds maximum frame size [{maxFrameSize}]");

            var header = new byte[FrameHeaderBytes];
            if (byteOrder == DotNettyByteOrder.LittleEndian)
                BinaryPrimitives.WriteInt32LittleEndian(header, (int)payload.Length);
            else
                BinaryPrimitives.WriteInt32BigEndian(header, (int)payload.Length);

            return PrependHeader(header, payload);
        }

        private static ReadOnlySequence<byte> Concat(ReadOnlySequence<byte> first, ReadOnlySequence<byte> second)
        {
            if (first.IsEmpty) return second;
            if (second.IsEmpty) return first;

            SequenceSegment? head = null;
            SequenceSegment? tail = null;

            foreach (var memory in first)
            {
                if (head is null)
                {
                    head = new SequenceSegment(memory);
                    tail = head;
                }
                else
                {
                    tail = tail!.Append(memory);
                }
            }

            foreach (var memory in second)
                tail = tail!.Append(memory);

            return new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);
        }

        private static ReadOnlySequence<byte> PrependHeader(byte[] header, ReadOnlySequence<byte> payload)
        {
            if (payload.IsEmpty)
                return new ReadOnlySequence<byte>(header);

            var head = new SequenceSegment(header);
            var tail = head;
            foreach (var memory in payload)
                tail = tail.Append(memory);

            return new ReadOnlySequence<byte>(head, 0, tail, tail.Memory.Length);
        }

        private sealed class RemoteTcpFrameDecoder : GraphStage<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>>
        {
            private readonly int _maxFrameSize;
            private readonly DotNettyByteOrder _byteOrder;
            private readonly Inlet<ReadOnlySequence<byte>> _in = new("RemoteTcpFrameDecoder.in");
            private readonly Outlet<ReadOnlySequence<byte>> _out = new("RemoteTcpFrameDecoder.out");

            public RemoteTcpFrameDecoder(int maxFrameSize, DotNettyByteOrder byteOrder)
            {
                _maxFrameSize = maxFrameSize;
                _byteOrder = byteOrder;
                Shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(_in, _out);
            }

            public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; }

            protected override Attributes InitialAttributes { get; } = Attributes.CreateName("RemoteTcpFrameDecoder");

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new Logic(this);
            }

            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly RemoteTcpFrameDecoder _stage;
                private ReadOnlySequence<byte> _buffer = ReadOnlySequence<byte>.Empty;
                private int? _payloadSize;

                public Logic(RemoteTcpFrameDecoder stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandlers(stage._in, stage._out, this);
                }

                public override void OnPush()
                {
                    _buffer = Concat(_buffer, Grab(_stage._in));
                    TryPushFrame();
                }

                public override void OnPull()
                {
                    TryPushFrame();
                }

                public override void OnUpstreamFinish()
                {
                    if (_buffer.IsEmpty)
                        CompleteStage();
                    else if (IsAvailable(_stage._out))
                        TryPushFrame();
                }

                private void TryPushFrame()
                {
                    if (_payloadSize is null)
                    {
                        if (_buffer.Length < FrameHeaderBytes)
                        {
                            TryPull();
                            return;
                        }

                        var firstSpan = _buffer.FirstSpan;
                        var payloadSize = firstSpan.Length >= FrameHeaderBytes
                            ? DecodeFrameSize(firstSpan.Slice(0, FrameHeaderBytes), _stage._byteOrder)
                            : DecodeSplitFrameSize(_buffer, _stage._byteOrder);

                        if (payloadSize < 0)
                        {
                            FailStage(new Framing.FramingException($"Decoded frame header reported negative size {payloadSize}"));
                            return;
                        }

                        if (payloadSize > _stage._maxFrameSize)
                        {
                            FailStage(new Framing.FramingException(
                                $"Maximum allowed remote frame size is {_stage._maxFrameSize} but decoded frame header reported size {payloadSize}"));
                            return;
                        }

                        _payloadSize = payloadSize;
                    }

                    var frameSize = FrameHeaderBytes + _payloadSize.Value;
                    if (_buffer.Length < frameSize)
                    {
                        TryPull();
                        return;
                    }

                    var payload = _buffer.Slice(FrameHeaderBytes, _payloadSize.Value);
                    _buffer = _buffer.Slice(frameSize);
                    _payloadSize = null;
                    Push(_stage._out, payload);

                    if (_buffer.IsEmpty && IsClosed(_stage._in))
                        CompleteStage();
                }

                private void TryPull()
                {
                    if (IsClosed(_stage._in))
                        FailStage(new Framing.FramingException("Stream finished but there was a truncated final remote frame in the buffer"));
                    else
                        Pull(_stage._in);
                }

                private static int DecodeSplitFrameSize(ReadOnlySequence<byte> buffer, DotNettyByteOrder byteOrder)
                {
                    Span<byte> header = stackalloc byte[FrameHeaderBytes];
                    buffer.Slice(0, FrameHeaderBytes).CopyTo(header);
                    return DecodeFrameSize(header, byteOrder);
                }

                private static int DecodeFrameSize(ReadOnlySpan<byte> header, DotNettyByteOrder byteOrder)
                {
                    return byteOrder == DotNettyByteOrder.LittleEndian
                        ? BinaryPrimitives.ReadInt32LittleEndian(header)
                        : BinaryPrimitives.ReadInt32BigEndian(header);
                }
            }
        }

        private sealed class SequenceSegment : ReadOnlySequenceSegment<byte>
        {
            public SequenceSegment(ReadOnlyMemory<byte> memory)
            {
                Memory = memory;
            }

            public SequenceSegment Append(ReadOnlyMemory<byte> memory)
            {
                var next = new SequenceSegment(memory)
                {
                    RunningIndex = RunningIndex + Memory.Length
                };
                Next = next;
                return next;
            }
        }
    }
}
