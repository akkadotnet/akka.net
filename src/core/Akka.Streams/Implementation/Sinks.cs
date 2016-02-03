using System;
using System.Collections.Immutable;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Actors;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Implementation
{
    internal interface ISinkModule
    {
        Shape Shape { get; }
        ISubscriber Create(MaterializationContext context, out object materializer);
    }

    public abstract class SinkModule<TIn, TMat> : Module, ISinkModule
    {
        private readonly SinkShape<TIn> _shape;

        protected SinkModule(SinkShape<TIn> shape)
        {
            _shape = shape;
        }

        public override Shape Shape { get { return _shape; } }
        public override IImmutableSet<IModule> SubModules { get { return ImmutableHashSet<IModule>.Empty; } }

        protected abstract SinkModule<TIn, TMat> NewInstance(SinkShape<TIn> shape);

        public abstract ISubscriber<TIn> Create(MaterializationContext context, out TMat materializer);

        ISubscriber ISinkModule.Create(MaterializationContext context, out object materializer)
        {
            TMat m;
            var result = Create(context, out m);
            materializer = m;
            return result;
        }

        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(_shape, shape)) return this;
            else throw new NotSupportedException("cannot replace the shape of a Sink, you need to wrap it in a Graph for that");
        }

        public override IModule CarbonCopy()
        {
            return NewInstance(new SinkShape<TIn>(Inlet.Create<TIn>(_shape.Inlet.CarbonCopy())));
        }

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
    /// Holds the downstream-most <see cref="IPublisher{T}"/> interface of the materialized flow.
    /// The stream will not have any subscribers attached at this point, which means that after prefetching
    /// elements to fill the internal buffers it will assert back-pressure until
    /// a subscriber connects and creates demand for elements to be emitted.
    /// </summary>
    internal class PublisherSink<TIn> : SinkModule<TIn, IPublisher<TIn>>
    {
        private readonly Attributes _attributes;
        private readonly SinkShape<TIn> _shape;

        public PublisherSink(Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _attributes = attributes;
            _shape = shape;
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new PublisherSink<TIn>(attributes, AmendShape(attributes));
        }

        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
        {
            return new PublisherSink<TIn>(_attributes, shape);
        }

        public override ISubscriber<TIn> Create(MaterializationContext context, out IPublisher<TIn> materializer)
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
        private readonly Attributes _attributes;

        public FanoutPublisherSink(Attributes attributes, SinkShape<TIn> shape) : base(shape)
        {
            _attributes = attributes;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new FanoutPublisherSink<TIn>(attributes, AmendShape(attributes));

        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
            => new FanoutPublisherSink<TIn>(Attributes, shape);

        public override ISubscriber<TIn> Create(MaterializationContext context, out IPublisher<TIn> materializer)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var settings = actorMaterializer.EffectiveSettings(context.EffectiveAttributes);
            var fanoutRef = actorMaterializer.ActorOf(context, FanoutProcessorImpl<TIn>.Props(settings));
            var fanoutProcessor = ActorProcessorFactory.Create<TIn, TIn>(fanoutRef);
            materializer = fanoutProcessor;
            return fanoutProcessor;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Attaches a subscriber to this stream which will just discard all received elements.
    /// </summary>
    internal sealed class SinkholeSink<TIn> : SinkModule<TIn, Task>
    {
        private readonly Attributes _attributes;

        public SinkholeSink(SinkShape<TIn> shape, Attributes attributes) : base(shape)
        {
            _attributes = attributes;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new SinkholeSink<TIn>(AmendShape(attributes), attributes);

        protected override SinkModule<TIn, Task> NewInstance(SinkShape<TIn> shape)
            => new SinkholeSink<TIn>(shape, Attributes);

        public override ISubscriber<TIn> Create(MaterializationContext context, out Task materializer)
        {
            var effectiveSettings = ActorMaterializer.Downcast(context.Materializer).EffectiveSettings(context.EffectiveAttributes);
            var p = new TaskCompletionSource<Unit>();
            materializer = p.Task;
            return new SinkholeSubscriber<TIn>(p);
        }

        public override string ToString() => "SinkholeSink";
    }

    /// <summary>
    /// INTERNAL API
    /// Attaches a subscriber to this stream.
    /// </summary>
    internal sealed class SubscriberSink<TIn> : SinkModule<TIn, Unit>
    {
        private readonly Attributes _attributes;
        private readonly ISubscriber<TIn> _subscriber;

        public SubscriberSink(ISubscriber<TIn> subscriber, Attributes attributes, SinkShape<TIn> shape) : base(shape)
        {
            _attributes = attributes;
            _subscriber = subscriber;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSink<TIn>(_subscriber, attributes, AmendShape(attributes));

        protected override SinkModule<TIn, Unit> NewInstance(SinkShape<TIn> shape)
            => new SubscriberSink<TIn>(_subscriber, Attributes, shape);

        public override ISubscriber<TIn> Create(MaterializationContext context, out Unit materializer)
        {
            materializer = Unit.Instance;
            return _subscriber;
        }

        public override string ToString() => "SubscriberSink";
    }

    /// <summary>
    /// INTERNAL API
    /// A sink that immediately cancels its upstream upon materialization.
    /// </summary>
    internal sealed class CancelSink<T> : SinkModule<T, Unit>
    {
        private readonly Attributes _attributes;

        public CancelSink(Attributes attributes, SinkShape<T> shape)
            : base(shape)
        {
            _attributes = attributes;
        }

        public override Attributes Attributes => _attributes;

        protected override SinkModule<T, Unit> NewInstance(SinkShape<T> shape) => new CancelSink<T>(Attributes, shape);

        public override ISubscriber<T> Create(MaterializationContext context, out Unit materializer)
        {
            materializer = Unit.Instance;
            return new CancellingSubscriber<T>();
        }

        public override IModule WithAttributes(Attributes attributes)
            => new CancelSink<T>(attributes, AmendShape(attributes));

        public override string ToString() => "CancelSink";
    }

    /// <summary>
    /// INTERNAL API
    /// Creates and wraps an actor into <see cref="ISubscriber{T}"/> from the given <see cref="Actor.Props"/>,
    /// which should be <see cref="Actor.Props"/> for an <see cref="ActorSubscriber"/>.
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

        public override ISubscriber<TIn> Create(MaterializationContext context, out IActorRef materializer)
        {
            var subscriberRef = ActorMaterializer.Downcast(context.Materializer).ActorOf(context, _props);
            materializer = subscriberRef;
            return new ActorSubscriberImpl<TIn>(subscriberRef);
        }

        public override string ToString() => "ActorSubscriberSink";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ActorRefSink<TIn> : SinkModule<TIn, IActorRef>
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

        protected override SinkModule<TIn, IActorRef> NewInstance(SinkShape<TIn> shape)
            => new ActorRefSink<TIn>(_ref, _onCompleteMessage, _attributes, shape);

        public override ISubscriber<TIn> Create(MaterializationContext context, out IActorRef materializer)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var effectiveSettings = actorMaterializer.EffectiveSettings(context.EffectiveAttributes);
            var subscriberRef = actorMaterializer.ActorOf(context,
                ActorRefSinkActor.Props(_ref, effectiveSettings.MaxInputBufferSize, _onCompleteMessage));

            materializer = subscriberRef;
            return new ActorSubscriberImpl<TIn>(subscriberRef);
        }

        public override string ToString() => "ActorRefSink";
    }

    internal sealed class LastOrDefaultStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<T>>
    {
        #region stage logic
        private sealed class LastOrDefaultLogic : GraphStageLogic
        {
            private readonly LastOrDefaultStage<T> _stage;

            public LastOrDefaultLogic(Shape shape, TaskCompletionSource<T> promise, LastOrDefaultStage<T> stage) : base(shape)
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

            public override void PreStart()
            {
                Pull(_stage.In);
            }
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("LastOrDefault.in");

        public LastOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        public override SinkShape<T> Shape { get; }
        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<T> materialized)
        {
            var promise = new TaskCompletionSource<T>();
            materialized = promise.Task;
            return new LastOrDefaultLogic(Shape, promise, this);
        }
    }

    internal sealed class FirstOrDefaultStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<T>>
    {
        #region stage logic
        private sealed class FirstOrDefaultLogic : GraphStageLogic
        {
            private readonly FirstOrDefaultStage<T> _stage;

            public FirstOrDefaultLogic(Shape shape, TaskCompletionSource<T> promise, FirstOrDefaultStage<T> stage) : base(shape)
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

            public override void PreStart()
            {
                Pull(_stage.In);
            }
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("FirstOrDefault.in");

        public FirstOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        public override SinkShape<T> Shape { get; }
        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<T> materialized)
        {
            var promise = new TaskCompletionSource<T>();
            materialized = promise.Task;
            return new FirstOrDefaultLogic(Shape, promise, this);
        }
    }

    internal class QueueSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, ISinkQueue<T>>
    {
        #region stage logic

        private interface IRequestElementCallback
        {
            AtomicReference<TaskCompletionSource<T>> RequestElement { get; }
        }

        private sealed class QueueStageLogic : GraphStageLogic, IRequestElementCallback
        {
            public AtomicReference<TaskCompletionSource<T>> RequestElement { get; set; }

            private QueueSink<T> _stage;

            public QueueStageLogic(Shape shape, QueueSink<T> stage) : base(shape)
            {
                _stage = stage;
            }

            public override void PreStart()
            {
                throw new NotImplementedException();
            }
        }

        private sealed class SinkQueue : ISinkQueue<T>
        {
            private IRequestElementCallback _stageLogic;

            public SinkQueue(IRequestElementCallback stageLogic)
            {
                _stageLogic = stageLogic;
            }

            public Task<T> PullAsync()
            {
                var reference = _stageLogic.RequestElement;
                var promise = new TaskCompletionSource<T>();

                //TODO

                return promise.Task;
            }
        }
        #endregion

        public readonly Inlet<T> In = new Inlet<T>("QueueSink.in");

        public QueueSink()
        {
            Shape = new SinkShape<T>(In);
        }

        public override SinkShape<T> Shape { get; }
        public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out ISinkQueue<T> materialized)
        {
            var maxBuffer = Module.Attributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0) throw new ArgumentException("Buffer must be greater than zero", "inheritedAttributes");

            var buffer = FixedSizeBuffer.Create<Result<T>>(maxBuffer);
            var currentRequest = default(TaskCompletionSource<T>);  
            
            var stageLogic = new QueueStageLogic(Shape, this);
            materialized = new SinkQueue(stageLogic);
            return stageLogic;
        }
    }
}