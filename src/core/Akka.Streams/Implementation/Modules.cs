//-----------------------------------------------------------------------
// <copyright file="Modules.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Actors;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    internal interface ISourceModule
    {
        Shape Shape { get; }
        IUntypedPublisher Create(MaterializationContext context, out object materializer);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public abstract class SourceModule<TOut, TMat> : AtomicModule, ISourceModule
    {
        private readonly SourceShape<TOut> _shape;

        protected SourceModule(SourceShape<TOut> shape)
        {
            _shape = shape;
        }

        public override Shape Shape => _shape;

        protected virtual string Label => GetType().Name;

        public sealed override string ToString() => $"{Label} [{GetHashCode()}%08x]";

        // This is okay since the only caller of this method is right below.
        protected abstract SourceModule<TOut, TMat> NewInstance(SourceShape<TOut> shape);

        public abstract IPublisher<TOut> Create(MaterializationContext context, out TMat materializer);

        IUntypedPublisher ISourceModule.Create(MaterializationContext context, out object materializer)
        {
            TMat m;
            var result = Create(context, out m);
            materializer = m;
            return UntypedPublisher.FromTyped(result);
        }


        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(shape, Shape))
                return this;

            throw new NotSupportedException("cannot replace the shape of a Source, you need to wrap it in a Graph for that");
        }

        public override IModule CarbonCopy()
            => NewInstance(new SourceShape<TOut>(Outlet.Create<TOut>(_shape.Outlet.CarbonCopy())));

        protected SourceShape<TOut> AmendShape(Attributes attributes)
        {
            var thisN = Attributes.GetNameOrDefault(null);
            var thatN = attributes.GetNameOrDefault(null);

            return thatN == null || thatN == thisN
                ? _shape
                : new SourceShape<TOut>(new Outlet<TOut>(thatN + ".out"));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Holds a `Subscriber` representing the input side of the flow. The `Subscriber` can later be connected to an upstream `Publisher`.
    /// </summary>
    /// <typeparam name="TOut"></typeparam>
    public sealed class SubscriberSource<TOut> : SourceModule<TOut, ISubscriber<TOut>>
    {
        public SubscriberSource(Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSource<TOut>(attributes, AmendShape(attributes));

        protected override SourceModule<TOut, ISubscriber<TOut>> NewInstance(SourceShape<TOut> shape)
            => new SubscriberSource<TOut>(Attributes, shape);

        public override IPublisher<TOut> Create(MaterializationContext context, out ISubscriber<TOut> materializer)
        {
            var processor = new VirtualProcessor<TOut>();
            materializer = processor;
            return processor;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Construct a transformation starting with given publisher. The transformation steps are executed 
    /// by a series of <see cref="IProcessor{T1,T2}"/> instances that mediate the flow of elements 
    /// downstream and the propagation of back-pressure upstream.
    /// </summary>
    /// <typeparam name="TOut"></typeparam>
    public sealed class PublisherSource<TOut> : SourceModule<TOut, NotUsed>
    {
        private readonly IPublisher<TOut> _publisher;

        public PublisherSource(IPublisher<TOut> publisher, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _publisher = publisher;
            Attributes = attributes;

            Label = $"PublisherSource({publisher})";
        }

        public override Attributes Attributes { get; }

        protected override string Label { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new PublisherSource<TOut>(_publisher, attributes, AmendShape(attributes));

        protected override SourceModule<TOut, NotUsed> NewInstance(SourceShape<TOut> shape)
            => new PublisherSource<TOut>(_publisher, Attributes, shape);

        public override IPublisher<TOut> Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return _publisher;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class MaybeSource<TOut> : SourceModule<TOut, TaskCompletionSource<TOut>>
    {
        public MaybeSource(Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new MaybeSource<TOut>(attributes, AmendShape(attributes));

        protected override SourceModule<TOut, TaskCompletionSource<TOut>> NewInstance(SourceShape<TOut> shape)
            => new MaybeSource<TOut>(Attributes, shape);

        public override IPublisher<TOut> Create(MaterializationContext context, out TaskCompletionSource<TOut> materializer)
        {
            materializer = new TaskCompletionSource<TOut>();
            return new MaybePublisher<TOut>(materializer, Attributes.GetNameOrDefault("MaybeSource"));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// Creates and wraps an actor into <see cref="IPublisher{T}"/> from the given <see cref="Props"/>, which should be props for an <see cref="ActorPublisher{T}"/>.
    /// </summary>
    /// <typeparam name="TOut"></typeparam>
    public sealed class ActorPublisherSource<TOut> : SourceModule<TOut, IActorRef>
    {
        private readonly Props _props;

        public ActorPublisherSource(Props props, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _props = props;
            Attributes = attributes;
        }

        public override Attributes Attributes { get; }

        public override IModule WithAttributes(Attributes attributes)
            => new ActorPublisherSource<TOut>(_props, attributes, AmendShape(attributes));

        protected override SourceModule<TOut, IActorRef> NewInstance(SourceShape<TOut> shape)
            => new ActorPublisherSource<TOut>(_props, Attributes, shape);

        public override IPublisher<TOut> Create(MaterializationContext context, out IActorRef materializer)
        {
            var publisherRef = ActorMaterializerHelper.Downcast(context.Materializer).ActorOf(context, _props);
            materializer = publisherRef;
            return new ActorPublisherImpl<TOut>(publisherRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class ActorRefSource<TOut> : SourceModule<TOut, IActorRef>
    {
        private readonly int _bufferSize;
        private readonly OverflowStrategy _overflowStrategy;

        public ActorRefSource(int bufferSize, OverflowStrategy overflowStrategy, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _bufferSize = bufferSize;
            _overflowStrategy = overflowStrategy;
            Attributes = attributes;

            Label = $"ActorRefSource({bufferSize}, {overflowStrategy})";
        }

        public override Attributes Attributes { get; }

        protected override string Label { get; }

        public override IModule WithAttributes(Attributes attributes) 
            => new ActorRefSource<TOut>(_bufferSize, _overflowStrategy, attributes, AmendShape(attributes));

        protected override SourceModule<TOut, IActorRef> NewInstance(SourceShape<TOut> shape) 
            => new ActorRefSource<TOut>(_bufferSize, _overflowStrategy, Attributes, shape);

        public override IPublisher<TOut> Create(MaterializationContext context, out IActorRef materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            materializer = mat.ActorOf(context, ActorRefSourceActor<TOut>.Props(_bufferSize, _overflowStrategy, mat.Settings));
            return new ActorPublisherImpl<TOut>(materializer);
        }
    }
}