using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Akka.IO;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using static Akka.Streams.Implementation.IO.InputStreamSinkStage;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class InputStreamSinkStage : GraphStageWithMaterializedValue<SinkShape<ByteString>, Stream>
    {
        #region internal classes

        internal interface IAdapterToStageMessage { }
        internal struct ReadElementAcknowledgement : IAdapterToStageMessage { }
        internal struct Close : IAdapterToStageMessage { }

        internal interface IStreamToAdapterMessage { }
        internal struct Data : IStreamToAdapterMessage
        {
            public readonly ByteString Bytes;

            public Data(ByteString bytes)
            {
                Bytes = bytes;
            }
        }
        internal struct Finished : IStreamToAdapterMessage { }
        internal struct Failed : IStreamToAdapterMessage
        {
            public readonly Exception Cause;

            public Failed(Exception cause)
            {
                Cause = cause;
            }
        }

        internal interface IStageWithCallback
        {
            void WakeUp(IAdapterToStageMessage msg);
        }

        private sealed class InputStreamSinkStageLogic : GraphStageLogic, IStageWithCallback
        {
            private readonly InputStreamSinkStage _stage;
            private bool _pullRequestIsSent = true;

            public InputStreamSinkStageLogic(Shape shape, InputStreamSinkStage stage) : base(shape)
            {
                _stage = stage;

                SetHandler(stage._in, onPush: OnPush, onUpstreamFailure: OnUpstreamFailure,
                    onUpstreamFinish: OnUpstreamFinish);
            }

            private void OnPush()
            {
                //1 is buffer for Finished or Failed callback
                if (_stage._dataQueue.Count + 1 == _stage._dataQueue.BoundedCapacity)
                    throw new BufferOverflowException("Queue is full");

                _pullRequestIsSent = false;
                _stage._dataQueue.Add(new Data(Grab(_stage._in)));
                if (_stage._dataQueue.Count + 1 < _stage._dataQueue.BoundedCapacity)
                    SendPullIfAllowed();
            }

            private void OnUpstreamFailure(Exception ex)
            {
                _stage._dataQueue.Add(new Failed(ex));
                FailStage(ex);
            }

            private void OnUpstreamFinish()
            {
                _stage._dataQueue.Add(new Finished());
                CompleteStage();
            }

            public override void PreStart() => Pull(_stage._in);

            public void WakeUp(IAdapterToStageMessage msg)
            {
                if (msg is ReadElementAcknowledgement)
                    SendPullIfAllowed();
                else if (msg is Close)
                    CompleteStage();
            }

            private void SendPullIfAllowed()
            {
                if (!_pullRequestIsSent)
                {
                    _pullRequestIsSent = true;
                    Pull(_stage._in);
                }
            }
        }

        #endregion

        private readonly Inlet<ByteString> _in = new Inlet<ByteString>("InputStreamSink.in");
        private readonly BlockingCollection<IStreamToAdapterMessage> _dataQueue;
        private readonly SinkShape<ByteString> _shape;
        private readonly TimeSpan _readTimeout;
        
        public InputStreamSinkStage(TimeSpan readTimeout)
        {
            _readTimeout = readTimeout;
            _shape = new SinkShape<ByteString>(_in);
            //has to be in this order as module depends on shape
            var maxBuffer = Module.Attributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greather than 0");

            _dataQueue = new BlockingCollection<IStreamToAdapterMessage>(maxBuffer + 1);
        }

        protected override Attributes InitialAttributes => DefaultAttributes.InputStreamSink;
        public override SinkShape<ByteString> Shape => _shape;

        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greather than 0");

            var logic = new InputStreamSinkStageLogic(Shape, this);
            return new LogicAndMaterializedValue<Stream>(logic, new InputStreamAdapter(_dataQueue, logic, _readTimeout));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// InputStreamAdapter that interacts with InputStreamSinkStage
    /// </summary>
    internal class InputStreamAdapter : Stream
    {
        #region not supported 

        public override void Flush()
        {
            throw new NotSupportedException("This stream can only read");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("This stream can only read");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("This stream can only read");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("This stream can only read");
        }

        public override long Length
        {
            get
            {
                throw new NotSupportedException("This stream can only read");
            }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException("This stream can only read");
            }
            set
            {
                throw new NotSupportedException("This stream can only read");
            }
        }
        #endregion

        private static readonly Exception SubscriberClosedException = new IOException("Reactive stream is terminated, no reads are possible");

        private readonly BlockingCollection<IStreamToAdapterMessage> _sharedBuffer;
        private readonly IStageWithCallback _sendToStage;
        private readonly TimeSpan _readTimeout;
        private bool _isActive = true;
        private bool _isStageAlive = true;
        private ByteString _detachedChunk;

        public InputStreamAdapter(BlockingCollection<IStreamToAdapterMessage> sharedBuffer, IStageWithCallback sendToStage, TimeSpan readTimeout)
        {
            _sharedBuffer = sharedBuffer;
            _sendToStage = sendToStage;
            _readTimeout = readTimeout;
        }

        public override void Close()
        {
            // at this point Subscriber may be already terminated
            if (_isStageAlive)
                _sendToStage.WakeUp(new Close());

            _isActive = false;
        }

        public sealed override int ReadByte()
        {
            var a = new byte[1];
            return Read(a, 0, 1) != -1 ? a[0] : -1;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer.Length <= 0) throw new ArgumentException("array size must be > 0");
            if (offset < 0) throw new ArgumentException("offset must be >= 0");
            if (count <= 0) throw new ArgumentException("count must be > 0");
            if (offset + count > buffer.Length) throw new ArgumentException("offset + count must be smaller or equal to the array length");

            return ExecuteIfNotClosed(() =>
            {
                if (!_isStageAlive)
                    return -1;

                if (_detachedChunk != null)
                    return ReadBytes(buffer, offset, count);
                
                try
                {
                    IStreamToAdapterMessage msg;
                    var success = _sharedBuffer.TryTake(out msg, _readTimeout);
                    if (!success)
                        throw new IOException("Timeout on waiting for new data");

                    if (msg is Data)
                    {
                        _detachedChunk = ((Data)msg).Bytes;
                        return ReadBytes(buffer, offset, count);
                    }
                    if (msg is Finished)
                    {
                        _isStageAlive = false;
                        return -1;
                    }

                    _isStageAlive = false;
                    throw new IOException(string.Empty, ((Failed)msg).Cause);
                }
                catch (ThreadInterruptedException ex)
                {
                    throw new IOException(string.Empty, ex);
                }
            });
        }

        private T ExecuteIfNotClosed<T>(Func<T> f)
        {
            if (_isActive)
                return f();
            throw SubscriberClosedException;
        }

        private int ReadBytes(byte[] buffer, int offset, int count)
        {
            if(_detachedChunk == null || _detachedChunk.IsEmpty)
                throw new InvalidOperationException("Chunk must be pulled from shared buffer");

            var availableInChunk = _detachedChunk.Count;
            var readBytes = GetData(buffer, offset, count, 0);

            if (readBytes >= availableInChunk)
                _sendToStage.WakeUp(new ReadElementAcknowledgement());

            return readBytes;
        }

        private int GetData(byte[] buffer, int offset, int count, int gotBytes)
        {
            var chunk = GrabDataChunk();
            if (chunk == null)
                return gotBytes;

            var size = chunk.Count;
            if (size <= count)
            {
                Array.Copy(chunk.ToArray(), 0, buffer, offset, Math.Min(size, count));
                _detachedChunk = null;
                if (size == count)
                    return gotBytes + size;

                return GetData(buffer, offset + size, count - size, gotBytes + size);
            }

            chunk.Take(count).ToArray().CopyTo(buffer, offset);
            _detachedChunk = chunk.Drop(count);
            return gotBytes + count;
        }

        private ByteString GrabDataChunk()
        {
            if (_detachedChunk != null)
                return _detachedChunk;

            var chunk = _sharedBuffer.Take();
            if (chunk is Data)
                _detachedChunk = ((Data) chunk).Bytes;

            return _detachedChunk;
        }
        
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
    }
}
