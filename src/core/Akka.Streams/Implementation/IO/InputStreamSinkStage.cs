//-----------------------------------------------------------------------
// <copyright file="InputStreamSinkStage.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using Akka.IO;
using Akka.Pattern;
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

        internal class ReadElementAcknowledgement : IAdapterToStageMessage
        {
            public static readonly ReadElementAcknowledgement Instance = new ReadElementAcknowledgement();

            private ReadElementAcknowledgement()
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

        internal interface IStreamToAdapterMessage { }

        internal struct Data : IStreamToAdapterMessage
        {
            public readonly ByteString Bytes;

            public Data(ByteString bytes)
            {
                Bytes = bytes;
            }
        }

        internal class Finished : IStreamToAdapterMessage
        {
            public static readonly Finished Instance = new Finished();

            private Finished()
            {

            }
        }

        internal class Initialized : IStreamToAdapterMessage
        {
            public static readonly Initialized Instance = new Initialized();

            private Initialized()
            {

            }
        }

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

        private sealed class Logic : GraphStageLogic, IStageWithCallback
        {
            private readonly InputStreamSinkStage _stage;
            private readonly Action<IAdapterToStageMessage> _callback;

            public Logic(InputStreamSinkStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _callback = GetAsyncCallback((IAdapterToStageMessage messagae) =>
                {
                    if(messagae is ReadElementAcknowledgement)
                        SendPullIfAllowed();
                    else if (messagae is Close)
                        CompleteStage();
                });

                SetHandler(stage._in, onPush: OnPush, 
                    onUpstreamFinish: OnUpstreamFinish,
                    onUpstreamFailure: OnUpstreamFailure);
            }

            private void OnPush()
            {
                //1 is buffer for Finished or Failed callback
                if (_stage._dataQueue.Count + 1 == _stage._dataQueue.BoundedCapacity)
                    throw new BufferOverflowException("Queue is full");
                
                _stage._dataQueue.Add(new Data(Grab(_stage._in)));
                if (_stage._dataQueue.BoundedCapacity - _stage._dataQueue.Count > 1)
                    SendPullIfAllowed();
            }

            private void OnUpstreamFinish()
            {
                _stage._dataQueue.Add(Finished.Instance);
                CompleteStage();
            }

            private void OnUpstreamFailure(Exception ex)
            {
                _stage._dataQueue.Add(new Failed(ex));
                FailStage(ex);
            }

            public override void PreStart()
            {
                _stage._dataQueue.Add(Initialized.Instance);
                Pull(_stage._in);
            }

            public void WakeUp(IAdapterToStageMessage msg) => _callback(msg);

            private void SendPullIfAllowed()
            {
                if (_stage._dataQueue.BoundedCapacity - _stage._dataQueue.Count > 1 && !HasBeenPulled(_stage._in))
                    Pull(_stage._in);
            }
        }

        #endregion

        private readonly Inlet<ByteString> _in = new Inlet<ByteString>("InputStreamSink.in");
        private readonly TimeSpan _readTimeout;
        private BlockingCollection<IStreamToAdapterMessage> _dataQueue;

        public InputStreamSinkStage(TimeSpan readTimeout)
        {
            _readTimeout = readTimeout;
            Shape = new SinkShape<ByteString>(_in);
        }

        protected override Attributes InitialAttributes => DefaultAttributes.InputStreamSink;

        public override SinkShape<ByteString> Shape { get; }

        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greather than 0");

            _dataQueue = new BlockingCollection<IStreamToAdapterMessage>(maxBuffer + 2);

            var logic = new Logic(this);
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
        private bool _isInitialized;
        private ByteString _detachedChunk;

        public InputStreamAdapter(BlockingCollection<IStreamToAdapterMessage> sharedBuffer, IStageWithCallback sendToStage, TimeSpan readTimeout)
        {
            _sharedBuffer = sharedBuffer;
            _sendToStage = sendToStage;
            _readTimeout = readTimeout;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            ExecuteIfNotClosed(() =>
            {
                // at this point Subscriber may be already terminated
                if (_isStageAlive)
                    _sendToStage.WakeUp(InputStreamSinkStage.Close.Instance);

                _isActive = false;
                return NotUsed.Instance;
            });
        }

        public sealed override int ReadByte()
        {
            var a = new byte[1];
            return Read(a, 0, 1) != 0 ? a[0] : -1;
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
                    return 0;

                if (_detachedChunk != null)
                    return ReadBytes(buffer, offset, count);

                IStreamToAdapterMessage msg;
                var success = _sharedBuffer.TryTake(out msg, _readTimeout);
                if (!success)
                    throw new IOException("Timeout on waiting for new data");

                if (msg is Data)
                {
                    _detachedChunk = ((Data) msg).Bytes;
                    return ReadBytes(buffer, offset, count);
                }
                if (msg is Finished)
                {
                    _isStageAlive = false;
                    return 0;
                }

                _isStageAlive = false;
                throw ((Failed) msg).Cause;
            });
        }

        private T ExecuteIfNotClosed<T>(Func<T> f)
        {
            if (_isActive)
            {
                WaitIfNotInitialized();
                return f();
            }
            throw SubscriberClosedException;
        }

        private void WaitIfNotInitialized()
        {
            if (!_isInitialized)
            {
                IStreamToAdapterMessage message;
                _sharedBuffer.TryTake(out message, _readTimeout);
                if (message is Initialized)
                    _isInitialized = true;
                else
                    throw new IllegalStateException("First message must be Initialized notification");
            }
        }

        private int ReadBytes(byte[] buffer, int offset, int count)
        {
            if(_detachedChunk == null || _detachedChunk.IsEmpty)
                throw new InvalidOperationException("Chunk must be pulled from shared buffer");

            var availableInChunk = _detachedChunk.Count;
            var readBytes = GetData(buffer, offset, count, 0);

            if (readBytes >= availableInChunk)
                _sendToStage.WakeUp(ReadElementAcknowledgement.Instance);

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
                Array.Copy(chunk.ToArray(), 0, buffer, offset, size);
                _detachedChunk = null;
                if (size == count)
                    return gotBytes + size;

                return GetData(buffer, offset + size, count - size, gotBytes + size);
            }

            Array.Copy(chunk.ToArray(), 0, buffer, offset, count);
            _detachedChunk = chunk.Drop(count);
            return gotBytes + count;
        }

        private ByteString GrabDataChunk()
        {
            if (_detachedChunk != null)
                return _detachedChunk;

            var chunk = _sharedBuffer.Take();
            if (chunk is Data)
            {
                _detachedChunk = ((Data) chunk).Bytes;
                return _detachedChunk;
            }
            if (chunk is Finished)
                _isStageAlive = false;

            return null;
        }
        
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
    }
}
