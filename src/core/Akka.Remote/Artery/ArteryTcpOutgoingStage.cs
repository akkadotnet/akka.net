// -----------------------------------------------------------------------
// <copyright file="ArteryTcpOutgoingStage.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using Akka.IO;
using Akka.Streams;
using Akka.Streams.Stage;
using StreamTcp = Akka.Streams.Dsl.Tcp;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// Outgoing byte flow over the existing TCP pipes, TLS initialization and I/O pumps.
    /// Interpreter callbacks replace the connection actor's write acknowledgements and read commands.
    /// </summary>
    internal sealed class ArteryTcpOutgoingStage
        : GraphStageWithMaterializedValue<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>, Task<StreamTcp.OutgoingConnection>>
    {
        private readonly Func<CancellationToken, Task<ConnectedTransport>> _connect;
        private readonly Lifetimes _lifetimes;
        private readonly ILoggingAdapter _log;
        private readonly int _maximumWriteBytes;
        private readonly Inlet<ReadOnlySequence<byte>> _in = new("ArteryTcp.outgoing.in");
        private readonly Outlet<ReadOnlySequence<byte>> _out = new("ArteryTcp.outgoing.out");

        internal ArteryTcpOutgoingStage(EndPoint endpoint, TcpSettings settings,
            IImmutableList<Inet.SocketOption> options, TlsClientSettings? tls, ILoggingAdapter log, Lifetimes lifetimes)
            : this(token => ConnectAsync(endpoint, settings, options, tls, log, token),
                settings.WriteCommandsQueueMaxSize, log, lifetimes)
        {
        }

        internal ArteryTcpOutgoingStage(Func<CancellationToken, Task<ConnectedTransport>> connect,
            int maximumWriteBytes, ILoggingAdapter log, Lifetimes lifetimes)
        {
            _connect = connect;
            _maximumWriteBytes = maximumWriteBytes;
            _log = log;
            _lifetimes = lifetimes;
            Shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(_in, _out);
        }

        public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; }

        public override ILogicAndMaterializedValue<Task<StreamTcp.OutgoingConnection>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var connected = new TaskCompletionSource<StreamTcp.OutgoingConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
            return new LogicAndMaterializedValue<Task<StreamTcp.OutgoingConnection>>(new Logic(this, connected), connected.Task);
        }

        private static async Task<ConnectedTransport> ConnectAsync(EndPoint endpoint, TcpSettings settings,
            IImmutableList<Inet.SocketOption> options, TlsClientSettings? tls, ILoggingAdapter log, CancellationToken token)
        {
            var socket = settings.OutgoingSocketForceIpv4
                ? new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { Blocking = false }
                : new Socket(SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
            try
            {
                foreach (var option in options)
                    option.BeforeConnect(socket);
                for (var remaining = settings.FinishConnectRetries; ; remaining--)
                {
                    try
                    {
                        await socket.ConnectAsync(endpoint, token).ConfigureAwait(false);
                        break;
                    }
                    catch (SocketException) when (remaining > 0)
                    {
                        // Same short connection-attempt retry as TcpOutgoingConnection; the
                        // surrounding Artery RestartFlow still owns reconnect/backoff policy.
                        await Task.Delay(TimeSpan.FromMilliseconds(1), token).ConfigureAwait(false);
                    }
                }
                socket.Blocking = true;
                try
                {
                    socket.NoDelay = true;
                }
                catch (SocketException error)
                {
                    log.Debug(error, "Could not enable TcpNoDelay");
                }
                foreach (var option in options)
                    option.AfterConnect(socket);
                var inputSize = TcpConnection.ResolvePipeBufferSize(settings, options);
                var inputOptions = new PipeOptions(pauseWriterThreshold: inputSize * 2L,
                    resumeWriterThreshold: inputSize, minimumSegmentSize: settings.MaxFrameSizeBytes,
                    useSynchronizationContext: false);
                var transport = TcpTransportConnection.CreateForOutgoing(socket, endpoint, inputOptions,
                    TcpConnection.ResolveOutputPipeOptions(settings, options), tls, log);
                return new ConnectedTransport(transport, endpoint, socket.LocalEndPoint!);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        /// <summary>Admission and shutdown share one gate; cleanup promises precede native work.</summary>
        internal sealed class Lifetimes(ILoggingAdapter log)
        {
            private readonly object _gate = new();
            private readonly HashSet<Task> _owners = new();
            private bool _stopping;

            internal bool TryRegister(Task cleanup)
            {
                lock (_gate)
                {
                    if (_stopping)
                        return false;
                    _owners.Add(cleanup);
                }
                _ = cleanup.ContinueWith(completed =>
                {
                    if (completed.IsFaulted)
                        log.Error(completed.Exception, "Outgoing TCP native cleanup failed.");
                    lock (_gate)
                        _owners.Remove(completed);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return true;
            }

            internal int Count
            {
                get
                {
                    lock (_gate)
                        return _owners.Count;
                }
            }

            internal void StopAdmission()
            {
                lock (_gate)
                    _stopping = true;
            }

            internal async Task WaitAsync(CancellationToken deadline)
            {
                Task[] owners;
                lock (_gate)
                {
                    if (!_stopping)
                        throw new InvalidOperationException("Close TCP admission before waiting for owners.");
                    owners = new Task[_owners.Count];
                    _owners.CopyTo(owners);
                }
                try
                {
                    await Task.WhenAll(owners).WaitAsync(deadline).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    log.Warning("Outgoing TCP native cleanup did not finish before the transport shutdown deadline.");
                }
                catch (Exception error)
                {
                    // Every owner's fault is observed and logged by registration as well.
                    log.Error(error, "Outgoing TCP native cleanup completed with an error.");
                }
                finally
                {
                    lock (_gate)
                        _owners.RemoveWhere(task => task.IsCompleted);
                }
            }
        }

        /// <summary>Exclusive native ownership; late completion and stage cancellation may race.</summary>
        internal sealed class ConnectedTransport(ITransportConnection transport, EndPoint remote, EndPoint local)
        {
            private int _aborted;
            private readonly object _retirementGate = new();
            private Task? _retirement;
            internal ITransportConnection Transport { get; } = transport;
            internal StreamTcp.OutgoingConnection Addresses { get; } = new(remote, local);

            internal void Abort()
            {
                if (Interlocked.Exchange(ref _aborted, 1) == 0)
                    Transport.Abort();
            }

            internal async Task AbortAndRetireAsync()
            {
                Exception? failure = null;
                try
                {
                    Abort();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                try
                {
                    await RetireAsync(Task.CompletedTask).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                if (failure is not null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }

            internal Task RetireAsync(Task readOperation)
            {
                // Pending owners have never been read. Once Opened transfers ownership, only
                // interpreter PostStop retires that owner, with its final read operation.
                lock (_retirementGate)
                    return _retirement ??= RetireCoreAsync(readOperation);
            }

            private async Task RetireCoreAsync(Task readOperation)
            {
                Exception? failure = null;
                try
                {
                    await Transport.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = error;
                }
                try
                {
                    // Native disposal stops the producer. The consumer must finish copying and
                    // advancing before reader completion can release its outstanding pipe leases.
                    await readOperation.ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                try
                {
                    await Transport.Input.CompleteAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                if (failure is not null)
                    ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        private readonly record struct ReadCompletion(ReadOnlySequence<byte> Data, bool End);
        private readonly record struct PumpCompletion(bool Write, Exception? Error);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            // Keep the TCP stream's existing soft cap: one admitted element may exceed it.
            private const long WriteBufferCap = 16 * 1024;
            private readonly ArteryTcpOutgoingStage _stage;
            private readonly TaskCompletionSource<StreamTcp.OutgoingConnection> _connected;
            private readonly TaskCompletionSource<Done> _cleaned = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenSource _stop = new();
            private readonly Queue<ReadOnlySequence<byte>> _writes = new();
            private Task _opening = Task.CompletedTask;
            private Task _readOperation = Task.CompletedTask;
            private Task _flushOperation = Task.CompletedTask;
            private Task _closeOperation = Task.CompletedTask;
            private Task _readMonitor = Task.CompletedTask;
            private Task _writeMonitor = Task.CompletedTask;
            private ConnectedTransport? _pending;
            private ConnectedTransport? _connection;
            private long _bufferedBytes;
            private bool _flushing;
            private bool _reading;
            private bool _readFinished;
            private Exception? _readFailure;
            private bool _drainRead;
            private bool _closing;
            private bool _graceful;
            private ReadOnlySequence<byte> _pendingRead;
            private Action<Done> _opened = null!;
            private Action<Done> _flushed = null!;
            private Action<Done> _closed = null!;
            private Action<Exception> _failed = null!;
            private Action<ReadCompletion> _read = null!;
            private Action<PumpCompletion> _pump = null!;

            internal Logic(ArteryTcpOutgoingStage stage, TaskCompletionSource<StreamTcp.OutgoingConnection> connected)
                : base(stage.Shape)
            {
                _stage = stage;
                _connected = connected;
                SetHandler(stage._in, this);
                SetHandler(stage._out, this);
            }

            public override void PreStart()
            {
                SetKeepGoing(true);
                if (!_stage._lifetimes.TryRegister(_cleaned.Task))
                {
                    FailStage(new OperationCanceledException("Artery TCP transport is shutting down."));
                    return;
                }
                _failed = GetAsyncCallback<Exception>(error =>
                {
                    _connected.TrySetException(error);
                    FailStage(error);
                });
                _opened = GetAsyncCallback<Done>(_ => Opened());
                _flushed = GetAsyncCallback<Done>(_ => Flushed());
                _closed = GetAsyncCallback<Done>(_ =>
                {
                    _graceful = true;
                    CompleteStage();
                });
                _read = GetAsyncCallback<ReadCompletion>(Read);
                _pump = GetAsyncCallback<PumpCompletion>(completion =>
                {
                    if (_closing)
                        return;
                    if (completion.Write)
                    {
                        FailStage(completion.Error ?? new IOException("TCP write pump stopped before the outgoing flow completed."));
                        return;
                    }
                    if (completion.Error is { } error)
                    {
                        // The transport completes its input writer without an exception so final
                        // buffered bytes can drain. A pump notification must not overtake them.
                        _readFailure = error;
                        if (_readFinished)
                            FailStage(error);
                        else if (_pendingRead.IsEmpty && (IsAvailable(_stage._out) || IsClosed(_stage._out)))
                            StartRead();
                    }
                });
                _opening = OpenAsync();
            }

            private async Task OpenAsync()
            {
                ConnectedTransport? connection = null;
                try
                {
                    connection = await _stage._connect(_stop.Token).ConfigureAwait(false);
                    Interlocked.Exchange(ref _pending, connection);
                    _stop.Token.ThrowIfCancellationRequested();
                    await connection.Transport.InitializeAsync(_stop.Token).ConfigureAwait(false);
                    _stop.Token.ThrowIfCancellationRequested();
                    _opened(Done.Instance);
                }
                catch (Exception error)
                {
                    _connected.TrySetException(error);
                    _failed(error);
                    if (connection is not null)
                    {
                        Interlocked.CompareExchange(ref _pending, null, connection);
                        await connection.AbortAndRetireAsync().ConfigureAwait(false);
                    }
                }
            }

            private void Opened()
            {
                try
                {
                    _connection = Interlocked.Exchange(ref _pending, null)
                        ?? throw new InvalidOperationException("The connected transport was already retired.");
                    _connection.Transport.Start();
                    _connected.TrySetResult(_connection.Addresses);
                    _readMonitor = MonitorAsync(_connection.Transport.ReadCompleted, write: false);
                    _writeMonitor = MonitorAsync(_connection.Transport.WriteCompleted, write: true);
                    if (IsAvailable(_stage._out) || IsClosed(_stage._out))
                        StartRead();
                    if (IsClosed(_stage._in))
                        BeginClose();
                    else
                        PullIfNeeded();
                }
                catch (Exception error)
                {
                    _connected.TrySetException(error);
                    FailStage(error);
                }
            }

            private async Task MonitorAsync(Task completion, bool write)
            {
                try
                {
                    await completion.WaitAsync(_stop.Token).ConfigureAwait(false);
                    _pump(new PumpCompletion(write, null));
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception error)
                {
                    _pump(new PumpCompletion(write, error));
                }
            }

            public void OnPush()
            {
                var bytes = Grab(_stage._in);
                _writes.Enqueue(bytes);
                _bufferedBytes += bytes.Length;
                if (!_flushing)
                    Flush();
                PullIfNeeded();
            }

            private void PullIfNeeded()
            {
                if (!_closing && !IsClosed(_stage._in) && !HasBeenPulled(_stage._in) && _bufferedBytes < WriteBufferCap)
                    Pull(_stage._in);
            }

            private void Flush()
            {
                if (_stage._maximumWriteBytes >= 0 && _bufferedBytes > _stage._maximumWriteBytes)
                    throw new IOException("TCP write exceeds write-commands-queue-max-size.");
                while (_writes.Count > 0)
                {
                    var bytes = _writes.Dequeue();
                    try
                    {
                        _connection!.Transport.Write(bytes);
                    }
                    finally
                    {
                        bytes.DisposeOwnedSegments();
                    }
                }
                _bufferedBytes = 0;
                _flushing = true;
                _flushOperation = FlushAsync();
            }

            private async Task FlushAsync()
            {
                try
                {
                    var result = await _connection!.Transport.FlushAsync(_stop.Token).ConfigureAwait(false);
                    if (result.IsCanceled || result.IsCompleted)
                        throw new IOException("TCP output pipe stopped before the accepted write could be flushed.");
                    _flushed(Done.Instance);
                }
                catch (Exception error)
                {
                    _failed(error);
                }
            }

            private void Flushed()
            {
                _flushing = false;
                if (_writes.Count > 0)
                    Flush();
                if (IsClosed(_stage._in) && !_flushing)
                    BeginClose();
                else
                    PullIfNeeded();
            }

            public void OnPull()
            {
                if (!_pendingRead.IsEmpty)
                {
                    var bytes = _pendingRead;
                    _pendingRead = default;
                    Push(_stage._out, bytes);
                    if (_drainRead)
                        StartRead();
                }
                else if (_connection is not null)
                    StartRead();
            }

            private void StartRead()
            {
                if (_reading || _readFinished || _closing)
                    return;
                _reading = true;
                _readOperation = ReadAsync();
            }

            private async Task ReadAsync()
            {
                try
                {
                    var reader = _connection!.Transport.Input;
                    while (true)
                    {
                        var result = await reader.ReadAsync(_stop.Token).ConfigureAwait(false);
                        // Returned pipe memory must never escape to another stage or actor.
                        var bytes = result.Buffer.IsEmpty ? ReadOnlySequence<byte>.Empty
                            : new ReadOnlySequence<byte>(result.Buffer.ToArray());
                        reader.AdvanceTo(result.Buffer.End);
                        if (!bytes.IsEmpty || result.IsCompleted || result.IsCanceled)
                        {
                            _read(new ReadCompletion(bytes, result.IsCompleted || result.IsCanceled));
                            return;
                        }
                    }
                }
                catch (Exception error)
                {
                    _failed(error);
                }
            }

            private void Read(ReadCompletion result)
            {
                _reading = false;
                if (!result.Data.IsEmpty)
                {
                    _drainRead = result.End;
                    if (!IsClosed(_stage._out))
                    {
                        if (IsAvailable(_stage._out))
                            Push(_stage._out, result.Data);
                        else
                            _pendingRead = result.Data;
                    }
                    if (_pendingRead.IsEmpty && (_drainRead || IsClosed(_stage._out)))
                        StartRead();
                }
                else if (result.End)
                {
                    _readFinished = true;
                    var error = _readFailure ?? (_connection!.Transport.HasReadError
                        ? _connection.Transport.ReadError ?? new IOException("TCP read pump failed.") : null);
                    if (error is not null)
                        FailStage(error);
                    else
                        Complete(_stage._out);
                }
            }

            public void OnUpstreamFinish()
            {
                if (_connection is not null && !_flushing && _writes.Count == 0)
                    BeginClose();
            }

            public void OnUpstreamFailure(Exception error) => FailStage(error);

            public void OnDownstreamFinish(Exception cause)
            {
                if (cause is not SubscriptionWithCancelException.NonFailureCancellation)
                {
                    FailStage(cause);
                    return;
                }
                _pendingRead = default;
                if (_connection is not null)
                    StartRead();
            }

            private void BeginClose()
            {
                if (_closing)
                    return;
                _closing = true;
                _closeOperation = CloseAsync();
            }

            private async Task CloseAsync()
            {
                try
                {
                    await _connection!.Transport.CloseAsync().ConfigureAwait(false);
                    _closed(Done.Instance);
                }
                catch (Exception error)
                {
                    _failed(error);
                }
            }

            public override void PostStop()
            {
                Exception? failure = null;
                try
                {
                    _stop.Cancel();
                }
                catch (Exception error)
                {
                    failure = error;
                }
                _connected.TrySetCanceled();
                var pending = Interlocked.Exchange(ref _pending, null);
                try
                {
                    pending?.Abort();
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                try
                {
                    if (!_graceful)
                        _connection?.Abort();
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                while (_writes.Count > 0)
                {
                    try
                    {
                        _writes.Dequeue().DisposeOwnedSegments();
                    }
                    catch (Exception error)
                    {
                        failure = failure is null ? error : new AggregateException(failure, error);
                    }
                }
                _pendingRead = default;
                _ = CleanupAsync(pending, failure);
            }

            private async Task CleanupAsync(ConnectedTransport? pending, Exception? failure)
            {
                // Disposal stops the native pumps. Waiting for a monitor first would invert that
                // dependency, especially when the read outlet ended without consuming another read.
                try
                {
                    if (_connection is not null)
                        await _connection.RetireAsync(_readOperation).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                try
                {
                    if (pending is not null)
                        await pending.RetireAsync(Task.CompletedTask).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                try
                {
                    // Opening still owns late results; it retires them before its task settles.
                    await Task.WhenAll(_opening, _readOperation, _flushOperation, _closeOperation,
                        _readMonitor, _writeMonitor).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    failure = failure is null ? error : new AggregateException(failure, error);
                }
                finally
                {
                    _stop.Dispose();
                }
                if (failure is null)
                    _cleaned.TrySetResult(Done.Instance);
                else
                {
                    _stage._log.Error(failure, "TCP outgoing transport cleanup failed.");
                    _cleaned.TrySetException(failure);
                }
            }

        }
    }
}
