//-----------------------------------------------------------------------
// <copyright file="FileSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.Streams.Actors;
using Akka.Streams.IO;
using Akka.Util;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class FileSubscriber : ActorSubscriber
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="f">TBD</param>
        /// <param name="completionPromise">TBD</param>
        /// <param name="bufferSize">TBD</param>
        /// <param name="startPosition">TBD</param>
        /// <param name="fileMode">TBD</param>
        /// <param name="autoFlush"></param>
        /// <param name="flushCommand"></param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
        public static Props Props(
            FileInfo f,
            TaskCompletionSource<IOResult> completionPromise,
            int bufferSize,
            long startPosition,
            FileMode fileMode,
            bool autoFlush = false,
            FlushSignaler flushCommand = null)
        {
            if (bufferSize <= 0)
                throw new ArgumentException($"bufferSize must be > 0 (was {bufferSize})", nameof(bufferSize));
            if (startPosition < 0)
                throw new ArgumentException($"startPosition must be >= 0 (was {startPosition})", nameof(startPosition));

            return Actor.Props.Create(() => new FileSubscriber(f, completionPromise, bufferSize, startPosition, fileMode, autoFlush, flushCommand))
                .WithDeploy(Deploy.Local);
        }

        private readonly FileInfo _f;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly CancellationTokenSource _cts;
        private readonly long _startPosition;
        private readonly FileMode _fileMode;
        private readonly ILoggingAdapter _log;
        private readonly FileStreamRequestStrategy _requestStrategy;
        private readonly bool _autoFlush;
        private readonly int _fileBufferSize;
        private FileStream _chan;
        private long _bytesWritten;        
        private Exception _upstreamError;
        private Task _fileTask = null;
        private ByteString _bufferedByteString = ByteString.Empty;
        private bool _flushRequested = false;

        private const int DefaultFileBufferSize = 4 * 1024; //regular file buffer size

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="f">TBD</param>
        /// <param name="completionPromise">TBD</param>
        /// <param name="bufferSize">TBD</param>
        /// <param name="startPosition">TBD</param>
        /// <param name="fileMode">TBD</param>
        /// <param name="autoFlush"></param>
        /// <param name="flushSignaler"></param>
        public FileSubscriber(
            FileInfo f,
            TaskCompletionSource<IOResult> completionPromise,
            int bufferSize,
            long startPosition,
            FileMode fileMode,
            bool autoFlush,
            FlushSignaler flushSignaler)
        {
            _cts = new CancellationTokenSource();
            _f = f;
            _completionPromise = completionPromise;
            _startPosition = startPosition;
            _fileMode = fileMode;
            _autoFlush = autoFlush;
            _log = Context.GetLogger();
            _requestStrategy = new FileStreamRequestStrategy(bufferSize);
            _fileBufferSize = DefaultFileBufferSize; //todo make configurable

            if (flushSignaler != null)
                flushSignaler.FileSubscriber = Self;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IRequestStrategy RequestStrategy => _requestStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            try
            {
                _chan = new FileStream(_f.ToString(), _fileMode, FileAccess.Write, FileShare.ReadWrite, _fileBufferSize, true);
                if (_startPosition > 0)
                    _chan.Position = _startPosition;
            }
            catch (Exception ex)
            {
                _completionPromise.SetException(ex);
                Cancel();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case OnNext next when _fileTask is null:
                    {
                        var bytes = (ByteString)next.Element;
                        if (_bufferedByteString.Count > 0)
                        {
                            bytes = _bufferedByteString.Concat(bytes);
                            _bufferedByteString = ByteString.Empty;
                            _requestStrategy.FileBufferCount = 0;
                        }
                        _fileTask = WriteAsync(Self, bytes, _autoFlush);
                    }
                    return true;
                case OnNext next:
                    //stream is busy
                    _bufferedByteString = _bufferedByteString.Concat((ByteString)next.Element);
                    _requestStrategy.FileBufferCount = _bufferedByteString.Count / _fileBufferSize;
                    return true;
                case Status.Success msg: //write completed
                    {
                        var bytes = (ByteString)msg.Status;
                        _bytesWritten += bytes.Count;
                        _fileTask = null;

                        if (_bufferedByteString.Count > 0 || _flushRequested)
                        {
                            _fileTask = WriteAsync(Self, _bufferedByteString, _autoFlush || _flushRequested || IsCanceled);
                            _bufferedByteString = ByteString.Empty;
                            _requestStrategy.FileBufferCount = 0;
                            _flushRequested = false;                            
                        }
                        else if (IsCanceled)
                        {
                            // close the channel/ file before completing the promise, allowing the
                            // file to be deleted, which would not work (on some systems) if the
                            // file is still open for writing
                            _chan?.Dispose();

                            var result = IOResult.Success(_bytesWritten);
                            if (_upstreamError is null)
                                _completionPromise.SetResult(result);
                            else
                                _completionPromise.SetException(new AbruptIOTerminationException(result, _upstreamError));

                            Context.Stop(Self);
                        }
                    }
                    return true;
                case Status.Failure msg:
                    _fileTask = null;
                    // close the channel/ file before completing the promise, allowing the
                    // file to be deleted, which would not work (on some systems) if the
                    // file is still open for writing
                    _chan?.Dispose();
                    _completionPromise.SetResult(IOResult.Failed(_bytesWritten, msg.Cause));
                    Cancel();
                    return true;
                case OnError error:
                    _log.Error(error.Cause, "Tearing down FileSink({0}) due to upstream error", _f.FullName);
                    _upstreamError = error.Cause;
                    RequestFlush();
                    return true;
                case OnComplete _:
                    RequestFlush();
                    return true;
                case FlushSignal _:
                    if (!_autoFlush)
                        RequestFlush();
                    return true;
                default:
                    return false;
            }
        }

        private void RequestFlush()
        {
            _flushRequested = true;
            if (_fileTask is null)
            {
                _fileTask = Task.CompletedTask;
                Self.Tell(new Status.Success(ByteString.Empty));                
            }
        }

        private async Task WriteAsync(IActorRef self, ByteString byteString, bool flush)
        {
            try
            {
                if (byteString.Count > 0)
                    await byteString.WriteToAsync(_chan, _cts.Token).ConfigureAwait(false);

                if (flush)
                    await _chan.FlushAsync(_cts.Token).ConfigureAwait(false);

                self.Tell(new Status.Success(byteString));
            }
            catch (Exception ex)
            {
                self.Tell(new Status.Failure(ex, "write_failed"));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            _cts.Cancel();
            _chan?.Dispose();
            _completionPromise.TrySetCanceled();
        }

        internal class FlushSignal
        {
            public static readonly FlushSignal Instance = new FlushSignal();
            private FlushSignal() { }
        }

        sealed class FileStreamRequestStrategy : MaxInFlightRequestStrategy
        {
            public FileStreamRequestStrategy(int max) : base(max)
            {
            }

            /// <summary>
            /// How many complete file stream buffers are queued
            /// </summary>
            public int FileBufferCount { get; set; }

            public override int InFlight => FileBufferCount;
        }
    }

    public class FlushSignaler
    {
        internal IActorRef FileSubscriber;

        public void Flush()
        {
            if (FileSubscriber == null)
                throw new InvalidOperationException("Instance has not been initialized by passing it into a file sink factory");
            FileSubscriber.Tell(IO.FileSubscriber.FlushSignal.Instance);
        }
    }
}
