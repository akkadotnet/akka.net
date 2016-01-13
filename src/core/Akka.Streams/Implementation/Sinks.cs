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

    internal sealed class FanoutPublisherSink<TIn> : SinkModule<TIn, IPublisher<TIn>>
    {
        public readonly int InitialBufferSize;
        public readonly int MaxBufferSize;
        private readonly Attributes _attributes;
        public FanoutPublisherSink(int initialBufferSize, int maxBufferSize, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            InitialBufferSize = initialBufferSize;
            MaxBufferSize = maxBufferSize;
            _attributes = attributes;
        }

        public override Attributes Attributes { get { return _attributes; } }
        
        public override IModule WithAttributes(Attributes attributes)
        {
            return new FanoutPublisherSink<TIn>(InitialBufferSize, MaxBufferSize, attributes, AmendShape(attributes));
        }

        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
        {
            return new FanoutPublisherSink<TIn>(InitialBufferSize, MaxBufferSize, Attributes, shape);
        }

        public override ISubscriber<TIn> Create(MaterializationContext context, out IPublisher<TIn> materializer)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var fanoutRef = actorMaterializer.ActorOf(context, Props.Create(() =>
                new FanoutProcessorImpl(actorMaterializer.EffectiveSettings(context.EffectiveAttributes),
                    InitialBufferSize, MaxBufferSize)).WithDeploy(Deploy.Local));
            var fanoutProcessor = ActorProcessorFactory.Create<TIn, TIn>(fanoutRef);
            materializer = fanoutProcessor;
            return fanoutProcessor;
        }
    }

    /**
     * INTERNAL API
     * Holds a [[scala.concurrent.Future]] that will be fulfilled with the first
     * thing that is signaled to this stream, which can be either an element (after
     * which the upstream subscription is canceled), an error condition (putting
     * the Future into the corresponding failed state) or the end-of-stream
     * (failing the Future with a NoSuchElementException).
     */
    public sealed class HeadSink<TIn> : SinkModule<TIn, Task<TIn>>
    {
        #region subscriber

        private sealed class HeadSinkSubscriber : ISubscriber<TIn>
        {
            private readonly TaskCompletionSource<TIn> _promise = new TaskCompletionSource<TIn>();
            private ISubscription _subscription = null;

            public Task<TIn> Task { get { return _promise.Task; } }

            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                if (_subscription != null) subscription.Cancel();
                else
                {
                    _subscription = subscription;
                    _subscription.Request(1);
                }
            }

            public void OnNext(TIn element)
            {
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                _promise.TrySetResult(element);
                _subscription.Cancel();
                _subscription = null;
            }

            public void OnNext(object element)
            {
                OnNext((TIn)element);
            }

            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                _promise.TrySetException(cause);
            }

            public void OnComplete()
            {
                _promise.TrySetException(new Exception("empty stream"));
            }
        }

        #endregion

        private readonly Attributes _attributes;

        public HeadSink(Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _attributes = attributes;
        }

        public override IModule CarbonCopy()
        {
            return new HeadSink<TIn>(Attributes, (SinkShape<TIn>)Shape);
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new HeadSink<TIn>(attributes, (SinkShape<TIn>)Shape);
        }

        public override Tuple<ISubscriber<TIn>, Task<TIn>> Create(MaterializationContext context)
        {
            var sub = new HeadSinkSubscriber();
            return new Tuple<ISubscriber<TIn>, Task<TIn>>(sub, sub.Task);
        }
    }

    /**
     * INTERNAL API
     * Attaches a subscriber to this stream which will just discard all received
     * elements.
     */
    public sealed class BlackholeSink : SinkModule<object, Task>
    {
        private readonly Attributes _attributes;

        public BlackholeSink(Attributes attributes, SinkShape<object> shape)
            : base(shape)
        {
            _attributes = attributes;
        }

        public override IModule CarbonCopy()
        {
            return new BlackholeSink(Attributes, (SinkShape<object>)Shape);
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new BlackholeSink(attributes, (SinkShape<object>)Shape);
        }

        public override Tuple<ISubscriber<object>, Task> Create(MaterializationContext context)
        {
            var effectiveSettings =
                ActorMaterializer.Downcast(context.Materializer).EffectiveSettings(context.EffectiveAttributes);

            var promise = new TaskCompletionSource<object>();
            return new Tuple<ISubscriber<object>, Task>(new BlackholeSubscriber<object>(effectiveSettings.MaxInputBufferSize, promise), promise.Task);
        }
    }

    /**
     * INTERNAL API
     * Attaches a subscriber to this stream.
     */
    public sealed class SubscriberSink<TIn> : SinkModule<TIn, object>
    {
        public readonly ISubscriber<TIn> Subscriber;
        private readonly Attributes _attributes;

        public SubscriberSink(ISubscriber<TIn> subscriber, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            Subscriber = subscriber;
            _attributes = attributes;
        }

        public override IModule CarbonCopy()
        {
            return new SubscriberSink<TIn>(Subscriber, Attributes, (SinkShape<TIn>)Shape);
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new SubscriberSink<TIn>(Subscriber, attributes, (SinkShape<TIn>)Shape);
        }

        public override Tuple<ISubscriber<TIn>, object> Create(MaterializationContext context)
        {
            return new Tuple<ISubscriber<TIn>, object>(Subscriber, null);
        }
    }

    /**
     * INTERNAL API
     * A sink that immediately cancels its upstream upon materialization.
     */
    public sealed class CancelSink<T> : SinkModule<T, Unit>
    {
        private readonly Attributes _attributes;

        public CancelSink(Attributes attributes, SinkShape<T> shape)
            : base(shape)
        {
            _attributes = attributes;
        }

        public override Attributes Attributes { get { return _attributes; } }

        protected override SinkModule<T, Unit> NewInstance(SinkShape<T> shape)
        {
            return new CancelSink<T>(Attributes, shape);
        }

        public override ISubscriber<T> Create(MaterializationContext context, out Unit materializer)
        {
            materializer = Unit.Instance;
            return new CancellingSubscriber<T>();
        }
        
        public override IModule WithAttributes(Attributes attributes)
        {
            return new CancelSink<T>(attributes, (SinkShape<T>)Shape);
        }
        
    }

    /**
     * INTERNAL API
     * Creates and wraps an actor into [[org.reactivestreams.Subscriber]] from the given `props`,
     * which should be [[akka.actor.Props]] for an [[akka.stream.actor.ActorSubscriber]].
     */
    public sealed class ActorSubscriberSink<TIn> : SinkModule<TIn, IActorRef>
    {
        public readonly Props Props;
        private readonly Attributes _attributes;

        public ActorSubscriberSink(Props props, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            Props = props;
            _attributes = attributes;
        }

        public override IModule CarbonCopy()
        {
            return new ActorSubscriberSink<TIn>(Props, Attributes, (SinkShape<TIn>)Shape);
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new ActorSubscriberSink<TIn>(Props, attributes, (SinkShape<TIn>)Shape);
        }

        public override Tuple<ISubscriber<TIn>, IActorRef> Create(MaterializationContext context)
        {
            var subscriberRef = ActorMaterializer.Downcast(context.Materializer).ActorOf(context, Props);
            return new Tuple<ISubscriber<TIn>, IActorRef>(new ActorSubscriberImpl<TIn>(subscriberRef), subscriberRef);
        }
    }

    public sealed class ActorRefSink<TIn> : SinkModule<TIn, object>
    {
        public readonly IActorRef Ref;
        public readonly object OnCompleteMessage;
        private readonly Attributes _attributes;

        public ActorRefSink(IActorRef @ref, object onCompleteMessage, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            Ref = @ref;
            OnCompleteMessage = onCompleteMessage;
            _attributes = attributes;
        }

        public override IModule CarbonCopy()
        {
            return new ActorRefSink<TIn>(Ref, OnCompleteMessage, Attributes, (SinkShape<TIn>)Shape);
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new ActorRefSink<TIn>(Ref, OnCompleteMessage, attributes, (SinkShape<TIn>)Shape);
        }

        public override Tuple<ISubscriber<TIn>, object> Create(MaterializationContext context)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var effectiveSettings = actorMaterializer.EffectiveSettings(context.EffectiveAttributes);
            var subscriberRef = actorMaterializer.ActorOf(context,
                ActorRefSinkActor.Props(Ref, effectiveSettings.MaxInputBufferSize, OnCompleteMessage));

            return new Tuple<ISubscriber<TIn>, object>(new ActorSubscriberImpl<TIn>(subscriberRef), null);
        }
    }

    public sealed class AcknowledgeSink<TIn> : SinkModule<TIn, ISinkQueue<TIn>>
    {
        private sealed class SinkQueue : ISinkQueue<TIn>
        {
            private readonly IActorRef _subscriberRef;

            public SinkQueue(IActorRef subscriberRef)
            {
                _subscriberRef = subscriberRef;
            }

            public Task<TIn> Pull()
            {
                return _subscriberRef.Ask<TIn>(new Request(1));
            }
        }

        public readonly int BufferSize;
        public readonly TimeSpan Timeout;
        private readonly Attributes _attributes;

        public AcknowledgeSink(int bufferSize, TimeSpan timeout, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            BufferSize = bufferSize;
            Timeout = timeout;
            _attributes = attributes;
        }

        public override IModule CarbonCopy()
        {
            return new AcknowledgeSink<TIn>(BufferSize, Timeout, Attributes, (SinkShape<TIn>)Shape);
        }

        public override Attributes Attributes { get { return _attributes; } }

        public override IModule WithAttributes(Attributes attributes)
        {
            return new AcknowledgeSink<TIn>(BufferSize, Timeout, attributes, (SinkShape<TIn>)Shape);
        }

        public override Tuple<ISubscriber<TIn>, ISinkQueue<TIn>> Create(MaterializationContext context)
        {
            var actorMaterializer = ActorMaterializer.Downcast(context.Materializer);
            var subscriberRef = actorMaterializer.ActorOf(context, AcknowledgeSubscriber.Props(BufferSize));

            return new Tuple<ISubscriber<TIn>, ISinkQueue<TIn>>(new ActorSubscriberImpl<TIn>(subscriberRef), new SinkQueue(subscriberRef));
        }
    }
}