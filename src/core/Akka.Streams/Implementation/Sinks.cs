//-----------------------------------------------------------------------
// <copyright file="Sinks.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    internal interface ISinkModule
    {
        Shape Shape { get; }
        object Create(MaterializationContext context, out object materializer);
    }

    public abstract class SinkModule<TIn, TMat> : AtomicModule, ISinkModule
    {
        private readonly SinkShape<TIn> _shape;

        protected SinkModule(SinkShape<TIn> shape)
        {
            _shape = shape;
        }

        public override Shape Shape => _shape;

        protected virtual string Label => GetType().Name;

        public sealed override string ToString() => $"{Label} [{GetHashCode()}%08x]";

        protected abstract SinkModule<TIn, TMat> NewInstance(SinkShape<TIn> shape);

        /// <summary>
        /// Create the Subscriber or VirtualPublisher that consumes the incoming
        /// stream, plus the materialized value. Since Subscriber and VirtualPublisher
        /// do not share a common supertype apart from AnyRef this is what the type
        /// union devolves into; unfortunately we do not have union types at our
        /// disposal at this point.
        /// </summary>
        public abstract object Create(MaterializationContext context, out TMat materializer);

        object ISinkModule.Create(MaterializationContext context, out object materializer)
        {
            TMat m;
            var result = Create(context, out m);
            materializer = m;
            return result;
        }

        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(_shape, shape))
                return this;

            throw new NotSupportedException("cannot replace the shape of a Sink, you need to wrap it in a Graph for that");
        }

        public override IModule CarbonCopy()
            => NewInstance(new SinkShape<TIn>(Inlet.Create<TIn>(_shape.Inlet.CarbonCopy())));

        protected SinkShape<TIn> AmendShape(Attributes attrs)
        {
            var thisN = Attributes.GetNameOrDefault(null);
            var thatN = attrs.GetNameOrDefault(null);

            return (thatN == null) || thisN == thatN
                ? _shape
                : new SinkShape<TIn>(new Inlet<TIn>(thatN + ".in"));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Holds the downstream-most <see cref="IPublisher{T}"/> interface of the materialized flow.
    /// The stream will not have any subscribers attached at this point, which means that after prefetching
    /// elements to fill the internal buffers it will assert back-pressure until
    /// a subscriber connects and creates demand for elements to be emitted.
    /// </summary>
    internal class PublisherSink<TIn> : SinkModule<TIn, IPublisher<TIn>>
    {
        public PublisherSink(Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes) 
            => new PublisherSink<TIn>(attributes, AmendShape(attributes));

        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape) 
            => new PublisherSink<TIn>(Attributes, shape);

        /// <summary>
        /// This method is the reason why SinkModule.create may return something that is
        /// not a Subscriber: a VirtualPublisher is used in order to avoid the immediate
        /// subscription a VirtualProcessor would perform (and it also saves overhead).
        /// </summary>
        public override object Create(MaterializationContext context, out IPublisher<TIn> materializer)
        {
            var processor = new VirtualProcessor<TIn>();
            materializer = processor;
            return processor;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FanoutPublisherSink<TIn> : SinkModule<TIn, IPublisher<TIn>>
    {
        public FanoutPublisherSink(Attributes attributes, SinkShape<TIn> shape) : base(shape)
        {
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new FanoutPublisherSink<TIn>(attributes, AmendShape(attributes));

        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
            => new FanoutPublisherSink<TIn>(Attributes, shape);

        public override object Create(MaterializationContext context, out IPublisher<TIn> materializer)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var settings = actorMaterializer.EffectiveSettings(Attributes);
            var impl = actorMaterializer.ActorOf(context, FanoutProcessorImpl<TIn>.Props(settings));
            var fanoutProcessor = new ActorProcessor<TIn,TIn>(impl);
            impl.Tell(new ExposedPublisher(fanoutProcessor));
            // Resolve cyclic dependency with actor. This MUST be the first message no matter what.
            materializer = fanoutProcessor;
            return fanoutProcessor;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Attaches a subscriber to this stream which will just discard all received elements.
    /// </summary>
    internal sealed class SinkholeSink<TIn> : SinkModule<TIn, Task>
    {
        public SinkholeSink(SinkShape<TIn> shape, Attributes attributes) : base(shape)
        {
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new SinkholeSink<TIn>(AmendShape(attributes), attributes);

        protected override SinkModule<TIn, Task> NewInstance(SinkShape<TIn> shape)
            => new SinkholeSink<TIn>(shape, Attributes);

        public override object Create(MaterializationContext context, out Task materializer)
        {
            var p = new TaskCompletionSource<NotUsed>();
            materializer = p.Task;
            return new SinkholeSubscriber<TIn>(p);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Attaches a subscriber to this stream.
    /// </summary>
    internal sealed class SubscriberSink<TIn> : SinkModule<TIn, NotUsed>
    {
        private readonly ISubscriber<TIn> _subscriber;

        public SubscriberSink(ISubscriber<TIn> subscriber, Attributes attributes, SinkShape<TIn> shape) : base(shape)
        {
            Attributes = attributes;
            _subscriber = subscriber;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSink<TIn>(_subscriber, attributes, AmendShape(attributes));

        protected override SinkModule<TIn, NotUsed> NewInstance(SinkShape<TIn> shape)
            => new SubscriberSink<TIn>(_subscriber, Attributes, shape);

        public override object Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return _subscriber;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// A sink that immediately cancels its upstream upon materialization.
    /// </summary>
    internal sealed class CancelSink<T> : SinkModule<T, NotUsed>
    {
        public CancelSink(Attributes attributes, SinkShape<T> shape)
            : base(shape)
        {
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        protected override SinkModule<T, NotUsed> NewInstance(SinkShape<T> shape) => new CancelSink<T>(Attributes, shape);

        public override object Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return new CancellingSubscriber<T>();
        }

        public override IModule WithAttributes(Attributes attributes)
            => new CancelSink<T>(attributes, AmendShape(attributes));
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Creates and wraps an actor into <see cref="ISubscriber{T}"/> from the given <see cref="Props"/>,
    /// which should be <see cref="Props"/> for an <see cref="ActorSubscriber"/>.
    /// </summary>
    internal sealed class ActorSubscriberSink<TIn> : SinkModule<TIn, IActorRef>
    {
        private readonly Props _props;
        private readonly Attributes _attributes;

        public ActorSubscriberSink(Props props, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _props = props;
            _attributes = attributes;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new ActorSubscriberSink<TIn>(_props, attributes, AmendShape(attributes));

        protected override SinkModule<TIn, IActorRef> NewInstance(SinkShape<TIn> shape)
            => new ActorSubscriberSink<TIn>(_props, _attributes, shape);

        public override object Create(MaterializationContext context, out IActorRef materializer)
        {
            var subscriberRef = ActorMaterializer.Downcast(context.Materializer).ActorOf(context, _props);
            materializer = subscriberRef;
            return ActorSubscriber.Create<TIn>(subscriberRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefSink<TIn> : SinkModule<TIn, NotUsed>
    {
        private readonly IActorRef _ref;
        private readonly object _onCompleteMessage;
        private readonly Attributes _attributes;

        public ActorRefSink(IActorRef @ref, object onCompleteMessage, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _ref = @ref;
            _onCompleteMessage = onCompleteMessage;
            _attributes = attributes;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new ActorRefSink<TIn>(_ref, _onCompleteMessage, attributes, AmendShape(attributes));

        protected override SinkModule<TIn, NotUsed> NewInstance(SinkShape<TIn> shape)
            => new ActorRefSink<TIn>(_ref, _onCompleteMessage, _attributes, shape);

        public override object Create(MaterializationContext context, out NotUsed materializer)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var effectiveSettings = actorMaterializer.EffectiveSettings(context.EffectiveAttributes);
            var subscriberRef = actorMaterializer.ActorOf(context,
                ActorRefSinkActor.Props(_ref, effectiveSettings.MaxInputBufferSize, _onCompleteMessage));

            materializer = null;
            return new ActorSubscriberImpl<TIn>(subscriberRef);
        }
    }

    internal sealed class LastOrDefaultStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<T>>
    {
        #region stage logic
        private sealed class Logic : GraphStageLogic
        {
            private readonly LastOrDefaultStage<T> _stage;

            public Logic(TaskCompletionSource<T> promise, LastOrDefaultStage<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                var prev = default(T);

                SetHandler(stage.In, onPush: () =>
                {
                    prev = Grab(stage.In);
                    Pull(stage.In);
                },
                onUpstreamFinish: () =>
                {
                    var head = prev;
                    prev = default(T);
                    promise.TrySetResult(head);
                    CompleteStage();
                },
                onUpstreamFailure: cause =>
                {
                    prev = default(T);
                    promise.TrySetException(cause);
                    FailStage(cause);
                });
            }

            public override void PreStart() => Pull(_stage.In);
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("LastOrDefault.in");

        public LastOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<T>();
            return new LogicAndMaterializedValue<Task<T>>(new Logic(promise, this), promise.Task);
        }
    }

    internal sealed class FirstOrDefaultStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<T>>
    {
        #region stage logic
        private sealed class Logic : GraphStageLogic
        {
            private readonly FirstOrDefaultStage<T> _stage;

            public Logic(TaskCompletionSource<T> promise, FirstOrDefaultStage<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage.In, onPush: () =>
                {
                    promise.TrySetResult(Grab(stage.In));
                    CompleteStage();
                },
                onUpstreamFinish: () =>
                {
                    promise.TrySetResult(default(T));
                    CompleteStage();
                },
                onUpstreamFailure: cause =>
                {
                    promise.TrySetException(cause);
                    FailStage(cause);
                });
            }

            public override void PreStart() => Pull(_stage.In);
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("FirstOrDefault.in");

        public FirstOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<T>();
            return new LogicAndMaterializedValue<Task<T>>(new Logic(promise, this), promise.Task);
        }
    }

    internal sealed class SeqStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<IImmutableList<T>>>
    {
        #region stage logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly SeqStage<T> _stage;
            private IImmutableList<T> _buf = ImmutableList<T>.Empty; 

            public Logic(SeqStage<T> stage, TaskCompletionSource<IImmutableList<T>> promise) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._in, onPush: () =>
                {
                    _buf = _buf.Add(Grab(stage._in));
                    Pull(stage._in);
                }, onUpstreamFinish: () =>
                {
                    promise.TrySetResult(_buf);
                    CompleteStage();
                }, onUpstreamFailure: ex =>
                {
                    promise.TrySetException(ex);
                    FailStage(ex);
                });
            }

            public override void PreStart() => Pull(_stage._in);
        }

        #endregion

        private readonly Inlet<T> _in = new Inlet<T>("Seq.in");

        public SeqStage()
        {
            Shape = new SinkShape<T>(_in);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.SeqSink;

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<Task<IImmutableList<T>>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<IImmutableList<T>>();
            return new LogicAndMaterializedValue<Task<IImmutableList<T>>>(new Logic(this, promise), promise.Task);
        }

        public override string ToString() => "SeqStage";
    }

    internal class QueueSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, ISinkQueue<T>>
    {
        #region stage logic
        private sealed class Logic : GraphStageLogicWithCallbackWrapper<TaskCompletionSource<Option<T>>>
        {
            private readonly QueueSink<T> _stage;
            private readonly int _maxBuffer;
            private IBuffer<Result<Option<T>>> _buffer;
            private Option<TaskCompletionSource<Option<T>>>_currentRequest;

            public Logic(QueueSink<T> stage, int maxBuffer) : base(stage.Shape)
            {
                _stage = stage;
                _maxBuffer = maxBuffer;
                _currentRequest = new Option<TaskCompletionSource<Option<T>>>();

                SetHandler(_stage.In,
                    onPush: () =>
                    {
                        EnqueueAndNotify(new Result<Option<T>>(Grab(_stage.In)));
                        if (_buffer.Used < _maxBuffer) Pull(_stage.In);
                    },
                    onUpstreamFinish: () => EnqueueAndNotify(new Result<Option<T>>(Option<T>.None)),
                    onUpstreamFailure: ex => EnqueueAndNotify(new Result<Option<T>>(ex)));
            }

            public override void PreStart()
            {
                // Allocates one additional element to hold stream closed/failure indicators
                _buffer = Buffer.Create<Result<Option<T>>>(_maxBuffer + 1, Materializer);
                SetKeepGoing(true);
                InitCallback(Callback());
                Pull(_stage.In);
            }

            public override void PostStop()
            {
                StopCallback(promise => promise.SetException(new IllegalStateException("Stream is terminated. QueueSink is detached")));
            }

            private Action<TaskCompletionSource<Option<T>>> Callback()
            {
                return GetAsyncCallback<TaskCompletionSource<Option<T>>>(
                    promise =>
                    {
                        if (_currentRequest.HasValue)
                            promise.SetException(
                                new IllegalStateException(
                                    "You have to wait for previous future to be resolved to send another request"));
                        else
                        {
                            if (_buffer.IsEmpty)
                                _currentRequest = promise;
                            else
                            {
                                if (_buffer.Used == _maxBuffer)
                                    TryPull(_stage.In);
                                SendDownstream(promise);
                            }
                        }
                    });
            }

            private void SendDownstream(TaskCompletionSource<Option<T>> promise)
            {
                var e = _buffer.Dequeue();
                if (e.IsSuccess)
                {
                    promise.SetResult(e.Value);
                    if (!e.Value.HasValue)
                        CompleteStage();
                }
                else
                {
                    promise.SetException(e.Exception);
                    FailStage(e.Exception);
                }
            }

            private void EnqueueAndNotify(Result<Option<T>> requested)
            {
                _buffer.Enqueue(requested);
                if (_currentRequest.HasValue)
                {
                    SendDownstream(_currentRequest.Value);
                    _currentRequest = Option<TaskCompletionSource<Option<T>>>.None;
                }
            }

            internal void Invoke(TaskCompletionSource<Option<T>> tuple) => InvokeCallbacks(tuple);
        }

        private sealed class Materialized : ISinkQueue<T>
        {
            private readonly Action<TaskCompletionSource<Option<T>>> _invokeLogic;

            public Materialized(Action<TaskCompletionSource<Option<T>>> invokeLogic)
            {
                _invokeLogic = invokeLogic;
            }

            public Task<Option<T>> PullAsync()
            {
                var promise = new TaskCompletionSource<Option<T>>();
                _invokeLogic(promise);
                return promise.Task;
            }
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("QueueSink.in");

        public QueueSink()
        {
            Shape = new SinkShape<T>(In);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.QueueSink;

        public override SinkShape<T> Shape { get; }

        public override ILogicAndMaterializedValue<ISinkQueue<T>>  CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer must be greater than zero", nameof(inheritedAttributes));

            var logic = new Logic(this, maxBuffer);
            return new LogicAndMaterializedValue<ISinkQueue<T>>(logic, new Materialized(t => logic.Invoke(t)));
        }

        public override string ToString() => "QueueSink";
    }
}