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
            object flushCommand = null)
        {
            if(bufferSize <= 0)
                throw new ArgumentException($"bufferSize must be > 0 (was {bufferSize})", nameof(bufferSize));
            if(startPosition < 0)
                throw new ArgumentException($"startPosition must be >= 0 (was {startPosition})", nameof(startPosition));

            return Actor.Props.Create(()=> new FileSubscriber(f, completionPromise, bufferSize, startPosition, fileMode, autoFlush, flushCommand))
                .WithDeploy(Deploy.Local);
        }

        private readonly FileInfo _f;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly long _startPosition;
        private readonly FileMode _fileMode;
        private readonly ILoggingAdapter _log;
        private readonly WatermarkRequestStrategy _requestStrategy;
        private readonly bool _autoFlush;
        private readonly object _flushCommand;
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
        /// <param name="flushCommand"></param>
        public FileSubscriber(
            FileInfo f, 
            TaskCompletionSource<IOResult> completionPromise, 
            int bufferSize, 
            long startPosition, 
            FileMode fileMode,
            bool autoFlush, 
            object flushCommand)
        {
            _f = f;
            _completionPromise = completionPromise;
            _startPosition = startPosition;
            _fileMode = fileMode;
            _autoFlush = autoFlush;
            _flushCommand = flushCommand;
            _log = Context.GetLogger();
            _requestStrategy = new WatermarkRequestStrategy(highWatermark: bufferSize);
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
                _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
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
                        var byteString = (ByteString) next.Element;
                        var bytes = byteString.ToArray();
                        try
                        {
                            _chan.Write(bytes, 0, bytes.Length);
                            _bytesWritten += bytes.Length;
                            if (_autoFlush)
                                _chan.Flush(true);
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, $"Tearing down FileSink({_f.FullName}) due to write error.");
                            _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
                            Context.Stop(Self);
                        }
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
                        Cancel();
                    }
                    return true;

                case OnError error:
                    _log.Error(error.Cause, $"Tearing down FileSink({_f.FullName}) due to upstream error");
                    _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, error.Cause));
                    Context.Stop(Self);
                    return true;

                case OnComplete _:
                    try
                    {
                        _chan.Flush(true);
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
                    } 
                    Context.Stop(Self);
                    return true;

                case var msg when _flushCommand != null && ReferenceEquals(_flushCommand, msg):
                    try
                    {
                        _chan.Flush();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, $"Tearing down FileSink({_f.FullName}). File flush failed.");
                        _completionPromise.TrySetResult(IOResult.Failed(_bytesWritten, ex));
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
            try
            {
                _chan?.Dispose();
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
