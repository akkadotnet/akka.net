//-----------------------------------------------------------------------
// <copyright file="RemoteTcpFramingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Remote.Transport.Streams;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using DotNettyByteOrder = DotNetty.Buffers.ByteOrder;

namespace Akka.Remote.Tests.Transport
{
    public class RemoteTcpFramingSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public RemoteTcpFramingSpec(ITestOutputHelper output)
            : base(ActorMaterializer.DefaultConfig(), output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public async Task RemoteTcpFraming_should_decode_multiple_big_endian_frames_from_one_chunk()
        {
            var payload1 = new byte[] { 1, 2, 3 };
            var payload2 = Array.Empty<byte>();
            var payload3 = new byte[] { 4, 5, 6, 7 };
            var chunk = Combine(
                Encode(payload1, DotNettyByteOrder.BigEndian),
                Encode(payload2, DotNettyByteOrder.BigEndian),
                Encode(payload3, DotNettyByteOrder.BigEndian));

            var decoded = await Decode(new[] { new ReadOnlySequence<byte>(chunk) }, DotNettyByteOrder.BigEndian);

            decoded.Should().HaveCount(3);
            decoded[0].ToArray().Should().Equal(payload1);
            decoded[1].ToArray().Should().BeEmpty();
            decoded[2].ToArray().Should().Equal(payload3);
        }

        [Fact]
        public async Task RemoteTcpFraming_should_decode_little_endian_frames_split_across_chunks()
        {
            var payload = new byte[] { 11, 12, 13, 14, 15, 16, 17, 18 };
            var frame = Encode(payload, DotNettyByteOrder.LittleEndian);

            var decoded = await Decode(Split(frame, 1, 2, 4), DotNettyByteOrder.LittleEndian);

            decoded.Should().ContainSingle();
            decoded[0].ToArray().Should().Equal(payload);
        }

        [Fact]
        public async Task RemoteTcpFraming_should_reject_oversized_frames()
        {
            var header = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(header, 5);

            Func<Task> decode = async () => await Decode(new[] { new ReadOnlySequence<byte>(header) }, DotNettyByteOrder.BigEndian, maxFrameSize: 4);

            await decode.Should()
                .ThrowAsync<Framing.FramingException>()
                .WithMessage("Maximum allowed remote frame size is 4 but decoded frame header reported size 5");
        }

        [Fact]
        public async Task RemoteTcpFraming_should_reject_truncated_final_frame()
        {
            var frame = Encode(new byte[] { 1, 2, 3, 4 }, DotNettyByteOrder.BigEndian);
            var truncated = frame.AsMemory(0, frame.Length - 1);

            Func<Task> decode = async () => await Decode(new[] { new ReadOnlySequence<byte>(truncated) }, DotNettyByteOrder.BigEndian);

            await decode.Should()
                .ThrowAsync<Framing.FramingException>()
                .WithMessage("Stream finished but there was a truncated final remote frame in the buffer");
        }

        private async Task<IReadOnlyList<ReadOnlySequence<byte>>> Decode(
            IEnumerable<ReadOnlySequence<byte>> chunks,
            DotNettyByteOrder byteOrder,
            int maxFrameSize = 1024)
        {
            return await Source.From(chunks)
                .Via(RemoteTcpFraming.Decoder(maxFrameSize, byteOrder))
                .RunWith(Sink.Seq<ReadOnlySequence<byte>>(), _materializer)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }

        private static byte[] Encode(byte[] payload, DotNettyByteOrder byteOrder)
        {
            return RemoteTcpFraming.Encode(new ReadOnlySequence<byte>(payload), 1024, byteOrder).ToArray();
        }

        private static ReadOnlySequence<byte>[] Split(byte[] bytes, params int[] prefixLengths)
        {
            var chunks = new List<ReadOnlySequence<byte>>(prefixLengths.Length + 1);
            var offset = 0;

            foreach (var length in prefixLengths)
            {
                chunks.Add(new ReadOnlySequence<byte>(bytes.AsMemory(offset, length)));
                offset += length;
            }

            if (offset < bytes.Length)
                chunks.Add(new ReadOnlySequence<byte>(bytes.AsMemory(offset)));

            return chunks.ToArray();
        }

        private static byte[] Combine(params byte[][] arrays)
        {
            var length = 0;
            foreach (var array in arrays)
                length += array.Length;

            var combined = new byte[length];
            var offset = 0;
            foreach (var array in arrays)
            {
                array.CopyTo(combined.AsSpan(offset));
                offset += array.Length;
            }

            return combined;
        }
    }
}
