using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reactive.Streams;
using System.Threading.Tasks;
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

        internal interface IAdapterToStageMessage { }

        internal class Flush : IAdapterToStageMessage
        {
            public static readonly Flush Instance = new Flush();

            private Flush()
            {
                
            }
        }

        internal class Close : IAdapterToStageMessage
        {
            public static readonly Close Instance = new Close();

            private Close()
            {

            }
        }

        internal interface IDownstreamStatus { }

        internal class Ok : IDownstreamStatus
        {
            public static readonly Ok Instance = new Ok();

            private Ok()
            {

            }
        }

        internal class Canceled : IDownstreamStatus
        {
            public static readonly Canceled Instance = new Canceled();

            private Canceled()
            {

            }
        }

        internal interface IStageWithCallback
        {
            Task WakeUp(IAdapterToStageMessage msg);
        }

        private sealed class Logic : GraphStageLogic, IStageWithCallback
        {
            private readonly OutputStreamSourceStage _stage;
            private TaskCompletionSource<Unit> _flush;
            private TaskCompletionSource<Unit> _close;
            private Action<Either<ByteString, Exception>> _downstreamCallback;
            private Action<Tuple<IAdapterToStageMessage, TaskCompletionSource<Unit>>> _upstreamCallback;

            public Logic(OutputStreamSourceStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _downstreamCallback = GetAsyncCallback((Either<ByteString, Exception> result) =>
                {
                    if(result.IsLeft)
                        OnPush(result.Value as ByteString);
                    else
                        FailStage(result.Value as Exception);
                });
                _upstreamCallback = GetAsyncCallback<Tuple<IAdapterToStageMessage, TaskCompletionSource<Unit>>>(OnAsyncMessage);
                SetHandler(_stage._out, onPull: OnPull, onDownstreamFinish: OnDownstreamFinish);
            }

            private void OnDownstreamFinish()
            {
                //assuming there can be no further in messages
                _stage._downstreamStatus.Value = Canceled.Instance;
                _stage._dataQueue = null;
                CompleteStage();
            }

            private void OnPull()
            {
                Interpreter.Materializer.ExecutionContext.Schedule(() =>
                {
                    try
                    {
                        _downstreamCallback(new Left<ByteString, Exception>(_stage._dataQueue.Take()));
                    }
                    catch (Exception ex)
                    {
                        _downstreamCallback(new Right<ByteString, Exception>(ex));
                    }
                });
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
                _upstreamCallback(new Tuple<IAdapterToStageMessage, TaskCompletionSource<Unit>>(msg, p));
                return p.Task;
            }

            private void OnAsyncMessage(Tuple<IAdapterToStageMessage, TaskCompletionSource<Unit>> @event)
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
                        _stage._downstreamStatus.Value = Canceled.Instance;
                        CompleteStage();
                        UnblockUpsteam();
                    }
                    else
                        SendResponseIfNeeded();
                }
            }

            private bool UnblockUpsteam()
            {
                if (_flush != null)
                {
                    _flush.TrySetResult(Unit.Instance);
                    _flush = null;
                    return true;
                }

                if (_close == null)
                    return false;

                _close.TrySetResult(Unit.Instance);
                _close = null;
                return true;
            }

            private void SendResponseIfNeeded()
            {
                if (_stage._downstreamStatus.Value is Canceled || _stage._dataQueue.Count == 0)
                    UnblockUpsteam();
            }
        }

        #endregion

        private readonly TimeSpan _writeTimeout;
        private readonly AtomicReference<IDownstreamStatus> _downstreamStatus = new AtomicReference<IDownstreamStatus>(Ok.Instance);
        private readonly Outlet<ByteString> _out = new Outlet<ByteString>("OutputStreamSource.out");
        private BlockingCollection<ByteString> _dataQueue;

        public OutputStreamSourceStage(TimeSpan writeTimeout)
        {
            _writeTimeout = writeTimeout;
            Shape = new SourceShape<ByteString>(_out);
        }

        public override SourceShape<ByteString> Shape { get; }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.OutputStreamSource;

        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            // has to be in this order as module depends on shape
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greather than 0");

            _dataQueue = new BlockingCollection<ByteString>(maxBuffer);

            var logic = new Logic(this);
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

        public override void Flush() => SendMessage(OutputStreamSourceStage.Flush.Instance);

        public override void Write(byte[] buffer, int offset, int count)
            => SendData(ByteString.Create(buffer, offset, count));

        public override void Close()
        {
            SendMessage(OutputStreamSourceStage.Close.Instance, false);
            _isActive = false;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
    }
}
