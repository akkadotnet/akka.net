//-----------------------------------------------------------------------
// <copyright file="Modules.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// Provides the source-module shape and creates an untyped publisher with its materialized value.
    /// </summary>
    internal interface ISourceModule
    {
        /// <summary>
        /// Gets the source shape provided by the module.
        /// </summary>
        Shape Shape { get; }
        /// <summary>
        /// Creates the publisher represented by this source module.
        /// </summary>
        /// <param name="context">The context used to materialize the publisher.</param>
        /// <param name="materializer">Receives the value produced when this module is materialized.</param>
        /// <returns>The publisher exposed by the materialized source.</returns>
        IUntypedPublisher Create(MaterializationContext context, out object materializer);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">The type of elements emitted by this source.</typeparam>
    /// <typeparam name="TMat">The type of the value produced when this source is materialized.</typeparam>
    [InternalApi]
    public abstract class SourceModule<TOut, TMat> : AtomicModule, ISourceModule
    {
        private readonly SourceShape<TOut> _shape;

        /// <summary>
        /// Initializes a source module with its output shape.
        /// </summary>
        /// <param name="shape">The source shape exposed by this module.</param>
        protected SourceModule(SourceShape<TOut> shape)
        {
            _shape = shape;
        }

        /// <summary>
        /// Gets the source shape exposed by this module.
        /// </summary>
        public override Shape Shape => _shape;

        /// <summary>
        /// Gets the label used in this module's string representation.
        /// </summary>
        protected virtual string Label => GetType().Name;

        /// <summary>
        /// Returns the module label and its identity hash code.
        /// </summary>
        /// <returns>A diagnostic string identifying this module instance.</returns>
        public sealed override string ToString() => $"{Label} [{GetHashCode()}%08x]";

        // This is okay since the only caller of this method is right below.
        /// <summary>
        /// Creates a new source module instance with the supplied copied shape.
        /// </summary>
        /// <param name="shape">The shape to expose from the new module.</param>
        /// <returns>A module of the same source type with the supplied shape.</returns>
        protected abstract SourceModule<TOut, TMat> NewInstance(SourceShape<TOut> shape);

        /// <summary>
        /// Creates the publisher and materialized value for this source module.
        /// </summary>
        /// <param name="context">The context used to materialize the publisher.</param>
        /// <param name="materializer">Receives this source's materialized value.</param>
        /// <returns>The publisher that emits this source's elements.</returns>
        public abstract IPublisher<TOut> Create(MaterializationContext context, out TMat materializer);

        IUntypedPublisher ISourceModule.Create(MaterializationContext context, out object materializer)
        {
            var result = Create(context, out var m);
            materializer = m;
            return UntypedPublisher.FromTyped(result);
        }


        /// <summary>
        /// Replaces the module's source shape.
        /// </summary>
        /// <param name="shape">The requested shape.</param>
        /// <exception cref="NotSupportedException">The requested shape differs from this source's shape.</exception>
        /// <returns>This module when the supplied shape equals the existing shape.</returns>
        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(shape, Shape))
                return this;

            throw new NotSupportedException("cannot replace the shape of a Source, you need to wrap it in a Graph for that");
        }

        /// <summary>
        /// Creates an independent module copy with a carbon-copied outlet.
        /// </summary>
        /// <returns>A new source module instance with a copied source shape.</returns>
        public override IModule CarbonCopy()
            => NewInstance(new SourceShape<TOut>(Outlet.Create<TOut>(_shape.Outlet.CarbonCopy())));

        /// <summary>
        /// Returns this source shape unless the attributes specify a different name for its outlet.
        /// </summary>
        /// <param name="attributes">The attributes whose name may be applied to the outlet.</param>
        /// <returns>The existing shape when the name is absent or unchanged; otherwise, a shape with the updated outlet name.</returns>
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
    /// <typeparam name="TOut">The type of elements received by the subscriber.</typeparam>
    [InternalApi]
    public sealed class SubscriberSource<TOut> : SourceModule<TOut, ISubscriber<TOut>>
    {
        /// <summary>
        /// Creates a subscriber-backed source module.
        /// </summary>
        /// <param name="attributes">Attributes applied to the source module.</param>
        /// <param name="shape">The source shape exposed by this module.</param>
        public SubscriberSource(Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// Gets the attributes applied to this module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply to the copied module.</param>
        /// <returns>A subscriber source module with the supplied attributes and amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSource<TOut>(attributes, AmendShape(attributes));

        /// <summary>
        /// Creates a subscriber source module with a copied shape.
        /// </summary>
        /// <param name="shape">The copied shape to expose.</param>
        /// <returns>A subscriber source module that retains this module's attributes.</returns>
        protected override SourceModule<TOut, ISubscriber<TOut>> NewInstance(SourceShape<TOut> shape)
            => new SubscriberSource<TOut>(Attributes, shape);

        /// <summary>
        /// Creates a virtual processor that acts as both the source publisher and materialized subscriber.
        /// </summary>
        /// <param name="context">The context used to materialize the source.</param>
        /// <param name="materializer">Receives the processor's subscriber side.</param>
        /// <returns>The processor's publisher side.</returns>
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
    /// <typeparam name="TOut">The type of elements emitted by the wrapped publisher.</typeparam>
    [InternalApi]
    public sealed class PublisherSource<TOut> : SourceModule<TOut, NotUsed>
    {
        private readonly IPublisher<TOut> _publisher;

        /// <summary>
        /// Creates a source module around an existing publisher.
        /// </summary>
        /// <param name="publisher">The publisher exposed by this source.</param>
        /// <param name="attributes">Attributes applied to the source module.</param>
        /// <param name="shape">The source shape exposed by this module.</param>
        public PublisherSource(IPublisher<TOut> publisher, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _publisher = publisher;
            Attributes = attributes;

            Label = $"PublisherSource({publisher})";
        }

        /// <summary>
        /// Gets the attributes applied to this module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Gets the label describing the wrapped publisher.
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply to the copied module.</param>
        /// <returns>A publisher source module with the supplied attributes and amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new PublisherSource<TOut>(_publisher, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates a publisher source module with a copied source shape.
        /// </summary>
        /// <param name="shape">The copied shape to expose.</param>
        /// <returns>A publisher source module that retains this module's publisher and attributes.</returns>
        protected override SourceModule<TOut, NotUsed> NewInstance(SourceShape<TOut> shape)
            => new PublisherSource<TOut>(_publisher, Attributes, shape);

        /// <summary>
        /// Returns the wrapped publisher and the source's <see cref="NotUsed"/> materialized value.
        /// </summary>
        /// <param name="context">The context used to materialize the source.</param>
        /// <param name="materializer">Receives <see cref="NotUsed.Instance"/>.</param>
        /// <returns>The publisher supplied to this module.</returns>
        public override IPublisher<TOut> Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return _publisher;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TOut">The type of the optional element emitted by the source.</typeparam>
    [InternalApi]
    public sealed class MaybeSource<TOut> : SourceModule<TOut, TaskCompletionSource<TOut>>
    {
        /// <summary>
        /// Creates a source module backed by a task completion source.
        /// </summary>
        /// <param name="attributes">Attributes applied to the source module.</param>
        /// <param name="shape">The source shape exposed by this module.</param>
        public MaybeSource(Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// Gets the attributes applied to this module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply to the copied module.</param>
        /// <returns>A maybe source module with the supplied attributes and amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new MaybeSource<TOut>(attributes, AmendShape(attributes));

        /// <summary>
        /// Creates a maybe source module with a copied source shape.
        /// </summary>
        /// <param name="shape">The copied shape to expose.</param>
        /// <returns>A maybe source module that retains this module's attributes.</returns>
        protected override SourceModule<TOut, TaskCompletionSource<TOut>> NewInstance(SourceShape<TOut> shape)
            => new MaybeSource<TOut>(Attributes, shape);

        /// <summary>
        /// Creates a publisher backed by a new task completion source for its optional element.
        /// </summary>
        /// <param name="context">The context used to materialize the source.</param>
        /// <param name="materializer">Receives the task completion source used to provide the element.</param>
        /// <returns>A publisher that emits the supplied element when present; completing the task with <c>default(TOut)</c> completes without an element.</returns>
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
    /// <typeparam name="TOut">The type of elements emitted by the actor publisher.</typeparam>
    [InternalApi]
    public sealed class ActorPublisherSource<TOut> : SourceModule<TOut, IActorRef>
    {
        private readonly Props _props;

        /// <summary>
        /// Creates an actor-publisher-backed source module.
        /// </summary>
        /// <param name="props">The actor properties used to create the publisher actor.</param>
        /// <param name="attributes">Attributes applied to the source module.</param>
        /// <param name="shape">The source shape exposed by this module.</param>
        public ActorPublisherSource(Props props, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _props = props;
            Attributes = attributes;
        }

        /// <summary>
        /// Gets the attributes applied to this module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply to the copied module.</param>
        /// <returns>An actor publisher source module with the supplied attributes and amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new ActorPublisherSource<TOut>(_props, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates an actor publisher source module with a copied source shape.
        /// </summary>
        /// <param name="shape">The copied shape to expose.</param>
        /// <returns>An actor publisher source module that retains this module's properties and attributes.</returns>
        protected override SourceModule<TOut, IActorRef> NewInstance(SourceShape<TOut> shape)
            => new ActorPublisherSource<TOut>(_props, Attributes, shape);

        /// <summary>
        /// Creates the publisher actor and returns its reference as the materialized value.
        /// </summary>
        /// <param name="context">The context used to create the publisher actor.</param>
        /// <param name="materializer">Receives the actor reference that accepts source commands.</param>
        /// <returns>A publisher adapter for the created actor.</returns>
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
    /// <typeparam name="TOut">The type of elements emitted by the actor-reference source.</typeparam>
    [InternalApi]
    public sealed class ActorRefSource<TOut> : SourceModule<TOut, IActorRef>
    {
        private readonly int _bufferSize;
        private readonly OverflowStrategy _overflowStrategy;

        /// <summary>
        /// Creates a source backed by an actor reference and its configured buffer.
        /// </summary>
        /// <param name="bufferSize">The number of elements buffered before applying the overflow strategy.</param>
        /// <param name="overflowStrategy">The policy used when the buffer cannot accept another element.</param>
        /// <param name="attributes">Attributes applied to the source module.</param>
        /// <param name="shape">The source shape exposed by this module.</param>
        public ActorRefSource(int bufferSize, OverflowStrategy overflowStrategy, Attributes attributes, SourceShape<TOut> shape) : base(shape)
        {
            _bufferSize = bufferSize;
            _overflowStrategy = overflowStrategy;
            Attributes = attributes;

            Label = $"ActorRefSource({bufferSize}, {overflowStrategy})";
        }

        /// <summary>
        /// Gets the attributes applied to this module.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Gets the label containing this source's buffer size and overflow strategy.
        /// </summary>
        protected override string Label { get; }

        /// <summary>
        /// Returns a copy of this module with the supplied attributes.
        /// </summary>
        /// <param name="attributes">The attributes to apply to the copied module.</param>
        /// <returns>An actor-reference source module with the supplied attributes and amended shape.</returns>
        public override IModule WithAttributes(Attributes attributes) 
            => new ActorRefSource<TOut>(_bufferSize, _overflowStrategy, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates an actor-reference source module with a copied source shape.
        /// </summary>
        /// <param name="shape">The copied shape to expose.</param>
        /// <returns>An actor-reference source module that retains this module's buffer settings and attributes.</returns>
        protected override SourceModule<TOut, IActorRef> NewInstance(SourceShape<TOut> shape) 
            => new ActorRefSource<TOut>(_bufferSize, _overflowStrategy, Attributes, shape);

        /// <summary>
        /// Creates the actor that backs the publisher and returns its reference as the materialized value.
        /// </summary>
        /// <param name="context">The context used to create the publisher actor.</param>
        /// <param name="materializer">Receives the actor reference that accepts source commands.</param>
        /// <returns>A publisher adapter for the created actor.</returns>
        public override IPublisher<TOut> Create(MaterializationContext context, out IActorRef materializer)
        {
            var mat = ActorMaterializerHelper.Downcast(context.Materializer);
            materializer = mat.ActorOf(context, ActorRefSourceActor<TOut>.Props(_bufferSize, _overflowStrategy, mat.Settings));
            return new ActorPublisherImpl<TOut>(materializer);
        }
    }
}
