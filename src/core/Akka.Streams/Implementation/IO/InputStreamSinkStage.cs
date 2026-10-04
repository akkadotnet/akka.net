//-----------------------------------------------------------------------
// <copyright file="InputStreamSinkStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using Akka.Pattern;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using static Akka.Streams.Implementation.IO.InputStreamSinkStage;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class InputStreamSinkStage : GraphStageWithMaterializedValue<SinkShape<ReadOnlySequence<byte>>, Stream>
    {
        #region internal classes

        /// <summary>
        /// Marker for messages sent from the materialized stream adapter to the stage.
        /// </summary>
        internal interface IAdapterToStageMessage
        {
        }

        /// <summary>
        /// Signals that the adapter consumed a queued element and the stage may pull again.
        /// </summary>
        internal sealed class ReadElementAcknowledgement : IAdapterToStageMessage
        {
            /// <summary>
            /// The shared read acknowledgement message.
            /// </summary>
            public static readonly ReadElementAcknowledgement Instance = new();

            private ReadElementAcknowledgement()
            {

            }
        }

        /// <summary>
        /// Requests completion of the stage when the materialized adapter is disposed.
        /// </summary>
        internal sealed class Close : IAdapterToStageMessage
        {
            /// <summary>
            /// The shared close message.
            /// </summary>
            public static readonly Close Instance = new();

            private Close()
            {

            }
        }

        /// <summary>
        /// Marker for data and lifecycle messages read by the materialized stream adapter.
        /// </summary>
        internal interface IStreamToAdapterMessage
        {
        }

        /// <summary>
        /// Carries a byte sequence from the stage to the materialized stream adapter.
        /// </summary>
        internal readonly struct Data : IStreamToAdapterMessage
        {
            /// <summary>
            /// The byte sequence supplied by the upstream stream.
            /// </summary>
            public readonly ReadOnlySequence<byte> Bytes;

            /// <summary>
            /// Creates a data message for the supplied byte sequence.
            /// </summary>
            /// <param name="bytes">The bytes to make available to the adapter.</param>
            public Data(ReadOnlySequence<byte> bytes)
            {
                Bytes = bytes;
            }
        }

        /// <summary>
        /// Signals that the upstream stream completed normally.
        /// </summary>
        internal sealed class Finished : IStreamToAdapterMessage
        {
            /// <summary>
            /// The shared upstream-completion message.
            /// </summary>
            public static readonly Finished Instance = new();

            private Finished()
            {

            }
        }

        /// <summary>
        /// Marks the start of the stage-to-adapter message sequence.
        /// </summary>
        internal sealed class Initialized : IStreamToAdapterMessage
        {
            /// <summary>
            /// The shared initialization message.
            /// </summary>
            public static readonly Initialized Instance = new();

            private Initialized()
            {

            }
        }

        /// <summary>
        /// Signals that the upstream stream failed.
        /// </summary>
        internal readonly struct Failed : IStreamToAdapterMessage
        {
            /// <summary>
            /// The upstream failure.
            /// </summary>
            public readonly Exception Cause;

            /// <summary>
            /// Creates a failure message for the upstream exception.
            /// </summary>
            /// <param name="cause">The failure to report to the adapter.</param>
            public Failed(Exception cause)
            {
                Cause = cause;
            }
        }

        /// <summary>
        /// Provides asynchronous callbacks from the materialized adapter to the stage.
        /// </summary>
        internal interface IStageWithCallback
        {
            /// <summary>
            /// Sends an adapter message to be handled on the stage callback.
            /// </summary>
            /// <param name="msg">The message to deliver to the stage.</param>
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

        private readonly Inlet<ReadOnlySequence<byte>> _in = new("InputStreamSink.in");
        private readonly TimeSpan _readTimeout;
        private BlockingCollection<IStreamToAdapterMessage> _dataQueue;

        /// <summary>
        /// Creates a sink stage that materializes a read-only stream backed by its upstream.
        /// </summary>
        /// <param name="readTimeout">The maximum time a blocking read waits for new data.</param>
        public InputStreamSinkStage(TimeSpan readTimeout)
        {
            _readTimeout = readTimeout;
            Shape = new SinkShape<ReadOnlySequence<byte>>(_in);
        }

        /// <summary>
        /// The default attributes for the input-stream sink.
        /// </summary>
        protected override Attributes InitialAttributes => DefaultAttributes.InputStreamSink;

        /// <summary>
        /// The sink shape that accepts byte sequences.
        /// </summary>
        public override SinkShape<ReadOnlySequence<byte>> Shape { get; }

        /// <summary>
        /// Creates stage logic and a read-only stream backed by a bounded queue of upstream elements.
        /// </summary>
        /// <param name="inheritedAttributes">Attributes inherited by this stage, including the input buffer size.</param>
        /// <exception cref="ArgumentException">Thrown when the maximum input buffer size is not positive.</exception>
        /// <returns>The stage logic and its materialized read-only stream.</returns>
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
    /// Read-only stream adapter that exposes byte sequences received by <see cref="InputStreamSinkStage"/>.
    /// </summary>
    internal sealed class InputStreamAdapter : Stream
    {
#region not supported 

        /// <summary>
        /// Flush is unsupported because this adapter is read-only.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports reading only.</exception>
        public override void Flush() => throw new NotSupportedException("This stream can only read");

        /// <summary>
        /// Seeking is unsupported because this adapter is not seekable.
        /// </summary>
        /// <param name="offset">The offset from the requested origin.</param>
        /// <param name="origin">The position used as the reference point for the offset.</param>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports reading only.</exception>
        /// <returns>This method does not return; it always throws.</returns>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException(
            "This stream can only read");

        /// <summary>
        /// Changing the stream length is unsupported because this adapter is read-only.
        /// </summary>
        /// <param name="value">The requested length.</param>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports reading only.</exception>
        public override void SetLength(long value) => throw new NotSupportedException("This stream can only read");

        /// <summary>
        /// Writing is unsupported because this adapter is read-only.
        /// </summary>
        /// <param name="buffer">The buffer that would receive the written bytes.</param>
        /// <param name="offset">The buffer offset at which writing would start.</param>
        /// <param name="count">The number of bytes that would be written.</param>
        /// <exception cref="NotSupportedException">Always thrown because the adapter supports reading only.</exception>
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException(
            "This stream can only read");

        /// <summary>
        /// Getting the stream length is unsupported because this adapter is not seekable.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown because the adapter does not expose a seekable length.</exception>
        public override long Length => throw new NotSupportedException("This stream can only read");

        /// <summary>
        /// Getting or setting the stream position is unsupported because this adapter is not seekable.
        /// </summary>
        /// <exception cref="NotSupportedException">Always thrown because the adapter does not expose a seekable position.</exception>
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
        private ReadOnlySequence<byte>? _detachedChunk;

        /// <summary>
        /// Creates an adapter that reads stage messages from a bounded queue.
        /// </summary>
        /// <param name="sharedBuffer">The queue containing initialization, data, and termination messages.</param>
        /// <param name="sendToStage">The callback used to acknowledge consumed data and request closure.</param>
        /// <param name="readTimeout">The maximum time to wait for queued data.</param>
        public InputStreamAdapter(BlockingCollection<IStreamToAdapterMessage> sharedBuffer,
            IStageWithCallback sendToStage, TimeSpan readTimeout)
        {
            _sharedBuffer = sharedBuffer;
            _sendToStage = sendToStage;
            _readTimeout = readTimeout;
        }

        /// <summary>
        /// Marks the adapter closed and asks the stage to complete.
        /// </summary>
        /// <param name="disposing">Whether managed resources should be disposed.</param>
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
        /// Reads one byte from the upstream stream, blocking until data arrives or the stream terminates.
        /// </summary>
        /// <exception cref="IllegalStateException">Thrown when the adapter receives messages before initialization.</exception>
        /// <exception cref="IOException">Thrown when the timed wait for initialization or the next queued message exceeds the configured timeout.</exception>
        /// <returns>The next byte as an unsigned value from 0 through 255, or -1 when the upstream has completed.</returns>
        public sealed override int ReadByte()
        {
            var a = new byte[1];
            return Read(a, 0, 1) != 0 ? a[0] & 0xff : -1;
        }

        /// <summary>
        /// Reads up to the requested number of bytes from the upstream stream.
        /// </summary>
        /// <param name="buffer">The buffer that receives the bytes.</param>
        /// <param name="offset">The zero-based offset in <paramref name="buffer"/> at which to store data.</param>
        /// <param name="count">The maximum number of bytes to read.</param>
        /// <exception cref="ArgumentException">Thrown when the buffer is empty, the offset is negative, the count is not positive, or the requested range extends past the buffer.</exception>
        /// <exception cref="IllegalStateException">Thrown when the adapter receives messages before initialization.</exception>
        /// <exception cref="IOException">Thrown when the timed wait for initialization or the next queued message exceeds the configured timeout.</exception>
        /// <returns>The number of bytes read, or zero when the upstream has completed.</returns>
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

                if (_detachedChunk.HasValue)
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
            if (!_detachedChunk.HasValue || _detachedChunk.Value.IsEmpty)
                throw new InvalidOperationException("Chunk must be pulled from shared buffer");

            var availableInChunk = _detachedChunk.Value.Length;
            var readBytes = GetData(buffer, offset, count, 0);

            if (readBytes >= availableInChunk)
                _sendToStage.WakeUp(ReadElementAcknowledgement.Instance);

            return readBytes;
        }

        private int GetData(byte[] buffer, int offset, int count, int gotBytes)
        {
            var chunk = GrabDataChunk();
            if (!chunk.HasValue)
                return gotBytes;

            var chunkValue = chunk.Value;
            var size = chunkValue.Length;
            if (size <= count)
            {
                var sizeInt = (int)size;
                chunkValue.CopyTo(buffer.AsSpan(offset, sizeInt));
                _detachedChunk = null;
                if (sizeInt == count)
                    return gotBytes + sizeInt;

                return GetData(buffer, offset + sizeInt, count - sizeInt, gotBytes + sizeInt);
            }

            chunkValue.Slice(0, count).CopyTo(buffer.AsSpan(offset, count));
            _detachedChunk = chunkValue.Slice(count);
            return gotBytes + count;
        }

        private ReadOnlySequence<byte>? GrabDataChunk()
        {
            if (_detachedChunk.HasValue)
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
        /// Indicates that this adapter supports reading.
        /// </summary>
        public override bool CanRead => true;

        /// <summary>
        /// Indicates that this adapter does not support seeking.
        /// </summary>
        public override bool CanSeek => false;

        /// <summary>
        /// Indicates that this adapter does not support writing.
        /// </summary>
        public override bool CanWrite => false;
    }
}
