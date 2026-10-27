//-----------------------------------------------------------------------
// <copyright file="FileSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Buffers;
using System.IO;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Actors;
using Akka.Streams.IO;
using Akka.Util;

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FileSubscriber : ActorSubscriber
    {
        /// <summary>
        /// Creates actor properties for a subscriber that writes incoming byte sequences to a file.
        /// </summary>
        /// <param name="f">The file to write.</param>
        /// <param name="completionPromise">The promise completed with the write result.</param>
        /// <param name="bufferSize">The request-strategy high watermark.</param>
        /// <param name="startPosition">The byte position at which writing starts.</param>
        /// <param name="fileMode">The mode used to open or create the file.</param>
        /// <param name="autoFlush">Whether to flush the file after each element.</param>
        /// <param name="flushCommand">Optional signaler that can request a file flush.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="bufferSize"/> is not positive or <paramref name="startPosition"/> is negative.</exception>
        /// <returns>Local actor properties for the file subscriber.</returns>
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

            return Actor.Props.Create<FileSubscriber>(f, completionPromise, bufferSize, startPosition, fileMode, autoFlush, flushCommand)
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
        /// Creates a subscriber actor that writes incoming byte sequences to a file.
        /// </summary>
        /// <param name="f">The file to write.</param>
        /// <param name="completionPromise">The promise completed with the write result.</param>
        /// <param name="bufferSize">The request-strategy high watermark.</param>
        /// <param name="startPosition">The byte position at which writing starts.</param>
        /// <param name="fileMode">The mode used to open or create the file.</param>
        /// <param name="autoFlush">Whether to flush the file after each element.</param>
        /// <param name="flushSignaler">Optional signaler that can request a file flush.</param>
        /// If this changes you must change <see cref="FileSubscriber.Props"/> as well!
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
        /// Requests elements according to the configured buffer-size high watermark.
        /// </summary>
        public override IRequestStrategy RequestStrategy => _requestStrategy;

        /// <summary>
        /// Opens the file for writing at the configured position before starting the subscriber.
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
        /// Writes elements, handles upstream termination, and processes explicit flush signals.
        /// </summary>
        /// <param name="message">The actor message to handle.</param>
        /// <returns><see langword="true"/> when the message is handled; otherwise, <see langword="false"/>.</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case OnNext next:
                    try
                    {
                        var sequence = (ReadOnlySequence<byte>)next.Element;
                        foreach (var segment in sequence)
                            _chan.Write(segment.Span);
                        _bytesWritten += sequence.Length;
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
        /// Closes the file and completes the write-result promise if it has not already completed.
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

        internal sealed class FlushSignal
        {
            public static readonly FlushSignal Instance = new();
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
