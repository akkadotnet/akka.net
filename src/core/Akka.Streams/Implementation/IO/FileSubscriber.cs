using System;
using System.IO;
using System.Reactive.Streams;
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
        public static Props Props(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int bufferSize,bool append)
        {
            if(bufferSize <= 0)
                throw new ArgumentException("Buffer size muste be > 0");

            return Actor.Props.Create(()=> new FileSubscriber(f, completionPromise, bufferSize, append)).WithDeploy(Deploy.Local);
        }

        private readonly FileInfo _f;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly bool _append;
        private readonly ILoggingAdapter _log;
        private readonly WatermarkRequestStrategy _requestStrategy;
        private FileStream _chan;
        private long _bytesWritten;

        private FileSubscriber(FileInfo f, TaskCompletionSource<IOResult> completionPromise, int bufferSize, bool append)
        {
            _f = f;
            _completionPromise = completionPromise;
            _append = append;
            _log = Context.GetLogger();
            _requestStrategy = new WatermarkRequestStrategy(highWatermark: bufferSize);
        }

        public override IRequestStrategy RequestStrategy => _requestStrategy;

        protected override void PreStart()
        {
            try
            {
                var openOptions = _append ? FileMode.Append : FileMode.Create;
                _chan = _f.Open(openOptions);
                base.PreStart();
            }
            catch (Exception ex)
            {
                _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(ex)));
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
                        var bytes = ((ByteBuffer)next.Element).Array();
                         _chan.Write(bytes, 0, bytes.Length);
                        _bytesWritten += bytes.Length;
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(ex)));
                        Cancel();
                    }
                })
                .With<OnError>(error =>
                {
                    _log.Error(error.Cause, $"Tearing down FileSink({_f.FullName}) due to upstream error");
                    _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(error.Cause)));
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
                        _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(ex)));
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
                _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(ex)));
            }

            _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Success(Unit.Instance)));
            base.PostStop();
        }
    }
}
