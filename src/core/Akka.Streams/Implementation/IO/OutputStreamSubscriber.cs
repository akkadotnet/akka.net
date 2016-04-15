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
    internal class OutputStreamSubscriber : ActorSubscriber
    {
        public static Props Props(Stream os, TaskCompletionSource<IOResult> completionPromise, int bufferSize, bool autoFlush)
        {
            if (bufferSize <= 0)
                throw new ArgumentException("Buffer size must be > 0");

            return
                Actor.Props.Create(() => new OutputStreamSubscriber(os, completionPromise, bufferSize, autoFlush))
                    .WithDeploy(Deploy.Local);
        }

        private readonly Stream _outputStream;
        private readonly TaskCompletionSource<IOResult> _completionPromise;
        private readonly bool _autoFlush;
        private long _bytesWritten;
        private readonly ILoggingAdapter _log;

        public OutputStreamSubscriber(Stream outputStream, TaskCompletionSource<IOResult> completionPromise, int bufferSize, bool autoFlush)
        {
            _outputStream = outputStream;
            _completionPromise = completionPromise;
            _autoFlush = autoFlush;
            RequestStrategy = new WatermarkRequestStrategy(highWatermark: bufferSize);
            _log = Context.GetLogger();
        }

        public override IRequestStrategy RequestStrategy { get; }

        protected override bool Receive(object message)
        {
            return message.Match()
                .With<OnNext>(next =>
                {
                    try
                    {
                        var bytes = next.Element as ByteString;
                        //blocking write
                        _outputStream.Write(bytes.ToArray(), 0, bytes.Count);
                        _bytesWritten += bytes.Count;
                        if (_autoFlush)
                            _outputStream.Flush();
                    }
                    catch (Exception ex)
                    {
                        _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(ex)));
                        Cancel();
                    }
                })
                .With<OnError>(error =>
                {
                    _log.Error(error.Cause,
                        $"Tearing down OutputStreamSink due to upstream error, wrote bytes: {_bytesWritten}");
                    _completionPromise.TrySetResult(new IOResult(_bytesWritten, Result.Failure<Unit>(error.Cause)));
                    Context.Stop(Self);
                })
                .With<OnComplete>(() =>
                {
                    Context.Stop(Self);
                    _outputStream.Flush();
                })
                .WasHandled;
        }

        protected override void PostStop()
        {
            try
            {
                _outputStream?.Close();
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
