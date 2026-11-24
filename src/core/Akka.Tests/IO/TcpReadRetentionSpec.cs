#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.IO;

public class TcpReadRetentionSpec : AkkaSpec
{
    public TcpReadRetentionSpec(ITestOutputHelper output) : base(output) { }

    [Fact(DisplayName = "Retain 1000 mixed TCP deliveries through advancement, completion reuse and close")]
    public async Task Should_retain_1000_deliveries_after_close()
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var probe = CreateTestProbe();
        var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);
        var retained = new List<ReadOnlySequence<byte>>();
        var arrays = new HashSet<byte[]>();
        var random = new Random(91);
        var edges = new[] { 1, 65535, 65536, 65537, 131072, 131073, 1048576 };
        for (var i = 0; i < 1000; i++)
        {
            var length = i < edges.Length ? edges[i] : random.Next(1, 1048577);
            var transport = pipe.Writer.GetMemory(length).Slice(0, length);
            Fill(transport.Span, i);
            if (i % 2 == 0) reader.Start();
            pipe.Writer.Advance(length);
            await pipe.Writer.FlushAsync();
            if (i % 2 != 0) reader.Start();
            var data = (await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>(TimeSpan.FromSeconds(10))).Data;
            Assert.Equal(length, data.Length);
            foreach (var memory in data)
            {
                Assert.InRange(memory.Length, 1, 65536);
                Assert.True(MemoryMarshal.TryGetArray(memory, out var array));
                Assert.True(arrays.Add(array.Array!), "No array may be reused between deliveries");
                Assert.Equal(memory.Length, array.Array!.Length);
            }
            // Simulate immediate transport-pool reuse after AdvanceTo.
            transport.Span.Fill(0xed);
            retained.Add(data);
        }
        await pipe.Writer.CompleteAsync();
        reader.Start();
        Assert.True((await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).IsCompleted);
        await pipe.Reader.CompleteAsync();
        for (var i = 0; i < retained.Count; i++)
        {
            var offset = 0;
            foreach (var memory in retained[i])
            {
                for (var j = 0; j < memory.Length; j++, offset++)
                    Assert.Equal(Value(i, offset), memory.Span[j]);
            }
            Assert.Equal(retained[i].Length, offset);
        }
    }

    [Theory(DisplayName = "TCP split geometry is correct with awkward source segments")]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(65537)]
    [InlineData(131072)]
    [InlineData(131073)]
    [InlineData(1048576)]
    public async Task Should_preserve_sequence_geometry_from_awkward_source(int length)
    {
        var expected = new byte[length];
        Fill(expected, 17);
        var sourceArrays = new List<byte[]>();
        Link? head = null;
        Link? tail = null;
        var sizes = new[] { 3, 8191, 1, 65529, 17, 4093 };
        for (var offset = 0; offset < length;)
        {
            var size = Math.Min(sizes[sourceArrays.Count % sizes.Length], length - offset);
            var array = expected.AsSpan(offset, size).ToArray();
            sourceArrays.Add(array);
            var next = new Link(array);
            if (tail is null) head = next;
            else tail.Append(next);
            tail = next;
            offset += size;
        }
        var source = new ReadOnlySequence<byte>(head!, 0, tail!, tail!.Memory.Length);
        var pipe = new FixedReader(source);
        var probe = CreateTestProbe();
        new TcpConnection.PipeReadToActor(pipe, CancellationToken.None, probe.Ref).Start();
        var data = (await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).Data;
        Assert.Equal(source.End, pipe.Consumed);
        foreach (var array in sourceArrays) array.AsSpan().Fill(0xee);
        Assert.Equal(expected, data.ToArray());
        Assert.Equal(length, data.Length);
        Assert.Equal(data.Start, data.GetPosition(0));
        Assert.Equal(data.End, data.GetPosition(length));
        var count = 0;
        foreach (var memory in data)
        {
            Assert.InRange(memory.Length, 1, 65536);
            count++;
        }
        Assert.Equal((length + 65535) / 65536, count);
        if (data.Start.GetObject() is ReadOnlySequenceSegment<byte> segment)
        {
            long running = 0;
            ReadOnlySequenceSegment<byte>? current = segment;
            while (current is not null)
            {
                Assert.Equal(running, current.RunningIndex);
                running += current.Memory.Length;
                current = current.Next;
            }
            Assert.Equal(length, running);
            Assert.Equal(0, data.Start.GetInteger());
            Assert.Equal(length % 65536 == 0 ? 65536 : length % 65536, data.End.GetInteger());
        }
        foreach (var offset in new[] { 0, 1, 65534, 65535, 65536, 65537, 131071, 131072, length - 1, length })
        {
            if (offset > length) continue;
            var position = data.GetPosition(offset);
            Assert.Equal(expected.AsSpan(offset).ToArray(), data.Slice(position).ToArray());
            Assert.Equal(expected.AsSpan(0, offset).ToArray(), data.Slice(data.Start, position).ToArray());
            var size = Math.Min(9, length - offset);
            Assert.Equal(expected.AsSpan(offset, size).ToArray(), data.Slice(position, size).ToArray());
        }
    }

    private static byte Value(int delivery, int offset) => (byte)((delivery * 17 + offset * 31) % 251);
    private static void Fill(Span<byte> bytes, int delivery)
    {
        for (var i = 0; i < bytes.Length; i++) bytes[i] = Value(delivery, i);
    }
    private sealed class Link : ReadOnlySequenceSegment<byte>
    {
        public Link(ReadOnlyMemory<byte> memory) => Memory = memory;
        public void Append(Link next) { next.RunningIndex = RunningIndex + Memory.Length; Next = next; }
    }
    private sealed class FixedReader : PipeReader
    {
        private readonly ReadOnlySequence<byte> _source;
        public SequencePosition Consumed { get; private set; }
        public FixedReader(ReadOnlySequence<byte> source) => _source = source;
        public override void AdvanceTo(SequencePosition consumed) => Consumed = consumed;
        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => AdvanceTo(consumed);
        public override void CancelPendingRead() => throw new NotSupportedException();
        public override void Complete(Exception? exception = null) { Assert.Null(exception); }
        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default) => new(new ReadResult(_source, false, false));
        public override bool TryRead(out ReadResult result) { result = new ReadResult(_source, false, false); return true; }
    }
}
