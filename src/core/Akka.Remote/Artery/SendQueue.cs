using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Remote.Artery.Utils;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal static class SendQueue
    {
        public interface IProducerApi<in T>
        {
            bool Offer(T message);
            bool IsEnabled { get; }
        }

        public interface IQueueValue<T> : IProducerApi<T>
        {
            void Inject(IQueue<T> queue);
        }

        public interface IWakeupSignal
        {
            void Wakeup();
        }
    }

    internal sealed class SendQueue<T> : GraphStageWithMaterializedValue<SourceShape<T>, SendQueue.IQueueValue<T>>
    {
        #region Logic

        public class Logic : OutGraphStageLogic, SendQueue.IWakeupSignal
        {
            private readonly SendQueue<T> _source;
            private readonly Outlet<T> _out;

            private IQueue<T> _consumerQueue;
            private readonly Action _wakeupCallback;

            public Logic(SendQueue<T> source) : base(source.Shape)
            {
                _source = source;
                _out = source.Out;
                _wakeupCallback = GetAsyncCallback(() =>
                {
                    if (IsAvailable(_out))
                        TryPush();
                });
                
                SetHandler(_out, this);
            }

            public override void PreStart()
            {
                var cb = GetAsyncCallback<Try<IQueue<T>>>(result =>
                {
                    if (result.IsSuccess)
                    {
                        _consumerQueue = result.Get();
                        _source.NeedWakeup = true;
                        if (IsAvailable(_out))
                            TryPush();
                    }
                    else
                        FailStage(result.Failure.Value);
                });

                // ARTERY TODO: in the original scala code, the pre-start callback is called inside the materializer execution context, but we don't have execution contexts. Does MessageDispatcher.Schedule equals to the scala ExecutionContext.Execute?
                var ec = Materializer.ExecutionContext;
                _source._queuePromise.Task.ContinueWith(task => ec.Schedule(() => cb.Invoke(task.Result)));
            }

            public override void OnPull()
            {
                if (_consumerQueue != null)
                    TryPush();
            }

            private void TryPush(bool firstAttempt = true)
            {
                switch (_consumerQueue.Poll())
                {
                    case null:
                        _source.NeedWakeup = true;
                        // additional poll() to grab any elements that might missed the needWakeup
                        // and have been enqueued just after it
                        if (firstAttempt)
                            TryPush(false);
                        break;
                    case var elem:
                        _source.NeedWakeup = false; // there will be another onPull
                        Push(_out, elem);
                        break;
                }
            }

            public void Wakeup()
            {
                _wakeupCallback.Invoke();
            }

            public override void PostStop()
            {
                var pending = new List<T>();
                if (_consumerQueue != null)
                {
                    var msg = _consumerQueue.Poll();
                    while (msg != null)
                    {
                        pending.Add(msg);
                        msg = _consumerQueue.Poll();
                    }
                    _consumerQueue.Clear();
                }
                _source.PostStopAction.Invoke(pending);

                base.PostStop();
            }
        }

        #endregion

        private readonly TaskCompletionSource<Try<IQueue<T>>> _queuePromise = new TaskCompletionSource<Try<IQueue<T>>>();
        private readonly Logic _logic;
        private volatile bool _needWakeup;

        public Outlet<T> Out => new Outlet<T>("SendQueue.out");
        public override SourceShape<T> Shape => new SourceShape<T>(Out);
        public Action<List<T>> PostStopAction { get; }

#pragma warning disable 420
        public bool NeedWakeup
        {
            get => Volatile.Read(ref _needWakeup);
            set => Volatile.Write(ref _needWakeup, value);
        }
#pragma warning restore 420

        public SendQueue(Action<List<T>> postStopAction) // (Vector<T> postStopAction)
        {
            postStopAction.Requiring(p => p != null, "postStopAction must not be null");

            PostStopAction = postStopAction;
            _logic = new Logic(this);
        }

        public override ILogicAndMaterializedValue<SendQueue.IQueueValue<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            => new LogicAndMaterializedValue<SendQueue.IQueueValue<T>>(_logic, new QueueValue(this));

        public class QueueValue : SendQueue.IQueueValue<T>
        {
            private readonly SendQueue<T> _source;
            private volatile IQueue<T> _producerQueue;
            private readonly TaskCompletionSource<Try<IQueue<T>>> _queuePromise;

            public QueueValue(SendQueue<T> source)
            {
                _source = source;
                _queuePromise = source._queuePromise;
            }

            public void Inject(IQueue<T> queue)
            {
#pragma warning disable 420
                Volatile.Write(ref _producerQueue, queue);
#pragma warning restore 420
                _queuePromise.SetResult(new Try<IQueue<T>>(queue));
            }

            public bool Offer(T message)
            {
#pragma warning disable 420
                var q = Volatile.Read(ref _producerQueue);
#pragma warning restore 420
                if(q is null)
                    throw new IllegalStateException("Offer not allowed before injecting the queue");
                var result = q.Offer(message);
                if (result && _source.NeedWakeup)
                {
                    _source.NeedWakeup = false;
                    _source._logic.Wakeup();
                }

                return result;
            }

            public bool IsEnabled => true;
        }
    }
}
