//-----------------------------------------------------------------------
// <copyright file="InputStreamPublisher.cs" company="Akka.NET Project">
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
    internal sealed class InputStreamPublisher : Actors.ActorPublisher<ReadOnlySequence<byte>>
    {
        /// <summary>
        /// Creates actor properties for a publisher that reads an input stream in byte chunks.
        /// </summary>
        /// <param name="inputstream">The stream to read.</param>
        /// <param name="completionSource">The promise completed with the number of bytes read or an I/O failure.</param>
        /// <param name="chunkSize">The number of bytes requested for each read.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="chunkSize"/> is less than or equal to zero.
        /// </exception>
        /// <returns>Local actor properties for the input-stream publisher.</returns>
        public static Props Props(Stream inputstream, TaskCompletionSource<IOResult> completionSource, int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentException($"chunkSize must be > 0 was {chunkSize}", nameof(chunkSize));

            return Actor.Props.Create<InputStreamPublisher>(inputstream, completionSource, chunkSize).WithDeploy(Deploy.Local);
        }

        private readonly struct Continue : IDeadLetterSuppression
        {
            public static Continue Instance { get; } = new();
        }
        
        private readonly Stream _inputstream;
        private readonly TaskCompletionSource<IOResult> _completionSource;
        private readonly int _chunkSize;
        private readonly byte[] _bytes;
        private readonly ILoggingAdapter _log;
        private long _readBytesTotal;

        /// <summary>
        /// Creates a publisher actor that reads an input stream and emits byte chunks.
        /// </summary>
        /// <param name="inputstream">The stream to read.</param>
        /// <param name="completionSource">The promise completed with the number of bytes read or an I/O failure.</param>
        /// <param name="chunkSize">The number of bytes requested for each read.</param>
        /// If this gets changed you must change <see cref="InputStreamPublisher.Props"/> as well!
        public InputStreamPublisher(Stream inputstream, TaskCompletionSource<IOResult> completionSource, int chunkSize)
        {
            _inputstream = inputstream;
            _completionSource = completionSource;
            _chunkSize = chunkSize;
            _bytes = new byte[chunkSize];
            _log = Context.GetLogger();
        }

        /// <summary>
        /// Handles read requests, continuation messages, and cancellation.
        /// </summary>
        /// <param name="message">The actor message to handle.</param>
        /// <returns><see langword="true"/> when the message is handled; otherwise, <see langword="false"/>.</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Request _:
                case Continue _:
                    ReadAndSignal();
                    return true;
                case Actors.Cancel _:
                    Context.Stop(Self);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Disposes the input stream and completes the read-result promise if it has not already completed.
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            try
            {
                _inputstream?.Dispose();
            }
            catch (Exception ex)
            {
                _completionSource.SetResult(IOResult.Failed(_readBytesTotal, ex));
            }
            _completionSource.SetResult(IOResult.Success(_readBytesTotal));
        }

        private void ReadAndSignal()
        {
            if (!IsActive)
                return;

            ReadAndEmit();
            if(TotalDemand > 0 && IsActive)
                Self.Tell(Continue.Instance);
        }

        private void ReadAndEmit()
        {
            if (TotalDemand <= 0)
                return;

            try
            {
                // blocking read
                var readBytes = _inputstream.Read(_bytes, 0, _chunkSize);
                if (readBytes == 0)
                {
                    //had nothing to read into this chunk
                    _log.Debug("No more bytes available to read (got 0 from read)");
                    OnCompleteThenStop();
                }
                else
                {
                    _readBytesTotal += readBytes;
                    // emit immediately, as this is the only chance to do it before we might block again
                    var copy = new byte[readBytes];
                    Array.Copy(_bytes, 0, copy, 0, readBytes);
                    OnNext(new ReadOnlySequence<byte>(copy));
                }
            }
            catch (Exception ex)
            {
                OnErrorThenStop(ex);
            }
        }
    }
}
