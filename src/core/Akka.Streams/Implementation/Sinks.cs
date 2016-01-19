using System;
using System.Collections.Immutable;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    public abstract class SinkModule<TIn, TMat> : Module
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

        public SubscriberSink(SinkShape<TIn> shape, Attributes attributes, ISubscriber<TIn> subscriber) : base(shape)
        {
            _attributes = attributes;
            _subscriber = subscriber;
        }

        public override Attributes Attributes => _attributes;

        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSink<TIn>(AmendShape(attributes), attributes, _subscriber);

        protected override SinkModule<TIn, Unit> NewInstance(SinkShape<TIn> shape)
            => new SubscriberSink<TIn>(shape, Attributes, _subscriber);

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

}