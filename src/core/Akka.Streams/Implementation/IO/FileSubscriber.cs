//-----------------------------------------------------------------------
// <copyright file="FileSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
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
        private readonly long _startPosition;
        private readonly FileMode _fileMode;
        private readonly ILoggingAdapter _log;
        private readonly WatermarkRequestStrategy _requestStrategy;
        private readonly bool _autoFlush;
        private FileStream _chan;
        private long _bytesWritten;

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
            _f = f;
            _completionPromise = completionPromise;
            _startPosition = startPosition;
            _fileMode = fileMode;
            _autoFlush = autoFlush;
            _log = Context.GetLogger();
            _requestStrategy = new WatermarkRequestStrategy(highWatermark: bufferSize);

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
                _chan = _f.Open(_fileMode, FileAccess.Write, FileShare.ReadWrite);
                if (_startPosition > 0)
                    _chan.Position = _startPosition;
                base.PreStart();
            }
            catch (Exception ex)
            {
                CloseAndComplete(new Try<IOResult>(ex));
                Cancel();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case OnNext next:
                    try
                    {
                        var byteString = (ByteString)next.Element;
                        var bytes = byteString.ToArray();
                        _chan.Write(bytes, 0, bytes.Length);
                        _bytesWritten += bytes.Length;
                        if (_autoFlush)
                            _chan.Flush(true);
                    }
                    catch (Exception ex)
                    {
                        CloseAndComplete(IOResult.Failed(_bytesWritten, ex));
                        Cancel();
                    }
                    return true;

                case OnError error:
                    _log.Error(error.Cause, "Tearing down FileSink({0}) due to upstream error", _f.FullName);
                    CloseAndComplete(new Try<IOResult>(new AbruptIOTerminationException(IOResult.Success(_bytesWritten), error.Cause)));
                    Context.Stop(Self);
                    return true;

                case OnComplete _:
                    try
                    {
                        _chan.Flush(true);
                    }
                    catch (Exception ex)
                    {
                        CloseAndComplete(IOResult.Failed(_bytesWritten, ex));
                    }
                    Context.Stop(Self);
                    return true;

                case FlushSignal _:
                    try
                    {
                        _chan.Flush();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Tearing down FileSink({0}). File flush failed.", _f.FullName);
                        CloseAndComplete(IOResult.Failed(_bytesWritten, ex));
                        Context.Stop(Self);
                    }
                    return true;
            }

            return false;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            CloseAndComplete(IOResult.Success(_bytesWritten));
            base.PostStop();
        }

        private void CloseAndComplete(Try<IOResult> result)
        {
            try
            {
                // close the channel/file before completing the promise, allowing the
                // file to be deleted, which would not work (on some systems) if the
                // file is still open for writing
                _chan?.Dispose();

                if (result.IsSuccess) 
                    _completionPromise.SetResult(result.Success.Value);
                else 
                    _completionPromise.SetException(result.Failure.Value);
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetException(ex);
            }
        }

        internal class FlushSignal
        {
            public static readonly FlushSignal Instance = new FlushSignal();
            private FlushSignal() { }
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
