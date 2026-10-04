//-----------------------------------------------------------------------
// <copyright file="OutputStreamSourceStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Dispatch;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util;
using Akka.Util.Internal;
using static Akka.Streams.Implementation.IO.OutputStreamSourceStage;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OutputStreamSourceStage : GraphStageWithMaterializedValue<SourceShape<ReadOnlySequence<byte>>, Stream>
    {
        #region internal classes

        /// <summary>
        /// Marker for flush and close requests sent from the materialized stream to the stage.
        /// </summary>
        internal interface IAdapterToStageMessage
        {
        }

        /// <summary>
        /// Requests that the stage acknowledge a flush after queued data is handled.
        /// </summary>
        internal sealed class Flush : IAdapterToStageMessage
        {
            /// <summary>
            /// The shared flush request.
            /// </summary>
            public static readonly Flush Instance = new();

            private Flush()
            {
            }
        }

        /// <summary>
        /// Requests that the stage complete and close the materialized stream.
        /// </summary>
        internal sealed class Close : IAdapterToStageMessage
        {
            /// <summary>
            /// The shared close request.
            /// </summary>
            public static readonly Close Instance = new();

            private Close()
            {
            }
        }

        /// <summary>
        /// Marker for whether the source stage can still deliver data downstream.
        /// </summary>
        internal interface IDownstreamStatus
        {
        }

        /// <summary>
        /// Indicates that downstream has not canceled the source.
        /// </summary>
        internal sealed class Ok : IDownstreamStatus
        {
            /// <summary>
            /// The shared active downstream status.
            /// </summary>
            public static readonly Ok Instance = new();

            private Ok()
            {
            }
        }

        /// <summary>
        /// Indicates that downstream canceled the source.
        /// </summary>
        internal sealed class Canceled : IDownstreamStatus
        {
            /// <summary>
            /// The shared canceled downstream status.
            /// </summary>
            public static readonly Canceled Instance = new();

            private Canceled()
            {
            }
        }

        /// <summary>
        /// Provides asynchronous stage callbacks used by the materialized output-stream adapter.
        /// </summary>
        internal interface IStageWithCallback
        {
            /// <summary>
            /// Sends a flush or close request to the stage.
            /// </summary>
            /// <param name="msg">The adapter request to deliver.</param>
            /// <returns>A task completed when the stage acknowledges the request.</returns>
            Task WakeUp(IAdapterToStageMessage msg);
        }

        private sealed class Logic : OutGraphStageLogic, IStageWithCallback
        {
            private readonly OutputStreamSourceStage _stage;
            private readonly AtomicReference<IDownstreamStatus> _downstreamStatus;
            private readonly string _dispatcherId;
            private readonly Action<(IAdapterToStageMessage, TaskCompletionSource<NotUsed>)> _upstreamCallback;
            private readonly OnPullRunnable _pullTask;
            private readonly CancellationTokenSource _cancellation = new();
            private BlockingCollection<ReadOnlySequence<byte>> _dataQueue;
            private TaskCompletionSource<NotUsed> _flush;
            private TaskCompletionSource<NotUsed> _close;
            private MessageDispatcher _dispatcher;

            public Logic(OutputStreamSourceStage stage, BlockingCollection<ReadOnlySequence<byte>> dataQueue,
                AtomicReference<IDownstreamStatus> downstreamStatus, string dispatcherId) : base(stage.Shape)
            {
                _stage = stage;
                _dataQueue = dataQueue;
                _downstreamStatus = downstreamStatus;
                _dispatcherId = dispatcherId;

                var downstreamCallback = GetAsyncCallback<Either<ReadOnlySequence<byte>, Exception>>(result =>
                {
                    if (result.IsLeft)
                        OnPush(result.ToLeft().Value);
                    else
                        FailStage(result.ToRight().Value);
                });
                _upstreamCallback =
                    GetAsyncCallback<(IAdapterToStageMessage, TaskCompletionSource<NotUsed>)>(OnAsyncMessage);
                _pullTask = new OnPullRunnable(downstreamCallback, dataQueue, _cancellation.Token);
                SetHandler(_stage._out, this);
            }

            public override void PreStart()
            {
                _dispatcher = ActorMaterializerHelper.Downcast(Materializer).System.Dispatchers.Lookup(_dispatcherId);
                base.PreStart();
            }

            public override void PostStop()
            {
                //assuming there can be no further in messages
                _downstreamStatus.Value = Canceled.Instance;
                _dataQueue = null;
                CompleteStage();

                // interrupt any pending blocking take
                _cancellation.Cancel(false);
                base.PostStop();
            }

            private sealed class OnPullRunnable : IRunnable
            {
                private readonly Action<Either<ReadOnlySequence<byte>, Exception>> _callback;
                private readonly BlockingCollection<ReadOnlySequence<byte>> _dataQueue;
                private readonly CancellationToken _cancellationToken;

                public OnPullRunnable(Action<Either<ReadOnlySequence<byte>, Exception>> callback,
                    BlockingCollection<ReadOnlySequence<byte>> dataQueue, CancellationToken cancellationToken)
                {
                    _callback = callback;
                    _dataQueue = dataQueue;
                    _cancellationToken = cancellationToken;
                }

                public void Run()
                {
                    try
                    {
                        _callback(new Left<ReadOnlySequence<byte>, Exception>(_dataQueue.Take(_cancellationToken)));
                    }
                    catch (OperationCanceledException)
                    {
                        _callback(new Left<ReadOnlySequence<byte>, Exception>(ReadOnlySequence<byte>.Empty));
                    }
                    catch (Exception ex)
                    {
                        _callback(new Right<ReadOnlySequence<byte>, Exception>(ex));
                    }
                }

#if !NETSTANDARD
                public void Execute()
                {
                    Run();
                }
#endif
            }

            public override void OnPull() => _dispatcher.Schedule(_pullTask);

            private void OnPush(ReadOnlySequence<byte> data)
            {
                if (_downstreamStatus.Value is Ok)
                {
                    Push(_stage._out, data);
                    SendResponseIfNeeded();
                }
            }

            public Task WakeUp(IAdapterToStageMessage msg)
            {
                var p = TaskEx.NonBlockingTaskCompletionSource<NotUsed>();
                _upstreamCallback((msg, p));
                return p.Task;
            }

            private void OnAsyncMessage((IAdapterToStageMessage, TaskCompletionSource<NotUsed>) @event)
            {
                if (@event.Item1 is Flush)
                {
                    _flush = @event.Item2;
                    SendResponseIfNeeded();
                }
                else if (@event.Item1 is Close)
                {
                    _close = @event.Item2;
                    SendResponseIfNeeded();
                }
            }

            private void UnblockUpsteam()
            {
                if (_flush != null)
                {
                    _flush.TrySetResult(NotUsed.Instance);
                    _flush = null;
                    return;
                }

                if (_close == null)
                    return;

                _downstreamStatus.Value = Canceled.Instance;
                _close.TrySetResult(NotUsed.Instance);
                _close = null;
                CompleteStage();
            }

            private void SendResponseIfNeeded()
            {
                if (_downstreamStatus.Value is Canceled || _dataQueue.Count == 0)
                    UnblockUpsteam();
            }
        }

        #endregion

        private readonly TimeSpan _writeTimeout;
        private readonly Outlet<ReadOnlySequence<byte>> _out = new("OutputStreamSource.out");

        /// <summary>
        /// Creates a source stage that materializes an output stream whose writes become source elements.
        /// </summary>
        /// <param name="writeTimeout">The maximum time the adapter waits for a flush or close acknowledgement.</param>
        public OutputStreamSourceStage(TimeSpan writeTimeout)
        {
            _writeTimeout = writeTimeout;
            Shape = new SourceShape<ReadOnlySequence<byte>>(_out);
        }

        /// <summary>
        /// The source shape that emits byte sequences written to the materialized stream.
        /// </summary>
        public override SourceShape<ReadOnlySequence<byte>> Shape { get; }

        /// <summary>
        /// The default attributes for the output-stream source.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.OutputStreamSource;

        /// <summary>
        /// Creates stage logic and a write-only stream backed by a bounded queue of byte sequences.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this stage, including its input buffer size and dispatcher.</param>
        /// <exception cref="ArgumentException">Thrown when the maximum input buffer size is not positive.</exception>
        /// <returns>The stage logic and its materialized write-only stream.</returns>
        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            // has to be in this order as module depends on shape
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greater than 0");

            var dataQueue = new BlockingCollection<ReadOnlySequence<byte>>(maxBuffer);
            var downstreamStatus = new AtomicReference<IDownstreamStatus>(Ok.Instance);

            var dispatcherId =
                inheritedAttributes.GetAttribute(
                    DefaultAttributes.IODispatcher.GetAttributeList<ActorAttributes.Dispatcher>().First()).Name;
            var logic = new Logic(this, dataQueue, downstreamStatus, dispatcherId);
            return new LogicAndMaterializedValue<Stream>(logic,
                new OutputStreamAdapter(dataQueue, downstreamStatus, logic, _writeTimeout));
        }
    }

    /// <summary>
    /// Write-only stream adapter that sends byte sequences to <see cref="OutputStreamSourceStage"/>.
    /// </summary>
    internal sealed class OutputStreamAdapter : Stream
    {
        #region not supported

        /// <summary>
        /// Seeking is unsupported because this adapter is not seekable.
        /// </summary>
        /// <param name="offset">The offset from the requested origin.</param>
        /// <param name="origin">The position used as the reference point for the offset.</param>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports writing only.</exception>
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// Changing the stream length is unsupported because this adapter is write-only.
        /// </summary>
        /// <param name="value">The requested length.</param>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports writing only.</exception>
        public override void SetLength(long value) => throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// Reading is unsupported because this adapter is write-only.
        /// </summary>
        /// <param name="buffer">The buffer that would receive the read bytes.</param>
        /// <param name="offset">The buffer offset at which reading would begin.</param>
        /// <param name="count">The maximum number of bytes that would be read.</param>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports writing only.</exception>
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// Getting the stream length is unsupported because this adapter is not seekable.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown because the adapter does not expose a seekable length.</exception>
        public override long Length => throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// Getting or setting the stream position is unsupported because this adapter is not seekable.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown because the adapter does not expose a seekable position.</exception>
        public override long Position
        {
            get => throw new NotSupportedException("This stream can only write");
            set => throw new NotSupportedException("This stream can only write");
        }

        #endregion

        private static readonly Exception PublisherClosedException =
            new IOException("Reactive stream is terminated, no writes are possible");

        private readonly BlockingCollection<ReadOnlySequence<byte>> _dataQueue;
        private readonly AtomicReference<IDownstreamStatus> _downstreamStatus;
        private readonly IStageWithCallback _stageWithCallback;
        private readonly TimeSpan _writeTimeout;
        private bool _isActive = true;
        private bool _isPublisherAlive = true;

        /// <summary>
        /// Creates an adapter that queues writes and sends flush and close requests to the source stage.
        /// </summary>
        /// <param name="dataQueue">The bounded queue of byte sequences to publish.</param>
        /// <param name="downstreamStatus">The current downstream cancellation status.</param>
        /// <param name="stageWithCallback">The callback used to notify the stage of flush and close requests.</param>
        /// <param name="writeTimeout">The maximum time to wait for a stage acknowledgement.</param>
        public OutputStreamAdapter(BlockingCollection<ReadOnlySequence<byte>> dataQueue,
            AtomicReference<IDownstreamStatus> downstreamStatus,
            IStageWithCallback stageWithCallback, TimeSpan writeTimeout)
        {
            _dataQueue = dataQueue;
            _downstreamStatus = downstreamStatus;
            _stageWithCallback = stageWithCallback;
            _writeTimeout = writeTimeout;
        }

        private void Send(Action sendAction)
        {
            if (_isActive)
            {
                if (_isPublisherAlive)
                    sendAction();
                else
                    throw PublisherClosedException;
            }
            else
                throw new IOException("OutputStream is closed");
        }

        private void SendData(ReadOnlySequence<byte> data) => Send(() =>
        {
            _dataQueue.Add(data);

            if (_downstreamStatus.Value is Canceled)
            {
                _isPublisherAlive = false;
                throw PublisherClosedException;
            }
        });


        private void SendMessage(IAdapterToStageMessage msg, bool handleCancelled = true) => Send(() =>
        {
            _stageWithCallback.WakeUp(msg).Wait(_writeTimeout);
            if (_downstreamStatus.Value is Canceled && handleCancelled)
            {
                //Publisher considered to be terminated at earliest convenience to minimize messages sending back and forth
                _isPublisherAlive = false;
                throw PublisherClosedException;
            }
        });


        /// <summary>
        /// Requests a flush and waits up to the configured timeout for the stage to acknowledge it.
        /// </summary>
        public override void Flush() => SendMessage(OutputStreamSourceStage.Flush.Instance);

        /// <summary>
        /// Copies the requested byte range into a new sequence and queues it for emission by the source.
        /// </summary>
        /// <param name="buffer">The buffer containing bytes to write.</param>
        /// <param name="offset">The zero-based offset of the first byte to write.</param>
        /// <param name="count">The number of bytes to copy and write.</param>
        public override void Write(byte[] buffer, int offset, int count)
        {
            // Stream.Write returns before downstream demand may consume this chunk, so
            // copy out of the caller-owned buffer before publishing it to the stream.
            var source = new ReadOnlyMemory<byte>(buffer, offset, count);
            SendData(new ReadOnlySequence<byte>(source.ToArray()));
        }

        /// <summary>
        /// Sends a close request to the stage and marks the adapter closed.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SendMessage(OutputStreamSourceStage.Close.Instance, false);
            _isActive = false;
        }

        /// <summary>
        /// Indicates that this adapter does not support reading.
        /// </summary>
        public override bool CanRead => false;

        /// <summary>
        /// Indicates that this adapter does not support seeking.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Indicates that this adapter supports writing.
        /// </summary>
        public override bool CanWrite => true;
    }
}
