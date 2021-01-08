//-----------------------------------------------------------------------
// <copyright file="InputStreamSinkStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// TBD
        /// </summary>
        internal interface IAdapterToStageMessage
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class ReadElementAcknowledgement : IAdapterToStageMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly ReadElementAcknowledgement Instance = new ReadElementAcknowledgement();

            private ReadElementAcknowledgement()
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
        internal interface IStreamToAdapterMessage
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal struct Data : IStreamToAdapterMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly ByteString Bytes;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="bytes">TBD</param>
            public Data(ByteString bytes)
            {
                Bytes = bytes;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Finished : IStreamToAdapterMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Finished Instance = new Finished();

            private Finished()
            {

            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Initialized : IStreamToAdapterMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static readonly Initialized Instance = new Initialized();

            private Initialized()
            {

            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal struct Failed : IStreamToAdapterMessage
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly Exception Cause;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cause">TBD</param>
            public Failed(Exception cause)
            {
                Cause = cause;
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
            void WakeUp(IAdapterToStageMessage msg);
        }

        private sealed class Logic : InGraphStageLogic, IStageWithCallback
        {
            private readonly InputStreamSinkStage _stage;
            private readonly Action<IAdapterToStageMessage> _callback;
            private bool _completionSignalled;

            public Logic(InputStreamSinkStage stage) : base(stage.Shape)
            {
                _stage = stage;
                _callback = GetAsyncCallback((IAdapterToStageMessage message) =>
                {
                    if (message is ReadElementAcknowledgement)
                        SendPullIfAllowed();
                    else if (message is Close)
                        CompleteStage();
                });

                SetHandler(stage._in, this);
            }

            public override void OnPush()
            {
                //1 is buffer for Finished or Failed callback
                if (_stage._dataQueue.Count + 1 == _stage._dataQueue.BoundedCapacity)
                    throw new BufferOverflowException("Queue is full");

                _stage._dataQueue.Add(new Data(Grab(_stage._in)));
                if (_stage._dataQueue.BoundedCapacity - _stage._dataQueue.Count > 1)
                    SendPullIfAllowed();
            }

            public override void OnUpstreamFinish()
            {
                _stage._dataQueue.Add(Finished.Instance);
                _completionSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception ex)
            {
                _stage._dataQueue.Add(new Failed(ex));
                _completionSignalled = true;
                FailStage(ex);
            }

            public override void PreStart()
            {
                _stage._dataQueue.Add(Initialized.Instance);
                Pull(_stage._in);
            }

            public override void PostStop()
            {
                if (!_completionSignalled)
                    _stage._dataQueue.Add(new Failed(new AbruptStageTerminationException(this)));
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="readTimeout">TBD</param>
        public InputStreamSinkStage(TimeSpan readTimeout)
        {
            _readTimeout = readTimeout;
            Shape = new SinkShape<ByteString>(_in);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes => DefaultAttributes.InputStreamSink;

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<ByteString> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the maximum size of the input buffer is less than or equal to zero.
        /// </exception>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Stream> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer size must be greater than 0");

            _dataQueue = new BlockingCollection<IStreamToAdapterMessage>(maxBuffer + 2);

            var logic = new Logic(this);
            return new LogicAndMaterializedValue<Stream>(logic,
                new InputStreamAdapter(_dataQueue, logic, _readTimeout));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// InputStreamAdapter that interacts with InputStreamSinkStage
    /// </summary>
    internal class InputStreamAdapter : Stream
    {
#region not supported 

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="NotSupportedException">TBD</exception>
        public override void Flush() => throw new NotSupportedException("This stream can only read");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="offset">TBD</param>
        /// <param name="origin">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(
            "This stream can only read");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="value">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override void SetLength(long value) => throw new NotSupportedException("This stream can only read");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="count">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(
            "This stream can only read");

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="NotSupportedException">TBD</exception>
        public override long Length => throw new NotSupportedException("This stream can only read");

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="NotSupportedException">TBD</exception>
        public override long Position
        {
            get => throw new NotSupportedException("This stream can only read");
            set => throw new NotSupportedException("This stream can only read");
        }

#endregion
        
        private static readonly Exception SubscriberClosedException =
            new IOException("Reactive stream is terminated, no reads are possible");

        private readonly BlockingCollection<IStreamToAdapterMessage> _sharedBuffer;
        private readonly IStageWithCallback _sendToStage;
        private readonly TimeSpan _readTimeout;
        private bool _isActive = true;
        private bool _isStageAlive = true;
        private bool _isInitialized;
        private ByteString _detachedChunk;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sharedBuffer">TBD</param>
        /// <param name="sendToStage">TBD</param>
        /// <param name="readTimeout">TBD</param>
        public InputStreamAdapter(BlockingCollection<IStreamToAdapterMessage> sharedBuffer,
            IStageWithCallback sendToStage, TimeSpan readTimeout)
        {
            _sharedBuffer = sharedBuffer;
            _sendToStage = sendToStage;
            _readTimeout = readTimeout;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="disposing">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when an <see cref="Initialized"/> message is not the first message.
        /// </exception>
        /// <exception cref="IOException">
        /// This exception is thrown when a timeout occurs waiting on new data.
        /// </exception>
        /// <returns>TBD</returns>
        public sealed override int ReadByte()
        {
            var a = new byte[1];
            return Read(a, 0, 1) != 0 ? a[0] & 0xff : -1;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="buffer">TBD</param>
        /// <param name="offset">TBD</param>
        /// <param name="count">TBD</param>
        /// <exception cref="ArgumentException">TBD
        /// This exception is thrown for a number of reasons. These include:
        /// <ul>
        /// <li>the specified <paramref name="buffer"/> size is less than or equal to zero</li>
        /// <li>the specified <paramref name="buffer"/> size is less than the combination of <paramref name="offset"/> and <paramref name="count"/></li>
        /// <li>the specified <paramref name="offset"/> is less than zero</li>
        /// <li>the specified <paramref name="count"/> is less than or equal to zero</li>
        /// </ul>
        /// </exception>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when an <see cref="Initialized"/> message is not the first message.
        /// </exception>
        /// <exception cref="IOException">
        /// This exception is thrown when a timeout occurs waiting on new data.
        /// </exception>
        /// <returns>TBD</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer.Length <= 0) throw new ArgumentException("array size must be > 0", nameof(buffer));
            if (offset < 0) throw new ArgumentException("offset must be >= 0", nameof(offset));
            if (count <= 0) throw new ArgumentException("count must be > 0", nameof(count));
            if (offset + count > buffer.Length)
                throw new ArgumentException("offset + count must be smaller or equal to the array length");

            return ExecuteIfNotClosed(() =>
            {
                if (!_isStageAlive)
                    return 0;

                if (_detachedChunk != null)
                    return ReadBytes(buffer, offset, count);

                var success = _sharedBuffer.TryTake(out var msg, _readTimeout);
                if (!success)
                    throw new IOException("Timeout on waiting for new data");

                if (msg is Data data)
                {
                    _detachedChunk = data.Bytes;
                    return ReadBytes(buffer, offset, count);
                }
                if (msg is Finished)
                {
                    _isStageAlive = false;
                    return 0;
                }
                if (msg is Failed failed)
                {
                    _isStageAlive = false;
                    throw failed.Cause;
                }

                throw new IllegalStateException("message 'Initialized' must come first");
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
            if (_isInitialized)
                return;

            if (_sharedBuffer.TryTake(out var message, _readTimeout))
            {
                if (message is Initialized)
                    _isInitialized = true;
                else
                    throw new IllegalStateException("First message must be Initialized notification");
            }
            else
                throw new IOException($"Timeout after {_readTimeout} waiting  Initialized message from stage");
        }

        private int ReadBytes(byte[] buffer, int offset, int count)
        {
            if (_detachedChunk == null || _detachedChunk.IsEmpty)
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
            _detachedChunk = chunk.Slice(count);
            return gotBytes + count;
        }

        private ByteString GrabDataChunk()
        {
            if (_detachedChunk != null)
                return _detachedChunk;

            var chunk = _sharedBuffer.Take();
            if (chunk is Data data)
            {
                _detachedChunk = data.Bytes;
                return _detachedChunk;
            }
            if (chunk is Finished)
                _isStageAlive = false;

            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// TBD
        /// </summary>
        public override bool CanWrite => false;
    }
}
