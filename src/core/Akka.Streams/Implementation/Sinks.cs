//-----------------------------------------------------------------------
// <copyright file="Sinks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;
using Reactive.Streams;
using Decider = Akka.Streams.Supervision.Decider;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Internal contract for sink modules that create their runtime subscriber or virtual publisher and materialized value.
    /// </summary>
    internal interface ISinkModule
    {
        /// <summary>
        /// The sink's graph shape.
        /// </summary>
        Shape Shape { get; }
        /// <summary>
        /// Creates the sink's runtime consumer and returns its materialized value through <paramref name="materializer"/>.
        /// </summary>
        object Create(MaterializationContext context, out object materializer);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">The type of elements consumed by the sink.</typeparam>
    /// <typeparam name="TMat">The type of the value produced when the sink is materialized.</typeparam>
    [InternalApi]
    public abstract class SinkModule<TIn, TMat> : AtomicModule, ISinkModule
    {
        private readonly SinkShape<TIn> _shape;

        /// <summary>
        /// Creates a sink module with the supplied shape.
        /// </summary>
        /// <param name="shape">The inlet shape consumed by this sink.</param>
        protected SinkModule(SinkShape<TIn> shape)
        {
            _shape = shape;
        }

        /// <summary>
        /// The sink's inlet shape.
        /// </summary>
        public override Shape Shape => _shape;

        /// <summary>
        /// The label used by <see cref="ToString"/>.
        /// </summary>
        protected virtual string Label => GetType().Name;

        /// <summary>
        /// Returns the module label and its hash code.
        /// </summary>
        /// <returns>A diagnostic representation of this module.</returns>
        public sealed override string ToString() => $"{Label} [{GetHashCode()}%08x]";

        /// <summary>
        /// Creates this sink module with a replacement inlet shape.
        /// </summary>
        /// <param name="shape">The inlet shape for the new module.</param>
        /// <returns>A sink module with the requested shape.</returns>
        protected abstract SinkModule<TIn, TMat> NewInstance(SinkShape<TIn> shape);

        /// <summary>
        /// Create the Subscriber or VirtualPublisher that consumes the incoming
        /// stream, plus the materialized value. Since Subscriber and VirtualPublisher
        /// do not share a common supertype apart from AnyRef this is what the type
        /// union devolves into; unfortunately we do not have union types at our
        /// disposal at this point.
        /// </summary>
        /// <param name="context">The context used to create runtime components for this sink.</param>
        /// <param name="materializer">Receives the value materialized by the sink.</param>
        /// <returns>The subscriber or virtual publisher that consumes the incoming stream.</returns>
        public abstract object Create(MaterializationContext context, out TMat materializer);

        object ISinkModule.Create(MaterializationContext context, out object materializer)
        {
            var result = Create(context, out var m);
            materializer = m;
            return result;
        }

        /// <summary>
        /// A sink module's shape cannot be changed; wrap the sink in a graph to adapt its shape.
        /// </summary>
        /// <param name="shape">The requested shape.</param>
        /// <exception cref="NotSupportedException">The requested shape differs from this sink's shape.</exception>
        /// <returns>This module when <paramref name="shape"/> equals its existing shape.</returns>
        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(_shape, shape))
                return this;

            throw new NotSupportedException(
                "cannot replace the shape of a Sink, you need to wrap it in a Graph for that");
        }

        /// <summary>
        /// Creates a carbon copy with a carbon-copied inlet.
        /// </summary>
        /// <returns>A new sink module with the copied shape.</returns>
        public override IModule CarbonCopy()
            => NewInstance(new SinkShape<TIn>(Inlet.Create<TIn>(_shape.Inlet.CarbonCopy())));

        /// <summary>
        /// Returns this sink's shape when the supplied attributes do not change its name; otherwise creates a named inlet.
        /// </summary>
        /// <param name="attrs">The attributes used to determine the inlet name.</param>
        /// <returns>The existing or amended sink shape.</returns>
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
    /// <typeparam name="TIn">The type of elements published to subscribers of the materialized publisher.</typeparam>
    [InternalApi]
    internal sealed class PublisherSink<TIn> : SinkModule<TIn, IPublisher<TIn>>
    {
        /// <summary>
        /// Creates a publisher sink module.
        /// </summary>
        /// <param name="attributes">The attributes applied to the sink.</param>
        /// <param name="shape">The sink's inlet shape.</param>
        public PublisherSink(Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// The attributes applied to this sink.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a sink module with the supplied attributes and an inlet amended to reflect its name.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A sink module using the supplied attributes.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new PublisherSink<TIn>(attributes, AmendShape(attributes));

        /// <summary>
        /// Creates this sink module with the supplied inlet shape.
        /// </summary>
        /// <param name="shape">The inlet shape for the copy.</param>
        /// <returns>A new publisher sink module.</returns>
        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
            => new PublisherSink<TIn>(Attributes, shape);

        /// <summary>
        /// This method is the reason why SinkModule.create may return something that is
        /// not a Subscriber: a VirtualPublisher is used in order to avoid the immediate
        /// subscription a VirtualProcessor would perform (and it also saves overhead).
        /// </summary>
        /// <param name="context">The materialization context.</param>
        /// <param name="materializer">Receives the publisher exposed by this sink.</param>
        /// <returns>A virtual processor that publishes elements when a subscriber connects.</returns>
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
    /// <typeparam name="TIn">The type of elements published to downstream subscribers.</typeparam>
    /// <typeparam name="TStreamBuffer">The buffer implementation used by the fanout processor.</typeparam>
    internal sealed class FanoutPublisherSink<TIn, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStreamBuffer> : SinkModule<TIn, IPublisher<TIn>> where TStreamBuffer : IStreamBuffer<TIn>
    {
        private readonly Action _onTerminated;

        /// <summary>
        /// Creates a fanout publisher sink.
        /// </summary>
        /// <param name="attributes">The attributes applied to the sink.</param>
        /// <param name="shape">The sink's inlet shape.</param>
        /// <param name="onTerminated">An optional callback invoked when the fanout processor terminates.</param>
        public FanoutPublisherSink(Attributes attributes, SinkShape<TIn> shape, Action onTerminated = null) : base(shape)
        {
            Attributes = attributes;
            _onTerminated = onTerminated;
        }

        /// <summary>
        /// The attributes applied to this sink.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a sink module with the supplied attributes and an inlet amended to reflect its name.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A sink module using the supplied attributes.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new FanoutPublisherSink<TIn, TStreamBuffer>(attributes, AmendShape(attributes), _onTerminated);

        /// <summary>
        /// Creates this sink module with the supplied inlet shape.
        /// </summary>
        /// <param name="shape">The inlet shape for the copy.</param>
        /// <returns>A new fanout publisher sink module.</returns>
        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
            => new FanoutPublisherSink<TIn, TStreamBuffer>(Attributes, shape, _onTerminated);

        /// <summary>
        /// Creates an actor-backed fanout publisher and exposes its processor as the materialized publisher.
        /// </summary>
        /// <param name="context">The materialization context.</param>
        /// <param name="materializer">Receives the publisher exposed by this sink.</param>
        /// <returns>The actor processor that publishes incoming elements to connected subscribers.</returns>
        public override object Create(MaterializationContext context, out IPublisher<TIn> materializer)
        {
            var actorMaterializer = ActorMaterializerHelper.Downcast(context.Materializer);
            var settings = actorMaterializer.EffectiveSettings(Attributes);
            var impl = actorMaterializer.ActorOf(context, FanoutProcessorImpl<TIn, TStreamBuffer>.Props(settings, _onTerminated));
            var fanoutProcessor = new ActorProcessor<TIn, TIn>(impl);
            impl.Tell(new ExposedPublisher(fanoutProcessor));
            // Resolve cyclic dependency with actor. This MUST be the first message no matter what.
            materializer = fanoutProcessor;
            return fanoutProcessor;
        }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Attaches a subscriber to this stream.
    /// </summary>
    /// <typeparam name="TIn">The type of elements consumed by the supplied subscriber.</typeparam>
    [InternalApi]
    public sealed class SubscriberSink<TIn> : SinkModule<TIn, NotUsed>
    {
        private readonly ISubscriber<TIn> _subscriber;

        /// <summary>
        /// Creates a sink module that attaches the supplied subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber to attach to the incoming stream.</param>
        /// <param name="attributes">The attributes applied to the sink.</param>
        /// <param name="shape">The sink's inlet shape.</param>
        public SubscriberSink(ISubscriber<TIn> subscriber, Attributes attributes, SinkShape<TIn> shape) : base(shape)
        {
            Attributes = attributes;
            _subscriber = subscriber;
        }

        /// <summary>
        /// The attributes applied to this sink.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Returns a sink module with the supplied attributes and an inlet amended to reflect its name.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A sink module using the supplied attributes.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new SubscriberSink<TIn>(_subscriber, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates this sink module with the supplied inlet shape.
        /// </summary>
        /// <param name="shape">The inlet shape for the copy.</param>
        /// <returns>A new subscriber sink module.</returns>
        protected override SinkModule<TIn, NotUsed> NewInstance(SinkShape<TIn> shape)
            => new SubscriberSink<TIn>(_subscriber, Attributes, shape);

        /// <summary>
        /// Returns the supplied subscriber as the sink's runtime consumer and materializes <see cref="NotUsed"/>.
        /// </summary>
        /// <param name="context">The materialization context.</param>
        /// <param name="materializer">Receives <see cref="NotUsed"/>.</param>
        /// <returns>The subscriber attached to the incoming stream.</returns>
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
    /// <typeparam name="T">The type of elements discarded by this sink.</typeparam>
    [InternalApi]
    public sealed class CancelSink<T> : SinkModule<T, NotUsed>
    {
        /// <summary>
        /// Creates a sink that cancels its upstream when materialized.
        /// </summary>
        /// <param name="attributes">The attributes applied to the sink.</param>
        /// <param name="shape">The sink's inlet shape.</param>
        public CancelSink(Attributes attributes, SinkShape<T> shape)
            : base(shape)
        {
            Attributes = attributes;
        }

        /// <summary>
        /// The attributes applied to this sink.
        /// </summary>
        public override Attributes Attributes { get; }

        /// <summary>
        /// Creates this sink module with the supplied inlet shape.
        /// </summary>
        /// <param name="shape">The inlet shape for the copy.</param>
        /// <returns>A new cancelling sink module.</returns>
        protected override SinkModule<T, NotUsed> NewInstance(SinkShape<T> shape)
            => new CancelSink<T>(Attributes, shape);

        /// <summary>
        /// Creates a cancelling subscriber and materializes <see cref="NotUsed"/>.
        /// </summary>
        /// <param name="context">The materialization context.</param>
        /// <param name="materializer">Receives <see cref="NotUsed"/>.</param>
        /// <returns>A subscriber that cancels its subscription.</returns>
        public override object Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return new CancellingSubscriber<T>();
        }

        /// <summary>
        /// Returns a sink module with the supplied attributes and an inlet amended to reflect its name.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A sink module using the supplied attributes.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new CancelSink<T>(attributes, AmendShape(attributes));
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Creates and wraps an actor into <see cref="ISubscriber{T}"/> from the given <see cref="Props"/>,
    /// which should be <see cref="Props"/> for an <see cref="ActorSubscriber"/>.
    /// </summary>
    /// <typeparam name="TIn">The type of elements consumed by the actor subscriber.</typeparam>
    [InternalApi]
    public sealed class ActorSubscriberSink<TIn> : SinkModule<TIn, IActorRef>
    {
        private readonly Props _props;
        private readonly Attributes _attributes;

        /// <summary>
        /// Creates a sink module that creates an actor and adapts it to a subscriber.
        /// </summary>
        /// <param name="props">The actor properties used to create the subscriber actor.</param>
        /// <param name="attributes">The attributes applied to the sink.</param>
        /// <param name="shape">The sink's inlet shape.</param>
        public ActorSubscriberSink(Props props, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _props = props;
            _attributes = attributes;
        }

        /// <summary>
        /// The attributes applied to this sink.
        /// </summary>
        public override Attributes Attributes => _attributes;

        /// <summary>
        /// Returns a sink module with the supplied attributes and an inlet amended to reflect its name.
        /// </summary>
        /// <param name="attributes">The attributes to apply.</param>
        /// <returns>A sink module using the supplied attributes.</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new ActorSubscriberSink<TIn>(_props, attributes, AmendShape(attributes));

        /// <summary>
        /// Creates this sink module with the supplied inlet shape.
        /// </summary>
        /// <param name="shape">The inlet shape for the copy.</param>
        /// <returns>A new actor subscriber sink module.</returns>
        protected override SinkModule<TIn, IActorRef> NewInstance(SinkShape<TIn> shape)
            => new ActorSubscriberSink<TIn>(_props, _attributes, shape);

        /// <summary>
        /// Creates an actor subscriber and materializes the reference to the actor created for it.
        /// </summary>
        /// <param name="context">The materialization context used to create the actor.</param>
        /// <param name="materializer">Receives the actor reference created for the subscriber.</param>
        /// <returns>An actor subscriber that forwards stream elements to the created actor.</returns>
        public override object Create(MaterializationContext context, out IActorRef materializer)
        {
            var subscriberRef = ActorMaterializerHelper.Downcast(context.Materializer).ActorOf(context, _props);
            materializer = subscriberRef;
            return ActorSubscriber.Create<TIn>(subscriberRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements consumed by this stage.</typeparam>
    [InternalApi]
    public sealed class LastOrDefaultStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<T>>
    {
        #region stage logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly TaskCompletionSource<T> _promise;
            private readonly LastOrDefaultStage<T> _stage;
            private T _prev;

            public Logic(TaskCompletionSource<T> promise, LastOrDefaultStage<T> stage) : base(stage.Shape)
            {
                _promise = promise;
                _stage = stage;

                SetHandler(stage.In, this);
            }

            public override void OnPush()
            {
                _prev = Grab(_stage.In);
                Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                var head = _prev;
                _prev = default(T);
                _promise.TrySetResult(head);
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _prev = default(T);
                _promise.TrySetException(e);
                FailStage(e);
            }

            public override void PreStart() => Pull(_stage.In);
        }

        #endregion

        /// <summary>
        /// The inlet that receives elements for the sink.
        /// </summary>
        public readonly Inlet<T> In = new("LastOrDefault.in");

        /// <summary>
        /// Creates a sink stage with an inlet named for the last-or-default operation.
        /// </summary>
        public LastOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// The sink shape containing <see cref="In"/>.
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// Creates the stage logic and the task that completes with the final element, or <c>default(T)</c> if the
        /// upstream completes without elements. An upstream failure faults the task.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited by the stage.</param>
        /// <returns>The stage logic and its last-element task.</returns>
        public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = TaskEx.NonBlockingTaskCompletionSource<T>();
            return new LogicAndMaterializedValue<Task<T>>(new Logic(promise, this), promise.Task);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements consumed by this stage.</typeparam>
    [InternalApi]
    public sealed class FirstOrDefaultStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<T>>
    {
        #region stage logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly TaskCompletionSource<T> _promise;
            private readonly FirstOrDefaultStage<T> _stage;
            private bool _completionSignalled;

            public Logic(TaskCompletionSource<T> promise, FirstOrDefaultStage<T> stage) : base(stage.Shape)
            {
                _promise = promise;
                _stage = stage;

                SetHandler(stage.In, this);
            }
            public override void OnPush()
            {
                _promise.TrySetResult(Grab(_stage.In));
                _completionSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFinish()
            {
                _promise.TrySetResult(default(T));
                _completionSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _promise.TrySetException(e);
                _completionSignalled = true;
                FailStage(e);
            }

            public override void PostStop()
            {
                if (!_completionSignalled)
                    _promise.TrySetException(new AbruptStageTerminationException(this));
            }

            public override void PreStart() => Pull(_stage.In);
        }

        #endregion

        /// <summary>
        /// The inlet that receives elements for the sink.
        /// </summary>
        public readonly Inlet<T> In = new("FirstOrDefault.in");

        /// <summary>
        /// Creates a sink stage with an inlet named for the first-or-default operation.
        /// </summary>
        public FirstOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// The sink shape containing <see cref="In"/>.
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// Creates the stage logic and the task that completes with the first element, or <c>default(T)</c> if the
        /// upstream completes without elements. An upstream failure faults the task.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited by the stage.</param>
        /// <returns>The stage logic and its first-element task.</returns>
        public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = TaskEx.NonBlockingTaskCompletionSource<T>();
            return new LogicAndMaterializedValue<Task<T>>(new Logic(promise, this), promise.Task);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements collected by this stage.</typeparam>
    [InternalApi]
    public sealed class SeqStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<IImmutableList<T>>>
    {
        #region stage logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly SeqStage<T> _stage;
            private readonly TaskCompletionSource<IImmutableList<T>> _promise;
            private IImmutableList<T> _buf = ImmutableList<T>.Empty;
            private bool _completionSignalled;

            public Logic(SeqStage<T> stage, TaskCompletionSource<IImmutableList<T>> promise) : base(stage.Shape)
            {
                _stage = stage;
                _promise = promise;

                SetHandler(stage.In, this);
            }

            public override void OnPush()
            {
                _buf = _buf.Add(Grab(_stage.In));
                Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                _promise.TrySetResult(_buf);
                _completionSignalled = true;
                CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _promise.TrySetException(e);
                _completionSignalled = true;
                FailStage(e);
            }

            public override void PostStop()
            {
                if (!_completionSignalled)
                    _promise.TrySetException(new AbruptStageTerminationException(this));
            }

            public override void PreStart() => Pull(_stage.In);
        }

        #endregion

        /// <summary>
        /// Creates a sink stage that collects all upstream elements into an immutable list.
        /// </summary>
        public SeqStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// The default attributes for this sequence sink.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.SeqSink;

        /// <summary>
        /// The sink shape containing <see cref="In"/>.
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// The inlet that receives elements for the sink.
        /// </summary>
        public readonly Inlet<T> In = new("Seq.in");

        /// <summary>
        /// Creates stage logic that accumulates upstream elements and completes its task with the immutable list when
        /// the upstream completes. An upstream failure faults the task.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited by the stage.</param>
        /// <returns>The stage logic and its collected-list task.</returns>
        public override ILogicAndMaterializedValue<Task<IImmutableList<T>>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = TaskEx.NonBlockingTaskCompletionSource<IImmutableList<T>>();
            return new LogicAndMaterializedValue<Task<IImmutableList<T>>>(new Logic(this, promise), promise.Task);
        }

        /// <summary>
        /// Returns the diagnostic name of this stage.
        /// </summary>
        /// <returns><c>SeqStage</c>.</returns>
        public override string ToString() => "SeqStage";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">The type of elements made available through the sink queue.</typeparam>
    #nullable enable
    [InternalApi]
    public sealed class QueueSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, ISinkQueue<T>>
    {
        #region stage logic

        private sealed class Logic : GraphStageLogicWithCallbackWrapper<TaskCompletionSource<Option<T>>>, IInHandler
        {
            private readonly QueueSink<T> _stage;
            private readonly int _maxBuffer;
            private IBuffer<Result<Option<T>>>? _buffer;
            private Option<TaskCompletionSource<Option<T>>> _currentRequest;
            private bool _closed;

            public Logic(QueueSink<T> stage, int maxBuffer) : base(stage.Shape)
            {
                _stage = stage;
                _maxBuffer = maxBuffer;
                _currentRequest = Option<TaskCompletionSource<Option<T>>>.None;

                SetHandler(stage.In, this);
            }

            public void OnPush()
            {
                Debug.Assert(_buffer != null, nameof(_buffer) + " != null");
                
                EnqueueAndNotify(new Result<Option<T>>(Grab(_stage.In)));
                if (_buffer!.Used < _maxBuffer) Pull(_stage.In);
            }

            public void OnUpstreamFinish() => EnqueueAndNotify(new Result<Option<T>>(Option<T>.None));

            public void OnUpstreamFailure(Exception e) => EnqueueAndNotify(new Result<Option<T>>(e));

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
                // Complete any pending request before shutting down to prevent orphaned Tasks
                if (_currentRequest.HasValue)
                {
                    _currentRequest.Value.SetException(new StreamDetachedException());
                    _currentRequest = Option<TaskCompletionSource<Option<T>>>.None;
                }
                StopCallback(promise => promise.SetException(new StreamDetachedException()));
            }

            private Action<TaskCompletionSource<Option<T>>> Callback()
            {
                return GetAsyncCallback<TaskCompletionSource<Option<T>>>(
                    promise =>
                    {
                        if (_closed)
                            promise.SetException(new StreamDetachedException());
                        else if (_currentRequest.HasValue)
                            promise.SetException(
                                new IllegalStateException(
                                    "You have to wait for previous future to be resolved to send another request"));
                        else
                        {
                            Debug.Assert(_buffer != null, nameof(_buffer) + " != null");

                            if (_buffer!.IsEmpty)
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
                Debug.Assert(_buffer != null, nameof(_buffer) + " != null");

                var e = _buffer!.Dequeue();
                if (e.IsSuccess)
                {
                    promise.SetResult(e.Value);
                    if (!e.Value.HasValue)
                    {
                        _closed = true;
                        CompleteStage();
                    }
                }
                else
                {
                    promise.SetException(e.Exception!);
                    FailStage(e.Exception);
                }
            }

            private void EnqueueAndNotify(Result<Option<T>> requested)
            {
                Debug.Assert(_buffer != null, nameof(_buffer) + " != null");
                
                _buffer!.Enqueue(requested);
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
                var promise = TaskEx.NonBlockingTaskCompletionSource<Option<T>>();
                _invokeLogic(promise);
                return promise.Task;
            }
        }

        #endregion

        /// <summary>
        /// The inlet that receives elements for the queue sink.
        /// </summary>
        public readonly Inlet<T> In = new("QueueSink.in");

        /// <summary>
        /// Creates a queue sink stage with an inlet named for the queue sink.
        /// </summary>
        public QueueSink()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// The default attributes for this queue sink.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.QueueSink;

        /// <summary>
        /// The sink shape containing <see cref="In"/>.
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// Creates stage logic and an <see cref="ISinkQueue{T}"/>. The queue returns available elements through
        /// successful <see cref="Option{T}"/> results, completes with <see cref="Option{T}.None"/> at upstream
        /// completion, and faults a pull task when the upstream fails or the stage detaches. Only one pull may be
        /// outstanding at a time.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes used to select the input buffer size.</param>
        /// <exception cref="ArgumentException">The configured maximum input buffer size is not positive.</exception>
        /// <returns>The stage logic and its sink queue.</returns>
        public override ILogicAndMaterializedValue<ISinkQueue<T>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var maxBuffer = inheritedAttributes.GetAttribute(new Attributes.InputBuffer(16, 16)).Max;
            if (maxBuffer <= 0)
                throw new ArgumentException("Buffer must be greater than zero", nameof(inheritedAttributes));

            var logic = new Logic(this, maxBuffer);
            return new LogicAndMaterializedValue<ISinkQueue<T>>(logic, new Materialized(t => logic.Invoke(t)));
        }

        /// <summary>
        /// Returns the diagnostic name of this stage.
        /// </summary>
        /// <returns><c>QueueSink</c>.</returns>
        public override string ToString() => "QueueSink";
    }
    #nullable restore

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">The type of elements consumed by the lazily selected sink.</typeparam>
    /// <typeparam name="TMat">The type of the selected sink's materialized value.</typeparam>
    internal sealed class LazySink<TIn, TMat> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<Option<TMat>>>
    {
        #region Logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly LazySink<TIn, TMat> _stage;
            private readonly TaskCompletionSource<Option<TMat>> _completion;
            private bool _switching;

            public Logic(LazySink<TIn, TMat> stage, Attributes inheritedAttributes, TaskCompletionSource<Option<TMat>> completion) 
                : base(stage.Shape)
            {
                _stage = stage;
                _completion = completion;

                SetHandler(stage.In, this);
            }

            public override void PreStart() => Pull(_stage.In);

            public override void OnPush()
            {
                var element = Grab(_stage.In);
                _switching = true;

                var callback = GetAsyncCallback<Result<Sink<TIn, TMat>>>(result =>
                {
                    if (result.IsSuccess)
                    {
                        // check if the stage is still in need for the lazy sink
                        // (there could have been an OnUpstreamFailure in the meantime that has completed the promise)
                        if (!_completion.Task.IsCompleted)
                        {
                            try
                            {
                                var mat = SwitchTo(result.Value, element);
                                _completion.TrySetResult(mat);
                                SetKeepGoing(true);
                            }
                            catch (Exception ex)
                            {
                                if (_completion.TrySetException(ex))
                                {
                                    // Same discarded materialized-task pattern as #8209 and #8210: callers may never keep
                                    // this Task<Option<TMat>>, so observe the fault here to avoid a later UnobservedTaskException.
                                    _ = _completion.Task.Exception;
                                }
                                FailStage(ex);
                            }
                        }
                    }
                    else
                    {
                        if (_completion.TrySetException(result.Exception))
                        {
                            // Same discarded materialized-task pattern as #8209 and #8210: callers may never keep
                            // this Task<Option<TMat>>, so observe the fault here to avoid a later UnobservedTaskException.
                            _ = _completion.Task.Exception;
                        }
                        FailStage(result.Exception);
                    }
                });

                try
                {
                    _stage._sinkFactory(element)
                        .ContinueWith(t => callback(Result.FromTask(t)), TaskContinuationOptions.ExecuteSynchronously);
                }
                catch (Exception ex)
                {
                    if (_completion.TrySetException(ex))
                    {
                        // Same discarded materialized-task pattern as #8209 and #8210: callers may never keep
                        // this Task<Option<TMat>>, so observe the fault here to avoid a later UnobservedTaskException.
                        _ = _completion.Task.Exception;
                    }
                    FailStage(ex);
                }
            }

            public override void OnUpstreamFinish()
            {
                // ignore OnUpstreamFinish while the stage is switching but SetKeepGoing
                if (_switching)
                {
                    // there is a cached element -> the stage must not be shut down automatically because IsClosed(In) is satisfied
                    SetKeepGoing(true);
                }                
                else
                {
                    _completion.TrySetResult(Option<TMat>.None);
                    base.OnUpstreamFinish();
                }
            }

            public override void OnUpstreamFailure(Exception ex)
            {
                if (_completion.TrySetException(ex))
                {
                    // Same discarded materialized-task pattern as #8209 and #8210: callers may never keep
                    // this Task<Option<TMat>>, so observe the fault here to avoid a later UnobservedTaskException.
                    _ = _completion.Task.Exception;
                }
                base.OnUpstreamFailure(ex);
            }

            private TMat SwitchTo(Sink<TIn, TMat> sink, TIn firstElement)
            {
                var firstElementPushed = false;

                var subOutlet = new SubSourceOutlet<TIn>(this, "LazySink");

                var matVal = Source.FromGraph(subOutlet.Source).RunWith(sink, Interpreter.SubFusingMaterializer);

                void MaybeCompleteStage()
                {
                    if (IsClosed(_stage.In) && subOutlet.IsClosed)
                        CompleteStage();
                }

                // The stage must not be shut down automatically; it is completed when MaybeCompleteStage decides
                SetKeepGoing(true);

                SetHandler(_stage.In, new LambdaInHandler(
                    () => subOutlet.Push(Grab(_stage.In)),
                    () =>
                    {
                        if (firstElementPushed)
                        {
                            subOutlet.Complete();
                            MaybeCompleteStage();
                        }
                    },
                    ex =>
                    {
                        // propagate exception irrespective if the cached element has been pushed or not
                        subOutlet.Fail(ex);
                        MaybeCompleteStage();
                    }));

                subOutlet.SetHandler(new LambdaOutHandler(
                    onPull: () =>
                    {
                        if (firstElementPushed)
                            Pull(_stage.In);
                        else
                        {
                            // the demand can be satisfied right away by the cached element
                            firstElementPushed = true;
                            subOutlet.Push(firstElement);
                            // In.OnUpstreamFinished was not propagated if it arrived before the cached element was pushed
                            // -> check if the completion must be propagated now
                            if (IsClosed(_stage.In))
                            {
                                subOutlet.Complete();
                                MaybeCompleteStage();
                            }
                        }
                    },
                    onDownstreamFinish: cause =>
                    {
                        if (!IsClosed(_stage.In)) Cancel(_stage.In, cause);
                        MaybeCompleteStage();
                    }));

                return matVal;
            }
        }

        #endregion

        private readonly Func<TIn, Task<Sink<TIn, TMat>>> _sinkFactory;

        /// <summary>
        /// Creates a lazy sink whose factory is invoked asynchronously after the first upstream element arrives.
        /// </summary>
        /// <param name="sinkFactory">Creates the sink to materialize after receiving the first element.</param>
        public LazySink(Func<TIn, Task<Sink<TIn, TMat>>> sinkFactory)
        {
            _sinkFactory = sinkFactory;
            Shape = new SinkShape<TIn>(In);
        }

        /// <summary>
        /// The default attributes for this lazy sink.
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.LazySink;

        /// <summary>
        /// The inlet that receives elements before the selected sink is materialized.
        /// </summary>
        public Inlet<TIn> In { get; } = new("lazySink.in");

        /// <summary>
        /// The sink shape containing <see cref="In"/>.
        /// </summary>
        public override SinkShape<TIn> Shape { get; }

        /// <summary>
        /// Creates the stage logic and a task that yields <see cref="Option{T}.None"/> if the upstream completes
        /// without an element, or the selected sink's materialized value after the first element selects a sink. An
        /// upstream failure faults the task.
        /// </summary>
        /// <param name="inheritedAttributes">The attributes inherited by the stage.</param>
        /// <returns>The stage logic and its optional materialized-value task.</returns>
        public override ILogicAndMaterializedValue<Task<Option<TMat>>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = TaskEx.NonBlockingTaskCompletionSource<Option<TMat>>();
            var stageLogic = new Logic(this, inheritedAttributes, completion);
            return new LogicAndMaterializedValue<Task<Option<TMat>>>(stageLogic, completion.Task);
        }

        /// <summary>
        /// Returns the diagnostic name of this stage.
        /// </summary>
        /// <returns><c>LazySink</c>.</returns>
        public override string ToString() => "LazySink";
    }

    internal sealed class ObservableSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, IObservable<T>>
    {
        #region internal classes

        private sealed class ObserverDisposable : IDisposable
        {
            private readonly ObservableLogic _logic;
            private readonly IObserver<T> _observer;
            private readonly AtomicBoolean _disposed = new(false);

            public ObserverDisposable(ObservableLogic logic, IObserver<T> observer)
            {
                _logic = logic;
                _observer = observer;
            }

            public void Dispose() => Dispose(unregister: true);

            public void Dispose(bool unregister)
            {
                if (_disposed.CompareAndSet(false, true))
                {
                    if (unregister) _logic.Remove(_observer);

                    _observer.OnCompleted();
                }
                else
                {
                    throw new ObjectDisposedException("ObservableSink subscription has been already disposed.");
                }
            }
        }

        private sealed class ObservableLogic : GraphStageLogic, IObservable<T>
        {
            private readonly ObservableSinkStage<T> _stage;
            private ImmutableDictionary<IObserver<T>, ObserverDisposable> _observers = ImmutableDictionary<IObserver<T>, ObserverDisposable>.Empty;

            public ObservableLogic(ObservableSinkStage<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.Inlet,
                    onPush: () =>
                    {
                        var element = Grab(stage.Inlet);
                        foreach (var observer in _observers.Keys) observer.OnNext(element);

                        Pull(stage.Inlet);
                    },
                    onUpstreamFinish: () =>
                    {
                        var old = Interlocked.Exchange(ref _observers, ImmutableDictionary<IObserver<T>, ObserverDisposable>.Empty);
                        foreach (var disposer in old.Values) disposer.Dispose(unregister: false);
                    },
                    onUpstreamFailure: e =>
                    {
                        foreach (var observer in _observers.Keys) observer.OnError(e);
                        _observers = ImmutableDictionary<IObserver<T>, ObserverDisposable>.Empty;
                    });
            }

            public override void PreStart()
            {
                base.PreStart();
                Pull(_stage.Inlet);
            }

            public void Remove(IObserver<T> observer)
            {
                ImmutableInterlocked.TryRemove(ref _observers, observer, out var _);
            }

            public IDisposable Subscribe(IObserver<T> observer) =>
                ImmutableInterlocked.GetOrAdd(ref _observers, observer, new ObserverDisposable(this, observer));
        }

        #endregion


        public ObservableSinkStage()
        {
            Shape = new SinkShape<T>(Inlet);
        }

        public Inlet<T> Inlet { get; } = new("observable.in");
        public override SinkShape<T> Shape { get; }
        public override ILogicAndMaterializedValue<IObservable<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var observable = new ObservableLogic(this);
            return new LogicAndMaterializedValue<IObservable<T>>(observable, observable);
        }
    }
}
