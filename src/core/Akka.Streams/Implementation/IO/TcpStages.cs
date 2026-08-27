//-----------------------------------------------------------------------
// <copyright file="TcpStages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.IO;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;
using StreamTcp = Akka.Streams.Dsl.Tcp;
using Tcp = Akka.IO.Tcp;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ConnectionSourceStage : GraphStageWithMaterializedValue<SourceShape<StreamTcp.IncomingConnection>, Task<StreamTcp.ServerBinding>>
    {
        #region internal classes

        private sealed class ConnectionSourceStageLogic : TimerGraphStageLogic, IOutHandler
        {
            private const string BindShutdownTimer = "BindTimer";

            private readonly AtomicCounterLong _connectionFlowsAwaitingInitialization = new();
            private readonly ConnectionSourceStage _stage;
            private IActorRef _listener;
            private readonly TaskCompletionSource<StreamTcp.ServerBinding> _bindingPromise;
            private readonly TaskCompletionSource<NotUsed> _unbindPromise = TaskEx.NonBlockingTaskCompletionSource<NotUsed>();
            private bool _unbindStarted = false;
            private readonly Queue<StreamTcp.IncomingConnection> _pendingConnections = new();

            public ConnectionSourceStageLogic(Shape shape, ConnectionSourceStage stage, TaskCompletionSource<StreamTcp.ServerBinding> bindingPromise)
                : base(shape)
            {
                _stage = stage;
                _bindingPromise = bindingPromise;

                SetHandler(_stage._out, this);
            }

            public void OnPull()
            {
                TryPush();
            }

            private void TryPush()
            {
                if (!IsAvailable(_stage._out)) return; // we have demand and can push
                if (_pendingConnections.Count <= 0) return;
                
                var toPush = _pendingConnections.Dequeue();
                Push(_stage._out, toPush);
            }

            public void OnDownstreamFinish(Exception cause)
            {
                if (Log.IsDebugEnabled)
                {
                    var endpoint = (IPEndPoint)_stage._endpoint;
                    if (cause is SubscriptionWithCancelException.NonFailureCancellation)
                        Log.Debug("Unbinding from {0} because downstream cancelled stream", endpoint);
                    else
                        Log.Debug(cause, "Unbinding from {0} because of downstream failure", endpoint);
                }
                
                TryUnbind();
            }

            private StreamTcp.IncomingConnection ConnectionFor(Tcp.Connected connected, IActorRef connection)
            {
                _connectionFlowsAwaitingInitialization.IncrementAndGet();

                var tcpFlow =
                    Flow.FromGraph(new IncomingConnectionStage(connection, connected.RemoteAddress, _stage._halfClose))
                    .Via(new Detacher<ReadOnlySequence<byte>>()) // must read ahead for proper completions
                    .MapMaterializedValue(unit =>
                    {
                        _connectionFlowsAwaitingInitialization.DecrementAndGet();
                        return unit;
                    });

                // FIXME: Previous code was wrong, must add new tests
                var handler = tcpFlow;
                if (_stage._idleTimeout.HasValue)
                    handler = tcpFlow.Join(TcpIdleTimeout.Create(_stage._idleTimeout.Value, connected.RemoteAddress));

                return new StreamTcp.IncomingConnection(connected.LocalAddress, connected.RemoteAddress, handler);
            }

            private void TryUnbind()
            {
                if (_listener != null && !_unbindStarted)
                {
                    _unbindStarted = true;
                    SetKeepGoing(true);
                    _listener.Tell(Tcp.Unbind.Instance, StageActor.Ref);
                }
            }

            private void UnbindCompleted()
            {
                StageActor.Unwatch(_listener);
                if (_connectionFlowsAwaitingInitialization.Current == 0)
                    CompleteStage();
                else
                    ScheduleOnce(BindShutdownTimer, _stage._bindShutdownTimeout);
            }

            protected internal override void OnTimer(object timerKey)
            {
                if (Equals(BindShutdownTimer, timerKey))
                    CompleteStage(); // TODO need to manually shut down instead right?
            }

            public override void PreStart()
            {
                GetStageActor(Receive);
                _stage._tcpManager.Tell(new Tcp.Bind(StageActor.Ref, _stage._endpoint, _stage._backlog, _stage._options, pullMode: true), StageActor.Ref);
            }

            private void Receive((IActorRef, object) args)
            {
                var sender = args.Item1;
                var msg = args.Item2;
                switch (msg)
                {
                    case Tcp.Bound bound:
                        _listener = sender;
                        StageActor.Watch(_listener);

                        if (IsAvailable(_stage._out))
                            _listener.Tell(new Tcp.ResumeAccepting(1), StageActor.Ref);

                        var thisStage = StageActor.Ref;
                        var binding = new StreamTcp.ServerBinding(bound.LocalAddress, () =>
                        {
                            // To allow unbind() to be invoked multiple times with minimal chance of dead letters, we check if
                            // it's already unbound before sending the message.
                            if (!_unbindPromise.Task.IsCompleted)
                            {
                                // Beware, sender must be explicit since stageActor.ref will be invalid to access after the stage stopped
                                thisStage.Tell(Tcp.Unbind.Instance, thisStage);
                            }
                            return _unbindPromise.Task;
                        });

                        _bindingPromise.NonBlockingTrySetResult(binding);
                        break;
                    
                    case Tcp.CommandFailed _:
                        var ex = BindFailedException.Instance;
                        _bindingPromise.NonBlockingTrySetException(ex);
                        _unbindPromise.TrySetResult(NotUsed.Instance);
                        FailStage(ex);
                        break;
                    
                    case Tcp.Connected connected:
                        _pendingConnections.Enqueue(ConnectionFor(connected, sender));
                        TryPush();
                        break;
                    
                    case Tcp.Unbind _:
                        if (!(_unbindStarted || IsClosed(_stage._out) || ReferenceEquals(_listener, null)))
                            TryUnbind();
                        break;
                    
                    case Tcp.Unbound _:
                    case Terminated _ when _unbindStarted:
                        UnbindCompleted();
                        break;
                    
                    case Terminated _:
                        FailStage(new IllegalStateException("IO Listener actor terminated unexpectedly"));
                        break;
                }
            }

            public override void PostStop()
            {
                _unbindPromise.TrySetResult(NotUsed.Instance);
                _bindingPromise.NonBlockingTrySetException(
                    new NoSuchElementException("Binding was unbound before it was completely finished"));
            }
        }

        #endregion

        private readonly IActorRef _tcpManager;
        private readonly EndPoint _endpoint;
        private readonly int _backlog;
        private readonly IImmutableList<Inet.SocketOption> _options;
        private readonly bool _halfClose;
        private readonly TimeSpan? _idleTimeout;
        private readonly TimeSpan _bindShutdownTimeout;
        private readonly Outlet<StreamTcp.IncomingConnection> _out = new("IncomingConnections.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcpManager">TBD</param>
        /// <param name="endpoint">TBD</param>
        /// <param name="backlog">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="halfClose">TBD</param>
        /// <param name="idleTimeout">TBD</param>
        /// <param name="bindShutdownTimeout">TBD</param>
        public ConnectionSourceStage(IActorRef tcpManager, EndPoint endpoint, int backlog,
            IImmutableList<Inet.SocketOption> options, bool halfClose, TimeSpan? idleTimeout,
            TimeSpan bindShutdownTimeout)
        {
            _tcpManager = tcpManager;
            _endpoint = endpoint;
            _backlog = backlog;
            _options = options;
            _halfClose = halfClose;
            _idleTimeout = idleTimeout;
            _bindShutdownTimeout = bindShutdownTimeout;
            Shape = new SourceShape<StreamTcp.IncomingConnection>(_out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<StreamTcp.IncomingConnection> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("ConnectionSource");

        // TODO: Timeout on bind
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<StreamTcp.ServerBinding>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var bindingPromise = TaskEx.NonBlockingTaskCompletionSource<StreamTcp.ServerBinding>();
            var logic = new ConnectionSourceStageLogic(Shape, this, bindingPromise);
            return new LogicAndMaterializedValue<Task<StreamTcp.ServerBinding>>(logic, bindingPromise.Task);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public class IncomingConnectionStage : GraphStage<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>>
    {
        private readonly IActorRef _connection;
        private readonly EndPoint _remoteAddress;
        private readonly bool _halfClose;
        private readonly AtomicBoolean _hasBeenCreated = new();
        private readonly Inlet<ReadOnlySequence<byte>> _bytesIn = new("IncomingTCP.in");
        private readonly Outlet<ReadOnlySequence<byte>> _bytesOut = new("IncomingTCP.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="halfClose">TBD</param>
        public IncomingConnectionStage(IActorRef connection, EndPoint remoteAddress, bool halfClose)
        {
            _connection = connection;
            _remoteAddress = remoteAddress;
            _halfClose = halfClose;
            Shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(_bytesIn, _bytesOut);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("IncomingConnection");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <exception cref="IllegalStateException">TBD</exception>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            if (_hasBeenCreated.Value)
                throw new IllegalStateException("Cannot materialize an incoming connection Flow twice.");
            _hasBeenCreated.Value = true;

            return new TcpConnectionStage.TcpStreamLogic(Shape, new TcpConnectionStage.Inbound(_connection, _halfClose), _remoteAddress);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"TCP-from({_remoteAddress})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class TcpConnectionStage
    {
        private class WriteAck : Tcp.Event
        {
            public static readonly WriteAck Instance = new();

            private WriteAck()
            {

            }

        }

        /// <summary>
        /// TBD
        /// </summary>
        internal interface ITcpRole
        {
            /// <summary>
            /// TBD
            /// </summary>
            bool HalfClose { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal readonly struct Outbound : ITcpRole
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="manager">TBD</param>
            /// <param name="connectCmd">TBD</param>
            /// <param name="localAddressPromise">TBD</param>
            /// <param name="halfClose">TBD</param>
            public Outbound(IActorRef manager, Tcp.Connect connectCmd, TaskCompletionSource<EndPoint> localAddressPromise, bool halfClose)
            {
                Manager = manager;
                ConnectCmd = connectCmd;
                LocalAddressPromise = localAddressPromise;
                HalfClose = halfClose;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Manager;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly Tcp.Connect ConnectCmd;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly TaskCompletionSource<EndPoint> LocalAddressPromise;

            /// <summary>
            /// TBD
            /// </summary>
            public bool HalfClose { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal readonly struct Inbound : ITcpRole
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="connection">TBD</param>
            /// <param name="halfClose">TBD</param>
            public Inbound(IActorRef connection, bool halfClose)
            {
                Connection = connection;
                HalfClose = halfClose;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public readonly IActorRef Connection;

            /// <summary>
            /// TBD
            /// </summary>
            public bool HalfClose { get; }
        }

        /// <summary>
        /// This is a *non-detached* design, i.e. this does not prefetch itself any of the inputs. It relies on downstream
        /// stages to provide the necessary prefetch on `bytesOut` and the framework to do the proper prefetch in the buffer
        /// backing `bytesIn`. If prefetch on `bytesOut` is required (i.e. user stages cannot be trusted) then it is better
        /// to attach an extra, fused buffer to the end of this flow. Keeping this stage non-detached makes it much simpler and
        /// easier to maintain and understand.
        /// </summary>
        internal sealed class TcpStreamLogic : GraphStageLogic
        {
            /// <summary>
            /// Coalescing cap for <see cref="_writeBufferedBytes"/>, mirroring Pekko's
            /// <c>pekko.stream.materializer.io.tcp.write-buffer-size</c> default of 16 KiB
            /// (pekko stream/src/main/resources/reference.conf). While a write is in flight the
            /// stage keeps pulling and accumulating elements below this cap instead of doing one
            /// write/ack round-trip per element; at/over the cap it stops pulling, which is the
            /// natural backpressure signal. Kept as an internal constant rather than a
            /// configurable Attributes/HOCON knob to keep this change scoped to the coalescing
            /// behavior itself -- exposing it the way Pekko does would be a reasonable follow-up.
            /// </summary>
            private const long WriteBufferCap = 16 * 1024;

            private readonly ITcpRole _role;
            private readonly EndPoint _remoteAddress;
            private readonly Inlet<ReadOnlySequence<byte>> _bytesIn;
            private readonly Outlet<ReadOnlySequence<byte>> _bytesOut;
            private IActorRef _connection;
            private readonly OutHandler _readHandler;

            // Write-coalescing state. While a write is outstanding (sent to the connection actor,
            // awaiting WriteAck), elements pushed from upstream are appended here instead of being
            // sent immediately; the whole accumulation is flushed as a single Tcp.Write once the
            // outstanding WriteAck arrives. Ports Pekko's TcpConnectionStage.TcpStreamLogic
            // writeBuffer/writeInProgress behavior (pekko stream/.../impl/io/TcpStages.scala, the
            // writeBuffer/writeInProgress fields and the onPush/WriteAck handling around lines
            // 275-388), minus the optional WriteDelayAck/coalesceWrites round-trip refinement --
            // see the comments on the WriteAck case in <see cref="Connected"/> for why.
            private WriteBufferSegment _writeBufferHead;
            private WriteBufferSegment _writeBufferTail;
            private long _writeBufferedBytes;

            /// <summary>There is a write outstanding (sent to the connection actor, awaiting <see cref="WriteAck"/>).</summary>
            private bool _writeInProgress;

            /// <summary>
            /// Upstream already finished (or downstream cancelled while upstream had already
            /// finished) but a write was still in flight/buffered at that time; the deferred
            /// Close/ConfirmedClose is sent once the write buffer fully drains.
            /// </summary>
            private bool _connectionClosePending;

            /// <summary>
            /// A minimal <see cref="ReadOnlySequenceSegment{T}"/> node used to chain buffered write
            /// payloads together -- a rope-like concatenation mirroring Pekko's <c>ByteString ++</c>
            /// accumulation of its writeBuffer. Zero-copy: each node wraps the SAME memory a producer
            /// handed to <see cref="AppendToWriteBuffer"/>, and optionally carries the
            /// <see cref="IMemoryOwner{T}"/> that memory came from (detached from the producer's own
            /// segment -- see that method's remarks). The owner is optional HERE, unlike
            /// <see cref="Akka.IO.OwnedSequenceSegment"/> (which always owns): this buffer is an
            /// AGGREGATOR that mixes owned frames (e.g. Artery's encoded frames) with borrowed links
            /// (e.g. the one-time connection preamble, which is never pool-backed) in the same chain.
            /// </summary>
            private sealed class WriteBufferSegment : ReadOnlySequenceSegment<byte>, IOwnedSequenceSegment
            {
                private IMemoryOwner<byte>? _owner;

                public WriteBufferSegment(ReadOnlyMemory<byte> memory, IMemoryOwner<byte>? owner = null)
                {
                    Memory = memory;
                    _owner = owner;
                }

                public void Chain(WriteBufferSegment next)
                {
                    next.RunningIndex = RunningIndex + Memory.Length;
                    Next = next;
                }

                /// <inheritdoc />
                public bool HasOwner => _owner is not null;

                /// <inheritdoc />
                public void DisposeOwner()
                {
                    var owner = _owner;
                    _owner = null;
                    owner?.Dispose();
                }

                /// <inheritdoc />
                public IMemoryOwner<byte>? DetachOwner()
                {
                    var owner = _owner;
                    _owner = null;
                    return owner;
                }
            }

            public TcpStreamLogic(FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> shape, ITcpRole role, EndPoint remoteAddress) : base(shape)
            {
                _role = role;
                _remoteAddress = remoteAddress;
                _bytesIn = shape.Inlet;
                _bytesOut = shape.Outlet;

                _readHandler = new LambdaOutHandler(
                    onPull: () =>
                    {
                        _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);
                    },
                    onDownstreamFinish: cause =>
                    {
                        if (cause is SubscriptionWithCancelException.NonFailureCancellation)
                        {
                            if(Log.IsDebugEnabled)
                                Log.Debug("Closing connection from {0} because downstream cancelled stream without failure", (IPEndPoint)_remoteAddress);
                            if (IsClosed(_bytesIn))
                            {
                                // A write that is still outstanding/buffered must be flushed
                                // (via WriteAck -> CloseConnectionUpstreamFinished) before we
                                // close -- otherwise bytes coalesced while that write was in
                                // flight would be silently dropped.
                                if (_writeInProgress)
                                    _connectionClosePending = true;
                                else
                                    _connection.Tell(Tcp.Close.Instance, StageActor.Ref);
                            }
                            else
                                _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);
                        }
                        else
                        {
                            if(Log.IsDebugEnabled)
                                Log.Debug(cause, "Aborting connection from {0} because of downstream failure", (IPEndPoint)_remoteAddress);
                            // Abort tears the connection down immediately; any buffered/in-flight
                            // write is intentionally dropped here (matches Pekko -- there is no
                            // flush-on-abort) same as onUpstreamFailure below.
                            _connection.Tell(Tcp.Abort.Instance, StageActor.Ref);
                            FailStage(cause);
                        }
                    });

                // No reading until role have been decided
                SetHandler(_bytesOut, onPull: DoNothing);
                SetHandler(_bytesIn,
                    onPush: () =>
                    {
                        var elem = Grab(_bytesIn);
                        ReactiveStreamsCompliance.RequireNonNullElement(elem);

                        // Unconditionally accumulate first -- mirrors Pekko's
                        // `writeBuffer = writeBuffer ++ elem` (TcpStages.scala ~474-483), which
                        // appends before branching on whether to send now or keep collecting.
                        // See AppendToWriteBuffer's remarks for the zero-copy ownership-transfer
                        // mechanism.
                        AppendToWriteBuffer(elem);

                        if (!_writeInProgress)
                        {
                            // Nothing outstanding: flush (= send this one element) immediately.
                            // The key change over the previous lock-step behavior is what happens
                            // next -- we keep demand open below so more elements can accumulate
                            // while this write's WriteAck round-trip to the connection actor is
                            // in flight, instead of waiting for the ack before pulling again.
                            FlushWriteBuffer();
                        }

                        if (_writeBufferedBytes < WriteBufferCap)
                            Pull(_bytesIn);
                        // else: at/over the cap -- stay un-pulled, this is the natural
                        // backpressure signal (mirrors Pekko's
                        // `if (writeBuffer.length < writeBufferSize) pull(bytesIn)`,
                        // TcpStages.scala ~484-485).
                    },
                    onUpstreamFinish: CloseConnectionUpstreamFinished,
                    onUpstreamFailure: ex =>
                    {
                        if (_connection != null)
                        {
                            if (Interpreter.Log.IsDebugEnabled)
                                Interpreter.Log.Debug(
                                    $"Aborting tcp connection to {_remoteAddress} because of upstream failure: {ex.Message}\n{ex.StackTrace}");
                            // Abort tears the connection down immediately; any buffered/in-flight
                            // write is intentionally dropped here, matching Pekko (no
                            // flush-on-abort).
                            _connection.Tell(Tcp.Abort.Instance, StageActor.Ref);
                        }
                        else
                            FailStage(ex);
                    });
            }

            /// <summary>
            /// Appends every segment of <paramref name="data"/> to the write-coalescing buffer --
            /// zero-copy: no <see cref="ReadOnlyMemory{T}.ToArray"/> or other memcpy anywhere in this
            /// method. Each appended <see cref="WriteBufferSegment"/> wraps the SAME memory
            /// <paramref name="data"/> exposed; if that memory is pool-backed, the owner responsible
            /// for eventually returning it is TRANSFERRED into the new segment rather than copied
            /// (modernize-akka-io-tcp design.md, Decision 8 / the ownership-transfer mechanism).
            /// </summary>
            /// <remarks>
            /// <b>Two cases, told apart by how <paramref name="data"/> is backed:</b>
            /// <para>
            /// <b>Segment-backed</b> (<c>data.Start.GetObject()</c> is a
            /// <see cref="ReadOnlySequenceSegment{T}"/> -- e.g. every frame
            /// <c>Akka.Remote.Artery.ArteryEncodeStage</c> pushes, each a single
            /// <see cref="Akka.IO.OwnedSequenceSegment"/>): this is (a chain of) pool-backed memory
            /// this stage is now responsible for. Walk <paramref name="data"/>'s OWN segment chain
            /// from its <c>Start</c> segment to its <c>End</c> segment (inclusive, bounded so the
            /// walk never runs past the tail this sequence actually references -- a later segment in
            /// the same chain could belong to a different, still-live write). For each link, take
            /// <c>(segment as IOwnedSequenceSegment)?.DetachOwner()</c> -- moving responsibility for
            /// disposal from the producer's segment to this buffer's own <see cref="WriteBufferSegment"/>
            /// wrapping the identical memory (sliced to <paramref name="data"/>'s own start/end
            /// offsets on the first/last link) -- and append it. A link's owner is never null-checked
            /// away here even if its sliced memory happens to be empty: dropping it instead of
            /// appending would detach the owner from its source without giving it anywhere to be
            /// disposed later, i.e. a leak.
            /// </para>
            /// <para>
            /// <b>Memory-/array-backed</b> (e.g. the once-per-connection preamble built by
            /// <c>ArteryRemoting.BuildPreamble</c> as <c>new ReadOnlySequence&lt;byte&gt;(buffer)</c>):
            /// this data is BORROWED, not owned -- there is no producer-side segment to detach
            /// anything from. Each non-empty chunk <see langword="foreach"/> yields becomes a
            /// <see cref="WriteBufferSegment"/> with a <see langword="null"/> owner (still zero-copy:
            /// the memory itself is still referenced, not copied), so the later buffer-teardown walk
            /// correctly skips it.
            /// </para>
            /// </remarks>
            private void AppendToWriteBuffer(ReadOnlySequence<byte> data)
            {
                if (data.IsEmpty)
                    return;

                if (data.Start.GetObject() is ReadOnlySequenceSegment<byte> segment)
                {
                    var startObject = segment;
                    var endSegment = data.End.GetObject() as ReadOnlySequenceSegment<byte>;
                    var startIndex = data.Start.GetInteger();
                    var endIndex = data.End.GetInteger();

                    while (segment is not null)
                    {
                        var memory = segment.Memory;
                        var isFirst = ReferenceEquals(segment, startObject);
                        var isLast = ReferenceEquals(segment, endSegment);

                        if (isFirst)
                            memory = memory.Slice(startIndex);
                        if (isLast)
                            memory = memory.Slice(0, isFirst ? endIndex - startIndex : endIndex);

                        var owner = (segment as IOwnedSequenceSegment)?.DetachOwner();
                        AppendWriteBufferSegment(memory, owner);

                        if (ReferenceEquals(segment, endSegment))
                            break;

                        segment = segment.Next!;
                    }
                }
                else
                {
                    foreach (var memory in data)
                    {
                        if (memory.IsEmpty)
                            continue;

                        AppendWriteBufferSegment(memory, owner: null);
                    }
                }
            }

            /// <summary>
            /// Appends a single <see cref="WriteBufferSegment"/> wrapping <paramref name="memory"/>
            /// (and, if non-null, carrying <paramref name="owner"/>) to the tail of the write buffer.
            /// </summary>
            private void AppendWriteBufferSegment(ReadOnlyMemory<byte> memory, IMemoryOwner<byte>? owner)
            {
                var segment = new WriteBufferSegment(memory, owner);
                if (_writeBufferHead is null)
                    _writeBufferHead = segment;
                else
                    _writeBufferTail!.Chain(segment);

                _writeBufferTail = segment;
                _writeBufferedBytes += memory.Length;
            }

            /// <summary>
            /// Removes and returns everything currently buffered as a single
            /// <see cref="ReadOnlySequence{T}"/> view over the chained segments (no copy), and
            /// resets the buffer to empty.
            /// </summary>
            private ReadOnlySequence<byte> DrainWriteBuffer()
            {
                if (_writeBufferHead is null)
                    return ReadOnlySequence<byte>.Empty;

                var sequence = new ReadOnlySequence<byte>(_writeBufferHead, 0, _writeBufferTail!, _writeBufferTail!.Memory.Length);
                _writeBufferHead = null;
                _writeBufferTail = null;
                _writeBufferedBytes = 0;
                return sequence;
            }

            /// <summary>
            /// Sends everything currently buffered as one <see cref="Tcp.Write"/>, marks a write
            /// as outstanding, and clears the buffer. Mirrors Pekko's <c>sendWriteBuffer()</c>
            /// (TcpStages.scala ~336-340).
            /// </summary>
            private void FlushWriteBuffer()
            {
                var buffered = DrainWriteBuffer();
                _connection.Tell(Tcp.Write.Create(buffered, WriteAck.Instance), StageActor.Ref);
                _writeInProgress = true;
            }

            /// <summary>
            /// Sends the connection's Close/ConfirmedClose (honoring half-close and the read
            /// side's state) if no write is currently outstanding; otherwise defers via
            /// <see cref="_connectionClosePending"/> until the buffered write(s) drain. Mirrors
            /// Pekko's <c>closeConnectionUpstreamFinished()</c> (TcpStages.scala ~403-424) --
            /// upstream finishing must never truncate a write that is still in flight or
            /// buffered.
            /// </summary>
            private void CloseConnectionUpstreamFinished()
            {
                // Reading has stopped before, either because of cancel, or PeerClosed, so just Close now
                // (or half-close is turned off)
                if (IsClosed(_bytesOut) || !_role.HalfClose)
                {
                    if (_writeInProgress)
                        _connectionClosePending = true; // continues once WriteAck drains the write buffer
                    else
                        _connection.Tell(Tcp.Close.Instance, StageActor.Ref);
                }
                // We still read, so we only close the write side
                else if (_connection != null)
                {
                    if (_writeInProgress)
                        _connectionClosePending = true;
                    else
                        _connection.Tell(Tcp.ConfirmedClose.Instance, StageActor.Ref);
                }
                else
                    CompleteStage();
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void PreStart()
            {
                SetKeepGoing(true);

                if (_role is Inbound inbound)
                {
                    SetHandler(_bytesOut, _readHandler);
                    _connection = inbound.Connection;
                    GetStageActor(Connected).Watch(_connection);
                    _connection.Tell(new Tcp.Register(StageActor.Ref, keepOpenOnPeerClosed: true, useResumeWriting: false), StageActor.Ref);
                    Pull(_bytesIn);
                }
                else
                {
                    var outbound = (Outbound)_role;
                    GetStageActor(Connecting(outbound)).Watch(outbound.Manager);
                    outbound.Manager.Tell(outbound.ConnectCmd, StageActor.Ref);
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void PostStop()
            {
                if (_role is Outbound outbound)
                {
                    // Fail if has not been completed with an address earlier
                    outbound.LocalAddressPromise.TrySetException(new StreamTcpException("Connection failed"));
                }

                // Catch-all for any owner(s) still sitting in the write buffer at teardown --
                // abort/fail/cancel all drop the buffer without flushing it (see the
                // onDownstreamFinish/onUpstreamFailure handlers above and CloseConnectionUpstreamFinished's
                // "defer until drained" comment), so this is where those owners actually get disposed.
                // On a graceful path the buffer has already been handed off to Tcp.Write and DRAINED
                // (DrainWriteBuffer nulls _writeBufferHead/_writeBufferTail on every flush -- see
                // FlushWriteBuffer), so this walk finds nothing and is a no-op; a flushed write's
                // owners become TcpConnection's responsibility to dispose at the pipe copy, never
                // this buffer's.
                var segment = _writeBufferHead;
                while (segment is not null)
                {
                    segment.DisposeOwner();
                    segment = (WriteBufferSegment?)segment.Next;
                }

                _writeBufferHead = null;
                _writeBufferTail = null;
            }

            private StageActorRef.Receive Connecting(Outbound outbound)
            {
                return args =>
                {
                    var sender = args.Item1;
                    var msg = args.Item2;

                    if (msg is Terminated)
                        FailStage(new StreamTcpException("The IO manager actor (TCP) has terminated. Stopping now."));
                    else if (msg is Tcp.CommandFailed failed)
                        FailStage(new StreamTcpException($"Tcp command {failed.Cmd} failed"));
                    else if (msg is Tcp.Connected connected)
                    {
                        ((Outbound)_role).LocalAddressPromise.TrySetResult(connected.LocalAddress);
                        _connection = sender;
                        SetHandler(_bytesOut, _readHandler);
                        StageActor.Unwatch(outbound.Manager);
                        StageActor.Become(Connected);
                        StageActor.Watch(_connection);
                        _connection.Tell(new Tcp.Register(StageActor.Ref, keepOpenOnPeerClosed: true, useResumeWriting: false), StageActor.Ref);

                        if (IsAvailable(_bytesOut))
                            _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);

                        Pull(_bytesIn);
                    }
                };

            }

            private void Connected((IActorRef, object) args)
            {
                var msg = args.Item2;

                switch (msg)
                {
                    // Keep on reading even when closed. There is no "close-read-side" in TCP
                    case Tcp.Received received when IsClosed(_bytesOut):
                        _connection.Tell(Tcp.ResumeReading.Instance, StageActor.Ref);
                        break;
                    case Tcp.Received received:
                        Push(_bytesOut, received.Data);
                        break;
                    case WriteAck:
                    {
                        if (_writeBufferHead is null)
                        {
                            // Nothing accumulated while this write was outstanding.
                            _writeInProgress = false;
                        }
                        else
                        {
                            // Flush everything accumulated while this write's ack was outstanding
                            // as a single Tcp.Write -- this is the coalescing payoff: N pushes
                            // become far fewer write/ack round-trips instead of one round-trip per
                            // element. Mirrors Pekko's WriteAck branch, minus the optional
                            // WriteDelayAck/coalesceWrites round-trip refinement (which
                            // deliberately delays this flush by one more empty-write round-trip to
                            // probe for a few more upstream elements before sending -- TcpStages.scala
                            // 362-388); this port always flushes immediately on ack instead.
                            FlushWriteBuffer();
                        }

                        if (!_writeInProgress && _connectionClosePending)
                            CloseConnectionUpstreamFinished();

                        if (!IsClosed(_bytesIn) && !HasBeenPulled(_bytesIn))
                            Pull(_bytesIn);

                        break;
                    }
                    case Terminated:
                        FailStage(new StreamTcpException("The connection actor has terminated. Stopping now."));
                        break;
                    case Tcp.CommandFailed failed:
                        FailStage(new StreamTcpException($"Tcp command {failed.Cmd} failed"));
                        break;
                    case Tcp.ErrorClosed closed:
                        FailStage(new StreamTcpException($"The connection closed with error: {closed.Cause}"));
                        break;
                    case Tcp.Aborted:
                        FailStage(new StreamTcpException("The connection has been aborted"));
                        break;
                    case Tcp.Closed:
                    case Tcp.ConfirmedClosed:
                        CompleteStage();
                        break;
                    case Tcp.PeerClosed:
                        Complete(_bytesOut);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OutgoingConnectionStage :
        GraphStageWithMaterializedValue<FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>, Task<StreamTcp.OutgoingConnection>>
    {
        private readonly IActorRef _tcpManager;
        private readonly EndPoint _remoteAddress;
        private readonly EndPoint _localAddress;
        private readonly IImmutableList<Inet.SocketOption> _options;
        private readonly bool _halfClose;
        private readonly TimeSpan? _connectionTimeout;
        private readonly Inlet<ReadOnlySequence<byte>> _bytesIn = new("IncomingTCP.in");
        private readonly Outlet<ReadOnlySequence<byte>> _bytesOut = new("IncomingTCP.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="tcpManager">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="localAddress">TBD</param>
        /// <param name="options">TBD</param>
        /// <param name="halfClose">TBD</param>
        /// <param name="connectionTimeout">TBD</param>
        public OutgoingConnectionStage(IActorRef tcpManager, EndPoint remoteAddress, EndPoint localAddress = null,
            IImmutableList<Inet.SocketOption> options = null, bool halfClose = true, TimeSpan? connectionTimeout = null)
        {
            _tcpManager = tcpManager;
            _remoteAddress = remoteAddress;
            _localAddress = localAddress;
            _options = options;
            _halfClose = halfClose;
            _connectionTimeout = connectionTimeout;
            Shape = new FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(_bytesIn, _bytesOut);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("OutgoingConnection");

        /// <summary>
        /// TBD
        /// </summary>
        public override FlowShape<ReadOnlySequence<byte>, ReadOnlySequence<byte>> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<StreamTcp.OutgoingConnection>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var localAddressPromise = TaskEx.NonBlockingTaskCompletionSource<EndPoint>();
            var outgoingConnectionPromise = TaskEx.NonBlockingTaskCompletionSource<StreamTcp.OutgoingConnection>();
            localAddressPromise.Task.ContinueWith(t =>
                {
                    if (t.IsCanceled) outgoingConnectionPromise.TrySetCanceled();
                    else if (t.IsFaulted) outgoingConnectionPromise.TrySetException(t.Exception);
                    else outgoingConnectionPromise.TrySetResult(new StreamTcp.OutgoingConnection(_remoteAddress, t.Result));
                }, TaskContinuationOptions.AttachedToParent);

            var logic = new TcpConnectionStage.TcpStreamLogic(Shape, new TcpConnectionStage.Outbound(_tcpManager, new Tcp.Connect(_remoteAddress, _localAddress, _options, _connectionTimeout, pullMode: true), localAddressPromise, _halfClose), _remoteAddress);

            return new LogicAndMaterializedValue<Task<StreamTcp.OutgoingConnection>>(logic, outgoingConnectionPromise.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"TCP-to({_remoteAddress})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class TcpIdleTimeout
    {
        public static BidiFlow<ReadOnlySequence<byte>, ReadOnlySequence<byte>, ReadOnlySequence<byte>, ReadOnlySequence<byte>, NotUsed> Create(TimeSpan idleTimeout, EndPoint remoteAddress = null)
        {
            var connectionString = remoteAddress == null ? "" : $" on connection to [{remoteAddress}]";

            var idleException = new TcpIdleTimeoutException(
                $"TCP idle-timeout encountered{connectionString}, no bytes passed in the last {idleTimeout}",
                idleTimeout);

            var toNetTimeout = BidiFlow.FromFlows(
                Flow.Create<ReadOnlySequence<byte>>().SelectError(e => e is TimeoutException ? idleException : e),
                Flow.Create<ReadOnlySequence<byte>>());

            var fromNetTimeout = toNetTimeout.Reversed(); // now the bottom flow transforms the exception, the top one doesn't (since that one is "fromNet") 

            return fromNetTimeout.Atop(BidiFlow.BidirectionalIdleTimeout<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(idleTimeout))
                .Atop(toNetTimeout);
        }
    }
}
