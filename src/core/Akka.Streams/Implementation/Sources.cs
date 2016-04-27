//-----------------------------------------------------------------------
// <copyright file="Sources.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Stage;
using Offered = System.Threading.Tasks.TaskCompletionSource<Akka.Streams.IQueueOfferResult>;

namespace Akka.Streams.Implementation
{
    internal sealed class QueueSource<TOut> : GraphStageWithMaterializedValue<SourceShape<TOut>, ISourceQueue<TOut>>
    {
        public sealed class Logic : GraphStageLogicWithCallbackWrapper<Tuple<TOut, Offered>>
        {
            private readonly QueueSource<TOut> _source;
            private IBuffer<TOut> _buffer;
            private Tuple<TOut, Offered> _pendingOffer;
            private bool _pulled;

            public Logic(QueueSource<TOut> source) : base(source.Shape)
            {
                _source = source;

                SetHandler(source.Shape.Outlet,
                    onDownstreamFinish: () =>
                    {
                        if (_pendingOffer != null)
                        {
                            var promise = _pendingOffer.Item2;
                            promise.SetResult(QueueOfferResult.QueueClosed.Instance);
                            _pendingOffer = null;
                        }
                        _source._completion.SetResult(new object());
                        CompleteStage();
                    },
                    onPull: () =>
                    {
                        if (_source._maxBuffer == 0)
                        {
                            if (_pendingOffer != null)
                            {
                                var element = _pendingOffer.Item1;
                                var promise = _pendingOffer.Item2;
                                Push(source.Shape.Outlet, element);
                                promise.SetResult(QueueOfferResult.Enqueued.Instance);
                                _pendingOffer = null;
                            }
                            else
                                _pulled = true;
                        }
                        else if (!_buffer.IsEmpty)
                        {
                            Push(source.Shape.Outlet, _buffer.Dequeue());
                            if (_pendingOffer != null)
                            {
                                var element = _pendingOffer.Item1;
                                var promise = _pendingOffer.Item2;
                                EnqueueAndSuccess(element, promise);
                            }
                        }
                        else
                            _pulled = true;
                    });
            }

            public override void PreStart()
            {
                if (_source._maxBuffer > 0)
                    _buffer = Buffer.Create<TOut>(_source._maxBuffer, Materializer);
                InitCallback(Callback());
            }

            public override void PostStop()
            {
                StopCallback(tuple =>
                {
                    if (tuple != null)
                    {
                        var promise = tuple.Item2;
                        promise.SetException(new IllegalStateException("Stream is terminated. SourceQueue is detached."));
                    }
                });
            }

            private void EnqueueAndSuccess(TOut element, Offered promise)
            {
                _buffer.Enqueue(element);
                promise.SetResult(QueueOfferResult.Enqueued.Instance);
            }

            private void BufferElement(TOut element, Offered promise)
            {
                if (!_buffer.IsFull)
                    EnqueueAndSuccess(element, promise);
                else
                {
                    switch (_source._overflowStrategy)
                    {
                        case OverflowStrategy.DropHead:
                            _buffer.DropHead();
                            EnqueueAndSuccess(element, promise);
                            break;
                        case OverflowStrategy.DropTail:
                            _buffer.DropTail();
                            EnqueueAndSuccess(element, promise);
                            break;
                        case OverflowStrategy.DropBuffer:
                            _buffer.Clear();
                            EnqueueAndSuccess(element, promise);
                            break;
                        case OverflowStrategy.DropNew:
                            promise.SetResult(QueueOfferResult.Dropped.Instance);
                            break;
                        case OverflowStrategy.Fail:
                            var bufferOverflowException =
                                new BufferOverflowException($"Buffer overflow (max capacity was: {_source._maxBuffer})!");
                            promise.SetResult(new QueueOfferResult.Failure(bufferOverflowException));
                            _source._completion.SetException(bufferOverflowException);
                            FailStage(bufferOverflowException);
                            break;
                        case OverflowStrategy.Backpressure:
                            if (_pendingOffer != null)
                                promise.SetException(
                                    new IllegalStateException(
                                        "You have to wait for previous offer to be resolved to send another request."));
                            else
                                _pendingOffer = new Tuple<TOut, Offered>(element, promise);
                            break;
                    }
                }
            }

            private Action<Tuple<TOut, Offered>> Callback()
            {
                return GetAsyncCallback<Tuple<TOut, Offered>>(
                    tuple =>
                    {
                        var element = tuple.Item1;
                        var promise = tuple.Item2;
                        if (_source._maxBuffer != 0)
                        {
                            BufferElement(element, promise);
                            if (_pulled)
                            {
                                Push(_source.Shape.Outlet, _buffer.Dequeue());
                                _pulled = false;
                            }
                        }
                        else if (_pulled)
                        {
                            Push(_source.Shape.Outlet, element);
                            _pulled = false;
                            promise.SetResult(QueueOfferResult.Enqueued.Instance);
                        }
                        else
                            _pendingOffer = tuple;
                    });
            }

            internal void Invoke(Tuple<TOut, Offered> tuple) => InvokeCallbacks(tuple);
        }

        public sealed class Materialized : ISourceQueue<TOut>
        {
            private readonly QueueSource<TOut> _source;
            private readonly Action<Tuple<TOut, Offered>> _invokeLogic;

            public Task<IQueueOfferResult> OfferAsync(TOut element)
            {
                var promise = new TaskCompletionSource<IQueueOfferResult>();
                _invokeLogic(new Tuple<TOut, Offered>(element, promise));
                return promise.Task;
            }

            public Task WatchCompletionAsync() => _source._completion.Task;

            public Materialized(QueueSource<TOut> source, Action<Tuple<TOut, Offered>> invokeLogic)
            {
                _source = source;
                _invokeLogic = invokeLogic;
            }
        }

        private readonly int _maxBuffer;
        private readonly OverflowStrategy _overflowStrategy;
        private readonly TaskCompletionSource<object> _completion = new TaskCompletionSource<object>();

        public QueueSource(int maxBuffer, OverflowStrategy overflowStrategy)
        {
            _maxBuffer = maxBuffer;
            _overflowStrategy = overflowStrategy;
            Shape = new SourceShape<TOut>(new Outlet<TOut>("queueSource.out"));
        }

        public override SourceShape<TOut> Shape { get; }

        public override ILogicAndMaterializedValue<ISourceQueue<TOut>>  CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var logic = new Logic(this);
            return new LogicAndMaterializedValue<ISourceQueue<TOut>>(logic, new Materialized(this, t => logic.Invoke(t)));
        }
    }
}