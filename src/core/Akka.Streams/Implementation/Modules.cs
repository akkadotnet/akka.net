//-----------------------------------------------------------------------
// <copyright file="Modules.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Streams.Actors;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    internal interface ISourceModule
    {
        /// <summary>
        /// TBD
        /// </summary>
        Shape Shape { get; }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        IUntypedPublisher Create(MaterializationContext context, out object materializer);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    [InternalApi]
    public abstract class SourceModule<TOut, TMat> : AtomicModule, ISourceModule
    {
        private readonly SourceShape<TOut> _shape;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        protected SourceModule(SourceShape<TOut> shape)
        {
            _shape = shape;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Shape Shape => _shape;

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string Label => GetType().Name;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public sealed override string ToString() => $"{Label} [{GetHashCode()}%08x]";

        // This is okay since the only caller of this method is right below.
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected abstract SourceModule<TOut, TMat> NewInstance(SourceShape<TOut> shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public abstract IPublisher<TOut> Create(MaterializationContext context, out TMat materializer);

        IUntypedPublisher ISourceModule.Create(MaterializationContext context, out object materializer)
        {
            TMat m;
            var result = Create(context, out m);
            materializer = m;
            return UntypedPublisher.FromTyped(result);
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(shape, Shape))
                return this;

            throw new NotSupportedException("cannot replace the shape of a Source, you need to wrap it in a Graph for that");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy()
            => NewInstance(new SourceShape<TOut>(Outlet.Create<TOut>(_shape.Outlet.CarbonCopy())));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class SubscriberSource<TOut> : SourceModule<TOut, ISubscriber<TOut>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public SubscriberSource(Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSource<TOut>(attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<TOut, ISubscriber<TOut>> NewInstance(SourceShape<TOut> shape)
            => new SubscriberSource<TOut>(Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class PublisherSource<TOut> : SourceModule<TOut, NotUsed>
    {
        private readonly IPublisher<TOut> _publisher;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public PublisherSource(IPublisher<TOut> publisher, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _publisher = publisher;
            Attributes = attributes;

            Label = $"PublisherSource({publisher})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new PublisherSource<TOut>(_publisher, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<TOut, NotUsed> NewInstance(SourceShape<TOut> shape)
            => new PublisherSource<TOut>(_publisher, Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public override IPublisher<TOut> Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return _publisher;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class MaybeSource<TOut> : SourceModule<TOut, TaskCompletionSource<TOut>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public MaybeSource(Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new MaybeSource<TOut>(attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<TOut, TaskCompletionSource<TOut>> NewInstance(SourceShape<TOut> shape)
            => new MaybeSource<TOut>(Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class ActorPublisherSource<TOut> : SourceModule<TOut, IActorRef>
    {
        private readonly Props _props;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="props">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public ActorPublisherSource(Props props, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _props = props;
            Attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new ActorPublisherSource<TOut>(_props, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<TOut, IActorRef> NewInstance(SourceShape<TOut> shape)
            => new ActorPublisherSource<TOut>(_props, Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TOut">TBD</typeparam>
    [InternalApi]
    public sealed class ActorRefSource<TOut> : SourceModule<TOut, IActorRef>
    {
        private readonly int _bufferSize;
        private readonly OverflowStrategy _overflowStrategy;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="bufferSize">TBD</param>
        /// <param name="overflowStrategy">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public ActorRefSource(int bufferSize, OverflowStrategy overflowStrategy, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _bufferSize = bufferSize;
            _overflowStrategy = overflowStrategy;
            Attributes = attributes;

            Label = $"ActorRefSource({bufferSize}, {overflowStrategy})";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes) 
            => new ActorRefSource<TOut>(_bufferSize, _overflowStrategy, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SourceModule<TOut, IActorRef> NewInstance(SourceShape<TOut> shape) 
            => new ActorRefSource<TOut>(_bufferSize, _overflowStrategy, Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public override IPublisher<TOut> Create(MaterializationContext context, out IActorRef materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            materializer = mat.ActorOf(context, ActorRefSourceActor<TOut>.Props(_bufferSize, _overflowStrategy, mat.Settings));
            return new ActorPublisherImpl<TOut>(materializer);
        }
    }
}
