//-----------------------------------------------------------------------
// <copyright file="OutputStreamSubscriber.cs" company="Akka.NET Project">
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

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OutputStreamSubscriber : ActorSubscriber
    {
        /// <summary>
        /// Creates actor properties for a subscriber that writes byte sequences to a stream.
        /// </summary>
        /// <param name="os">The output stream to write.</param>
        /// <param name="completionPromise">The promise completed with the write result.</param>
        /// <param name="bufferSize">The request-strategy high watermark.</param>
        /// <param name="autoFlush">Whether to flush the stream after each element.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="bufferSize"/> is not positive.</exception>
        /// <returns>Local actor properties for the output-stream subscriber.</returns>
        public static Props Props(Stream os, TaskCompletionSource<IOResult> completionPromise, int bufferSize, bool autoFlush)
        {
            if (bufferSize <= 0)
                throw new ArgumentException("Buffer size must be > 0");

            return
                Actor.Props.Create<OutputStreamSubscriber>(os, completionPromise, bufferSize, autoFlush)
                    .WithDeploy(Deploy.Local);
        }

        private readonly Stream _outputStream;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly bool _autoFlush;
        private long _bytesWritten;
        private readonly ILoggingAdapter _log;

        /// <summary>
        /// Creates a subscriber actor that writes byte sequences to a stream.
        /// </summary>
        /// <param name="outputStream">The output stream to write.</param>
        /// <param name="completionPromise">The promise completed with the write result.</param>
        /// <param name="bufferSize">The request-strategy high watermark.</param>
        /// <param name="autoFlush">Whether to flush the stream after each element.</param>
        /// If this gets changed you must change <see cref="OutputStreamSubscriber.Props"/> as well!
        public OutputStreamSubscriber(Stream outputStream, TaskCompletionSource<IOResult> completionPromise, int bufferSize, bool autoFlush)
        {
            _outputStream = outputStream;
            _completionPromise = completionPromise;
            _autoFlush = autoFlush;
            RequestStrategy = new WatermarkRequestStrategy(highWatermark: bufferSize);
            _log = Context.GetLogger();
        }

        /// <summary>
        /// Requests elements according to the configured buffer-size high watermark.
        /// </summary>
        public override IRequestStrategy RequestStrategy { get; }

        /// <summary>
        /// Writes elements and handles upstream termination.
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
                            _outputStream.Write(segment.Span);
                        _bytesWritten += sequence.Length;
                        if (_autoFlush)
                            _outputStream.Flush();
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
                        Cancel();
                    }
                    return true;
                case OnError error:
                    _log.Error(error.Cause, "Tearing down OutputStreamSink due to upstream error, wrote bytes: {0}", _bytesWritten);
                    _completionPromise.TrySetException(new AbruptIOTerminationException(IOResult.Success(_bytesWritten), error.Cause));
                    Context.Stop(Self);
                    return true;
                case OnComplete _:
                    Context.Stop(Self);
                    _outputStream.Flush();
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Disposes the stream and completes the write-result promise if it has not already completed.
        /// </summary>
        protected override void PostStop()
        {
            try
            {
                _outputStream?.Dispose();
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
            }

            _completionPromise.TrySetResult(IOResult.Success(_bytesWritten));
            base.PostStop();
        }
    }
}
