//-----------------------------------------------------------------------
// <copyright file="InputStreamPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal class InputStreamPublisher : Actors.ActorPublisher<ByteString>
    {
        public static Props Props(Stream inputstream, TaskCompletionSource<IOResult> completionSource, int chunkSize)
        {
            if (chunkSize <= 0)
                throw new ArgumentException($"chunkSize must be > 0 was {chunkSize}");

            return Actor.Props.Create(()=> new InputStreamPublisher(inputstream, completionSource, chunkSize)).WithDeploy(Deploy.Local);
        }

        private struct Continue
        {
            public static Continue Instance { get; } = new Continue();
        }
        
        private readonly Stream _inputstream;
        private readonly TaskCompletionSource<IOResult> _completionSource;
        private readonly int _chunkSize;
        private readonly byte[] _bytes;
        private readonly ILoggingAdapter _log;
        private long _readBytesTotal;

        public InputStreamPublisher(Stream inputstream, TaskCompletionSource<IOResult> completionSource, int chunkSize)
        {
            _inputstream = inputstream;
            _completionSource = completionSource;
            _chunkSize = chunkSize;
            _bytes = new byte[chunkSize];
            _log = Context.GetLogger();
        }

        protected override bool Receive(object message)
            => message.Match()
                    .With<Request>(ReadAndSignal)
                    .With<Continue>(ReadAndSignal)
                    .With<Cancel>(() => Context.Stop(Self))
                    .WasHandled;

        protected override void PostStop()
        {
            base.PostStop();
            try
            {
                _inputstream?.Close();
            }
            catch (Exception ex)
            {
                _completionSource.SetResult(new IOResult(_readBytesTotal, Result.Failure<NotUsed>(ex)));
            }
            _completionSource.SetResult(new IOResult(_readBytesTotal, Result.Success(NotUsed.Instance)));
        }

        private void ReadAndSignal()
        {
            if (!IsActive)
                return;

            ReadAndEmit();
            if(TotalDemand > 0 )
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
                    OnNext(ByteString.Create(_bytes, 0, readBytes));
                }
            }
            catch (Exception ex)
            {
                OnErrorThenStop(ex);
            }
        }
    }
}