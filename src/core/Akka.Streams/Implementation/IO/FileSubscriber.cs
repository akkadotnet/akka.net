//-----------------------------------------------------------------------
// <copyright file="FileSubscriber.cs" company="Akka.NET Project">
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
    internal class FileSubscriber : ActorSubscriber
    {
        public static Props Props(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int bufferSize, FileMode fileMode)
        {
            if(bufferSize <= 0)
                throw new ArgumentException("Buffer size muste be > 0");

            return Actor.Props.Create(()=> new FileSubscriber(f, completionPromise, bufferSize, fileMode)).WithDeploy(Deploy.Local);
        }

        private readonly FileInfo _f;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly FileMode _fileMode;
        private readonly ILoggingAdapter _log;
        private readonly WatermarkRequestStrategy _requestStrategy;
        private FileStream _chan;
        private long _bytesWritten;

        public FileSubscriber(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int bufferSize, FileMode fileMode)
        {
            _f = f;
            _completionPromise = completionPromise;
            _fileMode = fileMode;
            _log = Context.GetLogger();
            _requestStrategy = new WatermarkRequestStrategy(highWatermark: bufferSize);
        }

        public override IRequestStrategy RequestStrategy => _requestStrategy;

        protected override void PreStart()
        {
            try
            {
                _chan = _f.Open(_fileMode, FileAccess.Write);
                base.PreStart();
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<NotUsed>(ex)));
                Cancel();
            }
        }

        protected override bool Receive(object message)
        {
            return message.Match()
                .With<OnNext>(next =>
                {
                    try
                    {
                        var byteString = (ByteString) next.Element;
                        var bytes = (byteString.AsByteBuffer()).Array();
                         _chan.Write(bytes, 0, bytes.Length);
                        _bytesWritten += bytes.Length;
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<NotUsed>(ex)));
                        Cancel();
                    }
                })
                .With<OnError>(error =>
                {
                    _log.Error(error.Cause, $"Tearing down FileSink({_f.FullName}) due to upstream error");
                    _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<NotUsed>(error.Cause)));
                    Context.Stop(Self);
                })
                .With<OnComplete>(() =>
                {
                    try
                    {
                        _chan.Flush(true);
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<NotUsed>(ex)));
                    } 
                    Context.Stop(Self);
                })
                .WasHandled;
        }

        protected override void PostStop()
        {
            try
            {
                if(_chan != null)
                    _chan.Close();
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<NotUsed>(ex)));
            }

            _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Success(NotUsed.Instance)));
            base.PostStop();
        }
    }
}
