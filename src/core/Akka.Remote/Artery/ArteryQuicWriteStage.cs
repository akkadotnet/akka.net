// -----------------------------------------------------------------------
// <copyright file="ArteryQuicWriteStage.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// One outstanding native write, with native input held until that write settles. Coalesced
    /// writes return the original encoded owners after copying their bytes. The unused
    /// read outlet preserves the existing byte-flow graph shape for a unidirectional channel.
    /// </summary>
    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("windows")]
    [SupportedOSPlatform("osx")]
    internal sealed class ArteryQuicWriteStage
        : GraphStageWithMaterializedValue<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>, Task<Done>>
    {
        // Match the ordinary lane batch cap. Larger sequences keep the segmented path.
        private const int MaximumCoalescedWriteSize = 16 * 1024;

        private readonly ArteryQuicTransport _transport;
        private readonly EndPoint _endpoint;
        private readonly ArteryStreamId _role;
        private readonly int _ordinaryStream;

        internal ArteryQuicWriteStage(ArteryQuicTransport transport, EndPoint endpoint, ArteryStreamId role, int ordinaryStream = 0)
        {
            _transport = transport;
            _endpoint = endpoint;
            _role = role;
            _ordinaryStream = ordinaryStream;
        }

        private readonly Inlet<ReadOnlySequence<byte>> _in = new("ArteryQuic.in");
        private readonly Outlet<ReadOnlySequence<byte>> _out = new("ArteryQuic.out");
        public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape => new(_in, _out);

        public override ILogicAndMaterializedValue<Task<Done>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var connected = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            return new LogicAndMaterializedValue<Task<Done>>(new Logic(this, connected), connected.Task);
        }

        internal static ValueTask WriteCoalescedAsync(Stream stream, ReadOnlySequence<byte> bytes,
            ArrayPool<byte> pool, CancellationToken cancellationToken)
        {
            // Keep source sequence, stream and cancellation state out of the suspended write.
            // Preserve faulted-ValueTask behavior for synchronous preparation/submission errors.
            try
            {
                byte[]? buffer = null;
                try
                {
                    var length = checked((int)bytes.Length);
                    buffer = pool.Rent(length);
                    var memory = buffer.AsMemory(0, length);
                    bytes.CopyTo(memory.Span);
                    bytes.DisposeOwnedSegments();
                    bytes = default;
                    var write = stream.WriteAsync(memory, cancellationToken);
                    var submitted = buffer;
                    buffer = null;
                    return CompleteCoalescedWriteAsync(write, submitted, pool);
                }
                finally
                {
                    try
                    {
                        bytes.DisposeOwnedSegments();
                    }
                    finally
                    {
                        if (buffer is not null)
                            pool.Return(buffer);
                    }
                }
            }
            catch (Exception ex)
            {
                var completion = AsyncValueTaskMethodBuilder.Create();
                completion.SetException(ex);
                return completion.Task;
            }
        }

        private static async ValueTask CompleteCoalescedWriteAsync(ValueTask write, byte[] buffer, ArrayPool<byte> pool)
        {
            try
            {
                await write.ConfigureAwait(false);
            }
            finally
            {
                pool.Return(buffer);
            }
        }

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly ArteryQuicWriteStage _stage;
            private readonly TaskCompletionSource<Done> _connected;
            private readonly CancellationTokenSource _stop = new();
            private ArteryQuicTransport.Channel? _channel;
            private ArteryQuicTransport.Channel? _pendingChannel;
            private bool _writing;
            private bool _finished;
            private bool _graceful;
            private Task? _initializing;
            private Task? _monitoring;
            private Task? _writingOperation;
            private Task? _finishing;
            private Action<Exception>? _failed;
            private Action<Done>? _written;

            internal Logic(ArteryQuicWriteStage stage, TaskCompletionSource<Done> connected) : base(stage.Shape)
            {
                _stage = stage;
                _connected = connected;
                SetHandler(stage._in, this);
                SetHandler(stage._out, this);
            }

            public override void PreStart()
            {
                _failed = GetAsyncCallback<Exception>(ex =>
                {
                    _connected.TrySetException(ex);
                    FailStage(ex);
                });
                _written = GetAsyncCallback<Done>(done =>
                {
                    _writing = false;
                    if (_finished)
                        _finishing = FinishAsync();
                    else
                        Pull(_stage._in);
                });
                var opened = GetAsyncCallback<Done>(done =>
                {
                    var channel = Interlocked.Exchange(ref _pendingChannel, null)!;
                    _channel = channel;
                    _connected.TrySetResult(Done.Instance);
                    if (_finished)
                        _finishing = FinishAsync();
                    else
                        Pull(_stage._in);
                    _monitoring = MonitorAsync(channel);
                });
                _initializing = OpenAsync(opened);
            }

            private async Task OpenAsync(Action<Done> opened)
            {
                try
                {
                    var channel = await _stage._transport.OpenAsync(_stage._endpoint, _stage._role, _stop.Token, _stage._ordinaryStream).ConfigureAwait(false);
                    Interlocked.Exchange(ref _pendingChannel, channel);
                    if (_stop.IsCancellationRequested)
                        Interlocked.Exchange(ref _pendingChannel, null)?.Retire();
                    else
                        opened(Done.Instance);
                }
                catch (Exception ex)
                {
                    _connected.TrySetException(ex);
                    _failed!(ex);
                }
            }

            private async Task MonitorAsync(ArteryQuicTransport.Channel channel)
            {
                try
                {
                    await channel.Stream.WritesClosed.WaitAsync(_stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    if (!_stop.IsCancellationRequested)
                        _failed!(ex);
                }
            }

            public void OnPush()
            {
                var bytes = Grab(_stage._in);
                _writing = true;
                _writingOperation = WriteAsync(bytes);
            }

            private async Task WriteAsync(ReadOnlySequence<byte> bytes)
            {
                try
                {
                    var channel = _channel!;
                    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token, channel.CancellationToken);
                    if (_stage._role == ArteryStreamId.Ordinary && !bytes.IsSingleSegment &&
                        !bytes.IsEmpty && bytes.Length <= MaximumCoalescedWriteSize)
                    {
                        var length = (int)bytes.Length;
                        var coalesced = bytes;
                        // Transfer cleanup to the copying operation, including rent/copy failure.
                        bytes = default;
                        await WriteCoalescedAsync(channel.Stream, coalesced, ArrayPool<byte>.Shared, lifetime.Token).ConfigureAwait(false);
                        _stage._transport.RecordWrite(_stage._role, length);
                    }
                    else
                    {
                        foreach (var segment in bytes)
                        {
                            await channel.Stream.WriteAsync(segment, lifetime.Token).ConfigureAwait(false);
                            _stage._transport.RecordWrite(_stage._role, segment.Length);
                        }
                    }
                    _written!(Done.Instance);
                }
                catch (Exception ex)
                {
                    _failed!(ex);
                }
                finally
                {
                    // Direct writes return original owners here after native settlement.
                    // Copied writes transferred their cleanup to WriteCoalescedAsync.
                    bytes.DisposeOwnedSegments();
                }
            }

            public void OnUpstreamFinish()
            {
                _finished = true;
                SetKeepGoing(true);
                if (!_writing && _channel is not null)
                    _finishing = FinishAsync();
            }

            private async Task FinishAsync()
            {
                var complete = GetAsyncCallback<Done>(_ =>
                {
                    _graceful = true;
                    CompleteStage();
                });
                try
                {
                    _channel!.Stream.CompleteWrites();
                    await _channel.Stream.WritesClosed.WaitAsync(_stop.Token).ConfigureAwait(false);
                    // Peer owns native disposal; retaining this closed stream until connection
                    // retirement also prevents another channel from consuming its role/credit.
                    complete(Done.Instance);
                }
                catch (Exception ex)
                {
                    _failed!(ex);
                }
            }

            public void OnUpstreamFailure(Exception ex) => FailStage(ex);
            public void OnPull() { }
            public void OnDownstreamFinish(Exception cause) => CompleteStage();

            public override void PostStop()
            {
                _stop.Cancel();
                _connected.TrySetCanceled();
                Interlocked.Exchange(ref _pendingChannel, null)?.Retire();
                // A stopped channel fences all sibling streams, including idle writers.
                if (!_graceful)
                    _channel?.Retire();
                _stage._transport.Track(CleanupAsync());
            }

            private async Task CleanupAsync()
            {
                try
                {
                    await Task.WhenAll(_initializing ?? Task.CompletedTask, _monitoring ?? Task.CompletedTask,
                        _writingOperation ?? Task.CompletedTask, _finishing ?? Task.CompletedTask).ConfigureAwait(false);
                }
                finally
                {
                    _stop.Dispose();
                }
            }
        }
    }
}
