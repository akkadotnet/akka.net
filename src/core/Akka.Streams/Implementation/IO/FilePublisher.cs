//-----------------------------------------------------------------------
// <copyright file="FilePublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.IO;
using Akka.Streams.Actors;
using Akka.Streams.IO;

#pragma warning disable 1587

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class FilePublisher : Actors.ActorPublisher<ByteString>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="f">TBD</param>
        /// <param name="completionPromise">TBD</param>
        /// <param name="chunkSize">TBD</param>
        /// <param name="startPosition">TBD</param>
        /// <param name="initialBuffer">TBD</param>
        /// <param name="maxBuffer">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when one of the following conditions is met.
        /// 
        /// <ul>
        /// <li>The specified <paramref name="chunkSize"/> is less than or equal to zero.</li>
        /// <li>The specified <paramref name="startPosition"/> is less than zero</li>
        /// <li>The specified <paramref name="initialBuffer"/> is less than or equal to zero.</li>
        /// <li>The specified <paramref name="maxBuffer"/> is less than the specified <paramref name="initialBuffer"/>.</li>
        /// </ul>
        /// </exception>
        /// <returns>TBD</returns>
        public static Props Props(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int chunkSize,
            long startPosition, int initialBuffer, int maxBuffer)
        {
            if (chunkSize <= 0)
                throw new ArgumentException($"chunkSize must be > 0 (was {chunkSize})", nameof(chunkSize));
            if(startPosition < 0)
                throw new ArgumentException($"startPosition must be >= 0 (was {startPosition})", nameof(startPosition));
            if (initialBuffer <= 0)
                throw new ArgumentException($"initialBuffer must be > 0 (was {initialBuffer})", nameof(initialBuffer));
            if (maxBuffer < initialBuffer)
                throw new ArgumentException($"maxBuffer must be >= initialBuffer (was {maxBuffer})", nameof(maxBuffer));

            return Actor.Props.Create(() => new FilePublisher(f, completionPromise, chunkSize, startPosition, maxBuffer))
                .WithDeploy(Deploy.Local);
        }

        private struct Continue : IDeadLetterSuppression
        {
            public static readonly Continue Instance = new Continue();
        }

        private readonly FileInfo _f;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly int _chunkSize;
        private readonly long _startPosition;
        private readonly int _maxBuffer;
        private readonly byte[] _buffer;
        private readonly ILoggingAdapter _log;
        private long _eofReachedAtOffset = long.MinValue;
        private long _readBytesTotal;
        private IImmutableList<ByteString> _availableChunks = ImmutableList<ByteString>.Empty;
        private FileStream _chan;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="f">TBD</param>
        /// <param name="completionPromise">TBD</param>
        /// <param name="chunkSize">TBD</param>
        /// <param name="startPosition">TBD</param>
        /// <param name="maxBuffer">TBD</param>
        public FilePublisher(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int chunkSize, long startPosition, int maxBuffer)
        {
            _f = f;
            _completionPromise = completionPromise;
            _chunkSize = chunkSize;
            _startPosition = startPosition;
            _maxBuffer = maxBuffer;

            _log = Context.GetLogger();
            _buffer = new byte[chunkSize];
        }

        private bool EofEncountered => _eofReachedAtOffset != long.MinValue;

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            try
            {
                // Allow opening the same file for reading multiple times
                _chan = _f.Open(FileMode.Open, FileAccess.Read, FileShare.Read);
                if (_startPosition > 0)
                    _chan.Position = _startPosition;
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(IOResult.Failed(0, ex));
                OnErrorThenStop(ex);
            }

            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
            => message.Match()
                .With<Request>(() => ReadAndSignal(_maxBuffer))
                .With<Continue>(() => ReadAndSignal(_maxBuffer))
                .With<Cancel>(() => Context.Stop(Self))
                .WasHandled;

        private void ReadAndSignal(int maxReadAhead)
        {
            if (IsActive)
            {
                // Write previously buffered, then refill buffer
                _availableChunks = ReadAhead(maxReadAhead, SignalOnNexts(_availableChunks));

                if (TotalDemand > 0 && IsActive)
                    Self.Tell(Continue.Instance);
            }
        }

        private IImmutableList<ByteString> SignalOnNexts(IImmutableList<ByteString> chunks)
        {
            if (chunks.Count != 0 && TotalDemand > 0)
            {
                OnNext(chunks.First());
                return SignalOnNexts(chunks.RemoveAt(0));
            }

            if (chunks.Count == 0 && EofEncountered)
                OnCompleteThenStop();

            return chunks;
        }

        //BLOCKING IO READ
        private IImmutableList<ByteString> ReadAhead(int maxChunks, IImmutableList<ByteString> chunks)
        {
            if (chunks.Count <= maxChunks && IsActive)
            {
                try
                {
                    var readBytes = _chan.Read(_buffer, 0, _chunkSize);

                    if (readBytes == 0)
                    {
                        //EOF
                        _eofReachedAtOffset = _chan.Position;
                        _log.Debug($"No more bytes available to read (got 0 from read), marking final bytes of file @ {_eofReachedAtOffset}");
                        return chunks;
                    }

                    _readBytesTotal += readBytes;
                    var newChunks = chunks.Add(ByteString.CopyFrom(_buffer, 0, readBytes));
                    return ReadAhead(maxChunks, newChunks);
                }
                catch (Exception ex)
                {
                    OnErrorThenStop(ex);
                    //read failed, we're done here
                    return ImmutableList<ByteString>.Empty;
                }
            }

            return chunks;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();

            try
            {
                _chan?.Dispose();
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(IOResult.Failed(_readBytesTotal, ex));
            }

            _completionPromise.TrySetResult(IOResult.Success(_readBytesTotal));
        }
    }
}
