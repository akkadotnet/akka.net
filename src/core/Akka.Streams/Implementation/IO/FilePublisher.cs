//-----------------------------------------------------------------------
// <copyright file="FilePublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
using Akka.Util;

#pragma warning disable 1587

namespace Akka.Streams.Implementation.IO
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class FilePublisher : Actors.ActorPublisher<ByteString>
    {
        public static Props Props(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int chunkSize,
            int initialBuffer, int maxBuffer)
        {
            if (chunkSize <= 0)
                throw new ArgumentException($"chunkSize must be > 0 (was {chunkSize})");
            if (initialBuffer <= 0)
                throw new ArgumentException($"initialBuffer must be > 0 (was {initialBuffer})");
            if (maxBuffer < initialBuffer)
                throw new ArgumentException($"maxBuffer must be >= initialBuffer (was {maxBuffer})");

            return Actor.Props.Create(() => new FilePublisher(f, completionPromise, chunkSize, initialBuffer, maxBuffer))
                .WithDeploy(Deploy.Local);
        }

        private struct Continue
        {
            public static readonly Continue Instance = new Continue();
        }

        private readonly FileInfo _f;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly int _chunkSize;
        private readonly int _initialBuffer;
        private readonly int _maxBuffer;
        private readonly byte[] _buffer;
        private readonly ILoggingAdapter _log;
        private long _eofReachedAtOffset = long.MinValue;
        private long _readBytesTotal;
        private IImmutableList<ByteString> _availableChunks = ImmutableList<ByteString>.Empty;
        private FileStream _chan;

        public FilePublisher(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int chunkSize, int initialBuffer, int maxBuffer)
        {
            _f = f;
            _completionPromise = completionPromise;
            _chunkSize = chunkSize;
            _initialBuffer = initialBuffer;
            _maxBuffer = maxBuffer;

            _log = Context.GetLogger();
            _buffer = new byte[chunkSize];
        }

        private bool EofEncountered => _eofReachedAtOffset != long.MinValue;

        protected override void PreStart()
        {
            try
            {
                _chan = _f.Open(FileMode.Open, FileAccess.Read);
            }
            catch (Exception ex)
            {
                OnErrorThenStop(ex);
            }

            base.PreStart();
        }

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
                //Write previously buffered, read into buffer, write newly buffered
                _availableChunks = SignalOnNexts(ReadAhead(maxReadAhead, SignalOnNexts(_availableChunks)));

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
                    var newChunks = chunks.Add(ByteString.Create(_buffer, 0, readBytes));
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

        protected override void PostStop()
        {
            base.PostStop();

            try
            {
                _chan?.Dispose();
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(new IOResult(_readBytesTotal, Result.Failure<NotUsed>(ex)));
            }

            _completionPromise.TrySetResult(new IOResult(_readBytesTotal, Result.Success(NotUsed.Instance)));
        }
    }
}

