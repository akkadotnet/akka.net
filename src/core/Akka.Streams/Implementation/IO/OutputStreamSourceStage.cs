using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.IO;
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

        internal interface IAdapterToStageMessage { }
        internal struct Flush : IAdapterToStageMessage { }
        internal struct Close : IAdapterToStageMessage { }

        internal interface IDownstreamStatus { }
        internal struct Ok : IDownstreamStatus { }
        internal struct Canceled : IDownstreamStatus { }

        internal interface IStageWithCallback
        {
            Task WakeUp(IAdapterToStageMessage msg);
        }

        private sealed class OutputStreamSourceStageLogic : GraphStageLogic, IStageWithCallback
        {
            private readonly OutputStreamSourceStage _stage;
            private TaskCompletionSource<Unit> _flush;
            private TaskCompletionSource<Unit> _close;

            public OutputStreamSourceStageLogic(Shape shape, OutputStreamSourceStage stage) : base(shape)
            {
                _stage = stage;

                SetHandler(_stage._out, onPull: OnPull, onDownstreamFinish: OnDownstreamFinish);
            }

            private void OnDownstreamFinish()
            {
                //assuming there can be no further in messages
                _stage._downstreamStatus.Value = new Canceled();
                _stage._dataQueue = null;
                CompleteStage();
            }

            private void OnPull()
            {
                try
                {
                    OnPush(_stage._dataQueue.Take());
                }
                catch (Exception ex)
                {
                    FailStage(ex);
                }
            }

            private void OnPush(ByteString data)
            {
                if (_stage._downstreamStatus.Value is Ok)
                {
                    Push(_stage._out, data);
                    SendResponseIfNeeded();
                }
            }

            public Task WakeUp(IAdapterToStageMessage msg)
            {
                var p = new TaskCompletionSource<Unit>();
                UpstreamCallback(new Tuple<IAdapterToStageMessage, TaskCompletionSource<Unit>>(msg, p));
                return p.Task;
            }

            private void UpstreamCallback(Tuple<IAdapterToStageMessage, TaskCompletionSource<Unit>> @event)
            {
                if (@event.Item1 is Flush)
                {
                    _flush = @event.Item2;
                    SendResponseIfNeeded();
                }
                else if (@event.Item1 is Close)
                {
                    _close = @event.Item2;
                    if (_stage._dataQueue.Count == 0)
                    {
                        _stage._downstreamStatus.Value = new Canceled();
                        CompleteStage();
                        UnblockUpsteam();
                    }
                    else
                        SendResponseIfNeeded();
                }
            }

            private void UnblockUpsteam()
            {
                if (_flush != null)
                {
                    _flush.TrySetResult(Unit.Instance);
                    _flush = null;
                    return;
                }

                if (_close == null)
                    return;

                _close.TrySetResult(Unit.Instance);
                _close = null;
            }

            private void SendResponseIfNeeded()
            {
                if (_stage._downstreamStatus.Value is Canceled || _stage._dataQueue.Count == 0)
                    UnblockUpsteam();
            }
        }

        #endregion

        private readonly TimeSpan _writeTimeout;
        private readonly AtomicReference<IDownstreamStatus> _downstreamStatus = new AtomicReference<IDownstreamStatus>(new Ok());
        private readonly Outlet<ByteString> _out = new Outlet<ByteString>("OutputStreamSource.out");
        private readonly SourceShape<ByteString> _shape;
        private BlockingCollection<ByteString> _dataQueue;

        public OutputStreamSourceStage(TimeSpan writeTimeout)
        {
            _writeTimeout = writeTimeout;
            _shape = new SourceShape<ByteString>(_out);
            // has to be in this order as module depends on shape
            var maxBuffer = Module.Attributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greather than 0");

            _dataQueue = new BlockingCollection<ByteString>(maxBuffer);
        }

        public override SourceShape<ByteString> Shape => _shape;

        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new OutputStreamSourceStageLogic(Shape, this);
            return new LogicAndMaterializedValue<Stream>(logic,
                new OutputStreamAdapter(_dataQueue, _downstreamStatus, logic, _writeTimeout));
        }
    }

    internal class OutputStreamAdapter : Stream
    {
        #region not supported 

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("This stream can only write");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("This stream can only write");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("This stream can only write");
        }

        public override long Length
        {
            get
            {
                throw new NotSupportedException("This stream can only write");
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException("This stream can only write");
            }
            set
            {
                throw new NotSupportedException("This stream can only write");
            }
        }

        #endregion

        private static readonly Exception PublisherClosedException = new IOException("Reactive stream is terminated, no writes are possible");
        
        private readonly BlockingCollection<ByteString> _dataQueue;
        private readonly AtomicReference<IDownstreamStatus> _downstreamStatus;
        private readonly IStageWithCallback _stageWithCallback;
        private readonly TimeSpan _writeTimeout;
        private bool _isActive = true;
        private bool _isPublisherAlive = true;

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

        private void SendData(ByteString data)
        {
            Send(() =>
            {
                _dataQueue.Add(data);

                if (_downstreamStatus.Value is Canceled)
                {
                    _isPublisherAlive = false;
                    throw PublisherClosedException;
                }
            });
        }

        private void SendMessage(IAdapterToStageMessage msg, bool handleCancelled = true)
        {
            Send(() =>
            {
                _stageWithCallback.WakeUp(msg).Wait(_writeTimeout);
                if (_downstreamStatus.Value is Canceled && handleCancelled)
                {
                    //Publisher considered to be terminated at earliest convenience to minimize messages sending back and forth
                    _isPublisherAlive = false;
                    throw PublisherClosedException;
                }
            });
        }

        public override void Flush() => SendMessage(new Flush());

        public override void Write(byte[] buffer, int offset, int count)
            => SendData(ByteString.Create(buffer, offset, count));

        public override void Close()
        {
            SendMessage(new Close(), false);
            _isActive = false;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
    }
}
