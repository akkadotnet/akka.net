//-----------------------------------------------------------------------
// <copyright file="Sources.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    public sealed class QueueSource<TOut> : GraphStageWithMaterializedValue<SourceShape<TOut>, ISourceQueueWithComplete<TOut>>
    {
        #region internal classes

        /// <summary>
        /// TBD
        /// </summary>
        public interface IInput { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        internal sealed class Offer<T> : IInput
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
            /// <param name="completionSource">TBD</param>
            public Offer(T element, TaskCompletionSource<IQueueOfferResult> completionSource)
            {
                Element = element;
                CompletionSource = completionSource;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public T Element { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TaskCompletionSource<IQueueOfferResult> CompletionSource { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Completion : IInput
        {
            /// <summary>
            /// TBD
            /// </summary>
            public static Completion Instance { get; } = new Completion();

            private Completion()
            {
                
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal sealed class Failure : IInput
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ex">TBD</param>
            public Failure(Exception ex)
            {
                Ex = ex;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Exception Ex { get; }
        }

        #endregion  

        private sealed class Logic : GraphStageLogicWithCallbackWrapper<IInput>, IOutHandler
        {
            private readonly TaskCompletionSource<object> _completion;
            private readonly QueueSource<TOut> _stage;
            private IBuffer<TOut> _buffer;
            private Offer<TOut> _pendingOffer;
            private bool _terminating;

            public Logic(QueueSource<TOut> stage, TaskCompletionSource<object> completion) : base(stage.Shape)
            {
                _completion = completion;
                _stage = stage;

                SetHandler(stage.Out, this);
            }

            public void OnPull()
            {
                if (_stage._maxBuffer == 0)
                {
                    if (_pendingOffer != null)
                    {
                        Push(_stage.Out, _pendingOffer.Element);
                        _pendingOffer.CompletionSource.SetResult(QueueOfferResult.Enqueued.Instance);
                        _pendingOffer = null;
                        if (_terminating)
                        {
                            _completion.SetResult(new object());
                            CompleteStage();
                        }
                    }
                }
                else if (_buffer.NonEmpty)
                {
                    Push(_stage.Out, _buffer.Dequeue());
                    if (_pendingOffer != null)
                    {
                        EnqueueAndSuccess(_pendingOffer);
                        _pendingOffer = null;
                    }
                }

                if (_terminating && _buffer.IsEmpty)
                {
                    _completion.SetResult(new object());
                    CompleteStage();
                }
            }

            public void OnDownstreamFinish()
            {
                if (_pendingOffer != null)
                {
                    _pendingOffer.CompletionSource.SetResult(QueueOfferResult.QueueClosed.Instance);
                    _pendingOffer = null;
                }
                _completion.SetResult(new object());
                CompleteStage();
            }

            public override void PreStart()
            {
                if (_stage._maxBuffer > 0)
                    _buffer = Buffer.Create<TOut>(_stage._maxBuffer, Materializer);
                InitCallback(Callback());
            }

            public override void PostStop()
            {
                StopCallback(input =>
                {
                    var offer = input as Offer<TOut>;
                    if (offer != null)
                    {
                        var promise = offer.CompletionSource;
                        promise.SetException(new IllegalStateException("Stream is terminated. SourceQueue is detached."));
                    }
                });
            }

            private void EnqueueAndSuccess(Offer<TOut> offer)
            {
                _buffer.Enqueue(offer.Element);
                offer.CompletionSource.SetResult(QueueOfferResult.Enqueued.Instance);
            }

            private void BufferElement(Offer<TOut> offer)
            {
                if (!_buffer.IsFull)
                    EnqueueAndSuccess(offer);
                else
                {
                    switch (_stage._overflowStrategy)
                    {
                        case OverflowStrategy.DropHead:
                            _buffer.DropHead();
                            EnqueueAndSuccess(offer);
                            break;
                        case OverflowStrategy.DropTail:
                            _buffer.DropTail();
                            EnqueueAndSuccess(offer);
                            break;
                        case OverflowStrategy.DropBuffer:
                            _buffer.Clear();
                            EnqueueAndSuccess(offer);
                            break;
                        case OverflowStrategy.DropNew:
                            offer.CompletionSource.SetResult(QueueOfferResult.Dropped.Instance);
                            break;
                        case OverflowStrategy.Fail:
                            var bufferOverflowException =
                                new BufferOverflowException($"Buffer overflow (max capacity was: {_stage._maxBuffer})!");
                            offer.CompletionSource.SetResult(new QueueOfferResult.Failure(bufferOverflowException));
                            _completion.SetException(bufferOverflowException);
                            FailStage(bufferOverflowException);
                            break;
                        case OverflowStrategy.Backpressure:
                            if (_pendingOffer != null)
                                offer.CompletionSource.SetException(
                                    new IllegalStateException(
                                        "You have to wait for previous offer to be resolved to send another request."));
                            else
                                _pendingOffer = offer;
                            break;
                    }
                }
            }

            private Action<IInput> Callback()
            {
                return GetAsyncCallback<IInput>(
                    input =>
                    {
                        var offer = input as Offer<TOut>;
                        if (offer != null)
                        {
                            if (_stage._maxBuffer != 0)
                            {
                                BufferElement(offer);
                                if (IsAvailable(_stage.Out))
                                    Push(_stage.Out, _buffer.Dequeue());
                            }
                            else if (IsAvailable(_stage.Out))
                            {
                                Push(_stage.Out, offer.Element);
                                offer.CompletionSource.SetResult(QueueOfferResult.Enqueued.Instance);
                            }
                            else if (_pendingOffer == null)
                                _pendingOffer = offer;
                            else
                            {
                                switch (_stage._overflowStrategy)
                                {
                                    case OverflowStrategy.DropHead:
                                    case OverflowStrategy.DropBuffer:
                                        _pendingOffer.CompletionSource.SetResult(QueueOfferResult.Dropped.Instance);
                                        _pendingOffer = offer;
                                        break;
                                    case OverflowStrategy.DropTail:
                                    case OverflowStrategy.DropNew:
                                        offer.CompletionSource.SetResult(QueueOfferResult.Dropped.Instance);
                                        break;
                                    case OverflowStrategy.Backpressure:
                                        offer.CompletionSource.SetException(
                                            new IllegalStateException(
                                                "You have to wait for previous offer to be resolved to send another request"));
                                        break;
                                    case OverflowStrategy.Fail:
                                        var bufferOverflowException =
                                            new BufferOverflowException(
                                                $"Buffer overflow (max capacity was: {_stage._maxBuffer})!");
                                        offer.CompletionSource.SetResult(new QueueOfferResult.Failure(bufferOverflowException));
                                        _completion.SetException(bufferOverflowException);
                                        FailStage(bufferOverflowException);
                                        break;
                                    default:
                                        throw new ArgumentOutOfRangeException();
                                }
                            }
                        }

                        var completion = input as Completion;
                        if (completion != null)
                        {
                            if (_stage._maxBuffer != 0 && _buffer.NonEmpty || _pendingOffer != null)
                                _terminating = true;
                            else
                            {
                                _completion.SetResult(new object());
                                CompleteStage();
                            }
                        }

                        var failure = input as Failure;
                        if (failure != null)
                        {
                            _completion.SetException(failure.Ex);
                            FailStage(failure.Ex);
                        }
                    });
            }

            internal void Invoke(IInput offer) => InvokeCallbacks(offer);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Materialized : ISourceQueueWithComplete<TOut>
        {
            private readonly Action<IInput> _invokeLogic;
            private readonly TaskCompletionSource<object> _completion;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="invokeLogic">TBD</param>
            /// <param name="completion">TBD</param>
            public Materialized(Action<IInput> invokeLogic, TaskCompletionSource<object> completion)
            {
                _invokeLogic = invokeLogic;
                _completion = completion;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="element">TBD</param>
            /// <returns>TBD</returns>
            public Task<IQueueOfferResult> OfferAsync(TOut element)
            {
                var promise = new TaskCompletionSource<IQueueOfferResult>();
                _invokeLogic(new Offer<TOut>(element, promise));
                return promise.Task;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public Task WatchCompletionAsync() => _completion.Task;

            /// <summary>
            /// TBD
            /// </summary>
            public void Complete() => _invokeLogic(Completion.Instance);

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="ex">TBD</param>
            public void Fail(Exception ex) => _invokeLogic(new Failure(ex));
        }

        private readonly int _maxBuffer;
        private readonly OverflowStrategy _overflowStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="maxBuffer">TBD</param>
        /// <param name="overflowStrategy">TBD</param>
        public QueueSource(int maxBuffer, OverflowStrategy overflowStrategy)
        {
            _maxBuffer = maxBuffer;
            _overflowStrategy = overflowStrategy;
            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("queueSource.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<ISourceQueueWithComplete<TOut>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<object>();
            var logic = new Logic(this, completion);
            return new LogicAndMaterializedValue<ISourceQueueWithComplete<TOut>>(logic, new Materialized(t => logic.Invoke(t), completion));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TSource">TBD</typeparam>
    public sealed class UnfoldResourceSource<TOut, TSource> : GraphStage<SourceShape<TOut>>
    {
        #region Logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldResourceSource<TOut, TSource> _stage;
            private readonly Attributes _inheritedAttributes;
            private readonly Lazy<Decider> _decider;
            private TSource _blockingStream;

            public Logic(UnfoldResourceSource<TOut, TSource> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                _inheritedAttributes = inheritedAttributes;
                _decider = new Lazy<Decider>(() =>
                {
                    var strategy = _inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return strategy != null ? strategy.Decider : Deciders.StoppingDecider;
                });

                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                var stop = false;
                while (!stop)
                {
                    try
                    {
                        var data = _stage._readData(_blockingStream);
                        if (data.HasValue)
                            Push(_stage.Out, data.Value);
                        else
                            CloseStage();

                        break;
                    }
                    catch (Exception ex)
                    {
                        var directive = _decider.Value(ex);
                        switch (directive)
                        {
                            case Directive.Stop:
                                _stage._close(_blockingStream);
                                FailStage(ex);
                                stop = true;
                                break;
                            case Directive.Restart:
                                RestartState();
                                break;
                            case Directive.Resume:
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }
                }
            }

            public override void OnDownstreamFinish() => CloseStage();
            
            public override void PreStart() => _blockingStream = _stage._create();

            private void RestartState()
            {
                _stage._close(_blockingStream);
                _blockingStream = _stage._create();
            }

            private void CloseStage()
            {
                try
                {
                    _stage._close(_blockingStream);
                    CompleteStage();
                }
                catch (Exception ex)
                {
                    FailStage(ex);
                }
            }
        }

        #endregion

        private readonly Func<TSource> _create;
        private readonly Func<TSource, Option<TOut>> _readData;
        private readonly Action<TSource> _close;


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="create">TBD</param>
        /// <param name="readData">TBD</param>
        /// <param name="close">TBD</param>
        public UnfoldResourceSource(Func<TSource> create, Func<TSource, Option<TOut>> readData, Action<TSource> close)
        {
            _create = create;
            _readData = readData;
            _close = close;

            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.UnfoldResourceSource;

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("UnfoldResourceSource.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "UnfoldResourceSource";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TSource">TBD</typeparam>
    public sealed class UnfoldResourceSourceAsync<TOut, TSource> : GraphStage<SourceShape<TOut>>
    {
        #region Logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly UnfoldResourceSourceAsync<TOut, TSource> _source;
            private readonly Attributes _inheritedAttributes;
            private readonly Lazy<Decider> _decider;
            private TaskCompletionSource<TSource> _resource;
            private Action<Either<Option<TOut>, Exception>> _callback;
            private Action<Tuple<Action, Task>> _closeCallback;
            private Action<Tuple<TSource, Action<TSource>>> _onResourceReadyCallback;

            public Logic(UnfoldResourceSourceAsync<TOut, TSource> source, Attributes inheritedAttributes) : base(source.Shape)
            {
                _source = source;
                _inheritedAttributes = inheritedAttributes;
                _resource = new TaskCompletionSource<TSource>();
                _decider = new Lazy<Decider>(() =>
                {
                    var strategy = _inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return strategy != null ? strategy.Decider : Deciders.StoppingDecider;
                });

                SetHandler(source.Out, this);
            }

            public override void OnPull()
            {
                OnResourceReady(source =>
                {
                    try
                    {
                        _source._readData(source).ContinueWith(t =>
                        {
                            if (t.IsCompleted && !t.IsCanceled)
                                _callback(new Left<Option<TOut>, Exception>(t.Result));
                            else
                                _callback(new Right<Option<TOut>, Exception>(t.Exception));
                        });
                    }
                    catch (Exception ex)
                    {
                        ErrorHandler(ex);
                    }
                });
            }

            public override void OnDownstreamFinish() => CloseStage();

            public override void PreStart()
            {
                CreateStream(false);
                _callback = GetAsyncCallback<Either<Option<TOut>, Exception>>(either =>
                {
                    if (either.IsLeft)
                    {
                        var element = either.ToLeft().Value;
                        if (element.HasValue)
                            Push(_source.Out, element.Value);
                        else
                            CloseStage();
                    }
                    else
                        ErrorHandler(either.ToRight().Value);
                });

                _closeCallback = GetAsyncCallback<Tuple<Action, Task>>(t =>
                {
                    if (t.Item2.IsCompleted && !t.Item2.IsFaulted)
                        t.Item1();
                    else
                        FailStage(t.Item2.Exception);
                });

                _onResourceReadyCallback = GetAsyncCallback<Tuple<TSource, Action<TSource>>>(t => t.Item2(t.Item1));
            }

            private void CreateStream(bool withPull)
            {
                var cb = GetAsyncCallback<Either<TSource, Exception>>(either =>
                {
                    if (either.IsLeft)
                    {
                        _resource.SetResult(either.ToLeft().Value);
                        if(withPull)
                            OnPull();
                    }
                    else
                        FailStage(either.ToRight().Value);
                });

                try
                {
                    _source._create().ContinueWith(t =>
                    {
                        if(t.IsCompleted && !t.IsFaulted && t.Result != null)
                            cb(new Left<TSource, Exception>(t.Result));
                        else
                            cb(new Right<TSource, Exception>(t.Exception));
                    });
                }
                catch (Exception ex)
                {
                    FailStage(ex);
                }
            }

            private void OnResourceReady(Action<TSource> action)
            {
                _resource.Task.ContinueWith(t =>
                {
                    if (t.IsCompleted && !t.IsFaulted && t.Result != null)
                        _onResourceReadyCallback(Tuple.Create(t.Result, action));
                });
            }

            private void ErrorHandler(Exception ex)
            {
                var directive = _decider.Value(ex);
                switch (directive)
                {
                    case Directive.Stop:
                        OnResourceReady(s=>_source._close(s));
                        FailStage(ex);
                        break;
                    case Directive.Resume:
                        OnPull();
                        break;
                    case Directive.Restart:
                        RestartState();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private void CloseAndThen(Action action)
            {
                SetKeepGoing(true);
                OnResourceReady(source =>
                {
                    try
                    {
                        _source._close(source).ContinueWith(t => _closeCallback(Tuple.Create(action, t)));
                    }
                    catch (Exception ex)
                    {
                        FailStage(ex);
                    }
                });
            }

            private void RestartState()
            {
                CloseAndThen(() =>
                {
                    _resource = new TaskCompletionSource<TSource>();
                    CreateStream(true);
                });
            }

            private void CloseStage() => CloseAndThen(CompleteStage);
        }

        #endregion

        private readonly Func<Task<TSource>> _create;
        private readonly Func<TSource, Task<Option<TOut>>> _readData;
        private readonly Func<TSource, Task> _close;


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="create">TBD</param>
        /// <param name="readData">TBD</param>
        /// <param name="close">TBD</param>
        public UnfoldResourceSourceAsync(Func<Task<TSource>> create, Func<TSource, Task<Option<TOut>>> readData, Func<TSource, Task> close)
        {
            _create = create;
            _readData = readData;
            _close = close;

            Shape = new SourceShape<TOut>(Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.UnfoldResourceSourceAsync;

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("UnfoldResourceSourceAsync.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override SourceShape<TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this, inheritedAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "UnfoldResourceSourceAsync";
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// A graph stage that can be used to integrate Akka.Streams with .NET events.
    /// </summary>
    /// <typeparam name="TEventArgs"></typeparam>
    /// <typeparam name="TDelegate">Delegate</typeparam>
    public sealed class EventSourceStage<TDelegate, TEventArgs> : GraphStage<SourceShape<TEventArgs>>
    {
        #region logic

        private class Logic : OutGraphStageLogic
        {
            private readonly EventSourceStage<TDelegate, TEventArgs> _stage;
            private readonly LinkedList<TEventArgs> _buffer;
            private readonly TDelegate _handler;
            private readonly Action<TEventArgs> _onOverflow;

            public Logic(EventSourceStage<TDelegate, TEventArgs> stage) : base(stage.Shape)
            {
                _stage = stage;
                _buffer = new LinkedList<TEventArgs>();
                var bufferCapacity = stage._maxBuffer;
                var onEvent = GetAsyncCallback<TEventArgs>(e =>
                {
                    if (IsAvailable(_stage.Out))
                    {
                        Push(_stage.Out, e);
                    }
                    else
                    {
                        if (_buffer.Count >= bufferCapacity) _onOverflow(e);
                        else Enqueue(e);
                    }
                });

                _handler = _stage._conversion(onEvent);
                _onOverflow = SetupOverflowStrategy(stage._overflowStrategy);
                SetHandler(stage.Out, this);
            }
            private void Enqueue(TEventArgs e) => _buffer.AddLast(e);

            private TEventArgs Dequeue()
            {
                var element = _buffer.First.Value;
                _buffer.RemoveFirst();
                return element;
            }

            public override void OnPull()
            {
                if (_buffer.Count > 0)
                {
                    var element = Dequeue();
                    Push(_stage.Out, element);
                }
            }

            public override void PreStart()
            {
                base.PreStart();
                _stage._addHandler(_handler);
            }

            public override void PostStop()
            {
                _stage._removeHandler(_handler);
                _buffer.Clear();
                base.PostStop();
            }


            private Action<TEventArgs> SetupOverflowStrategy(OverflowStrategy overflowStrategy)
            {
                switch (overflowStrategy)
                {
                    case OverflowStrategy.DropHead:
                        return message =>
                        {
                            _buffer.RemoveFirst();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropTail:
                        return message =>
                        {
                            _buffer.RemoveLast();
                            Enqueue(message);
                        };
                    case OverflowStrategy.DropNew:
                        return message =>
                        {
                            // do nothing
                        };
                    case OverflowStrategy.DropBuffer:
                        return message =>
                        {
                            _buffer.Clear();
                            Enqueue(message);
                        };
                    case OverflowStrategy.Fail:
                        return message =>
                        {
                            FailStage(new BufferOverflowException($"{_stage.Out} buffer has been overflown"));
                        };
                    case OverflowStrategy.Backpressure:
                        return message =>
                        {
                            throw new NotSupportedException("OverflowStrategy.Backpressure is not supported");
                        };
                    default: throw new NotSupportedException($"Unknown option: {overflowStrategy}");
                }
            }
        }

        #endregion

        private readonly Func<Action<TEventArgs>, TDelegate> _conversion;
        private readonly Action<TDelegate> _addHandler;
        private readonly Action<TDelegate> _removeHandler;
        private readonly int _maxBuffer;
        private readonly OverflowStrategy _overflowStrategy;

        public Outlet<TEventArgs> Out { get; } = new Outlet<TEventArgs>("event.out");

        /// <summary>
        /// Creates a new instance of event source class.
        /// </summary>
        /// <param name="conversion">Function used to convert given event handler to delegate compatible with underlying .NET event.</param>
        /// <param name="addHandler">Action which attaches given event handler to the underlying .NET event.</param>
        /// <param name="removeHandler">Action which detaches given event handler to the underlying .NET event.</param>
        /// <param name="maxBuffer">Maximum size of the buffer, used in situation when amount of emitted events is higher than current processing capabilities of the downstream.</param>
        /// <param name="overflowStrategy">Overflow strategy used, when buffer (size specified by <paramref name="maxBuffer"/>) has been overflown.</param>
        public EventSourceStage(Action<TDelegate> addHandler,  Action<TDelegate> removeHandler, Func<Action<TEventArgs>, TDelegate> conversion, int maxBuffer, OverflowStrategy overflowStrategy)
        {
            _conversion = conversion ?? throw new ArgumentNullException(nameof(conversion));
            _addHandler = addHandler ?? throw new ArgumentNullException(nameof(addHandler));
            _removeHandler = removeHandler ?? throw new ArgumentNullException(nameof(removeHandler));
            _maxBuffer = maxBuffer;
            _overflowStrategy = overflowStrategy;
            Shape = new SourceShape<TEventArgs>(Out);
        }

        public override SourceShape<TEventArgs> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }
}