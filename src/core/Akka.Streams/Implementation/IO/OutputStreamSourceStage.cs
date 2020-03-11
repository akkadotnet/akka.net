//-----------------------------------------------------------------------
// <copyright file="OutputStreamSourceStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Dispatch;
using Akka.IO;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util;
using static Akka.Streams.Implementation.IO.OutputStreamSourceStage;

namespace Akka.Streams.Implementation.IO
{

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class OutputStreamSourceStage : GraphStageWithMaterializedValue<SourceShape<ByteString>, Stream>
    {
        #region internal classes

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IAdapterToStageMessage { }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Flush : IAdapterToStageMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Flush Instance = new Flush();

            private Flush()
            {
                
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Close : IAdapterToStageMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Close Instance = new Close();

            private Close()
            {
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IDownstreamStatus { }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Ok : IDownstreamStatus
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Ok Instance = new Ok();

            private Ok()
            {

            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Canceled : IDownstreamStatus
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Canceled Instance = new Canceled();

            private Canceled()
            {

            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IStageWithCallback
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="msg">TBD</param>
            /// <returns>TBD</returns>
            Task WakeUp(IAdapterToStageMessage msg);
        }

        private sealed class Logic : OutGraphStageLogic, IStageWithCallback
        {
            private readonly OutputStreamSourceStage _stage;
            private readonly AtomicReference<IDownstreamStatus> _downstreamStatus;
            private readonly string _dispatcherId;
            private readonly Action<(IAdapterToStageMessage, TaskCompletionSource<NotUsed>)> _upstreamCallback;
            private readonly OnPullRunnable _pullTask;
            private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
            private BlockingCollection<ByteString> _dataQueue;
            private TaskCompletionSource<NotUsed> _flush;
            private TaskCompletionSource<NotUsed> _close;
            private MessageDispatcher _dispatcher;

            public Logic(OutputStreamSourceStage stage, BlockingCollection<ByteString> dataQueue, AtomicReference<IDownstreamStatus> downstreamStatus, string dispatcherId) : base(stage.Shape)
            {
                _stage = stage;
                _dataQueue = dataQueue;
                _downstreamStatus = downstreamStatus;
                _dispatcherId = dispatcherId;

                var downstreamCallback = GetAsyncCallback<Either<ByteString, Exception>>(result =>
                {
                    if (result.IsLeft)
                        OnPush(result.Value as ByteString);
                    else
                        FailStage(result.Value as Exception);
                });
                _upstreamCallback = GetAsyncCallback<(IAdapterToStageMessage, TaskCompletionSource<NotUsed>)>(OnAsyncMessage);
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
                private readonly Action<Either<ByteString, Exception>> _callback;
                private readonly BlockingCollection<ByteString> _dataQueue;
                private readonly CancellationToken _cancellationToken;

                public OnPullRunnable(Action<Either<ByteString, Exception>> callback, BlockingCollection<ByteString> dataQueue, CancellationToken cancellationToken)
                {
                    _callback = callback;
                    _dataQueue = dataQueue;
                    _cancellationToken = cancellationToken;
                }

                public void Run()
                {
                    try
                    {
                        _callback(new Left<ByteString, Exception>(_dataQueue.Take(_cancellationToken)));
                    }
                    catch (OperationCanceledException)
                    {
                        _callback(new Left<ByteString, Exception>(ByteString.Empty));
                    }
                    catch (Exception ex)
                    {
                        _callback(new Right<ByteString, Exception>(ex));
                    }
                }
            }
            
            public override void OnPull() => _dispatcher.Schedule(_pullTask);

            private void OnPush(ByteString data)
            {
                if (_downstreamStatus.Value is Ok)
                {
                    Push(_stage._out, data);
                    SendResponseIfNeeded();
                }
            }

            public Task WakeUp(IAdapterToStageMessage msg)
            {
                var p = new TaskCompletionSource<NotUsed>();
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
        private readonly Outlet<ByteString> _out = new Outlet<ByteString>("OutputStreamSource.out");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="writeTimeout">TBD</param>
        public OutputStreamSourceStage(TimeSpan writeTimeout)
        {
            _writeTimeout = writeTimeout;
            Shape = new SourceShape<ByteString>(_out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<ByteString> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.OutputStreamSource;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            // has to be in this order as module depends on shape
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greater than 0");

            var dataQueue = new BlockingCollection<ByteString>(maxBuffer);
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
    /// TBD
    /// </summary>
    internal class OutputStreamAdapter : Stream
    {
        #region not supported 

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="offset">TBD</param>
        /// <param name="origin">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        public override void SetLength(long value) => throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="count">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="NotSupportedException">TBD</exception>
        public override long Length => throw new NotSupportedException("This stream can only write");

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="NotSupportedException">TBD</exception>
        public override long Position
        {
            get => throw new NotSupportedException("This stream can only write");
            set => throw new NotSupportedException("This stream can only write");
        }

        #endregion

        private static readonly Exception PublisherClosedException = new IOException("Reactive stream is terminated, no writes are possible");
        
        private readonly BlockingCollection<ByteString> _dataQueue;
        private readonly AtomicReference<IDownstreamStatus> _downstreamStatus;
        private readonly IStageWithCallback _stageWithCallback;
        private readonly TimeSpan _writeTimeout;
        private bool _isActive = true;
        private bool _isPublisherAlive = true;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="dataQueue">TBD</param>
        /// <param name="downstreamStatus">TBD</param>
        /// <param name="stageWithCallback">TBD</param>
        /// <param name="writeTimeout">TBD</param>
        public OutputStreamAdapter(BlockingCollection<ByteString> dataQueue,
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

        private void SendData(ByteString data) => Send(() =>
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
        /// TBD
        /// </summary>
        public override void Flush() => SendMessage(OutputStreamSourceStage.Flush.Instance);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="count">TBD</param>
        public override void Write(byte[] buffer, int offset, int count)
            => SendData(ByteString.FromBytes(buffer, offset, count));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="disposing">TBD</param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            SendMessage(OutputStreamSourceStage.Close.Instance, false);
            _isActive = false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool CanRead => false;
        /// <summary>
        /// TBD
        /// </summary>
        public override bool CanSeek => false;
        /// <summary>
        /// TBD
        /// </summary>
        public override bool CanWrite => true;
    }
}
