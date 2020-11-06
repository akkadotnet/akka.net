//-----------------------------------------------------------------------
// <copyright file="Sinks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
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
using Reactive.Streams;
using Decider = Akka.Streams.Supervision.Decider;
using Directive = Akka.Streams.Supervision.Directive;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    internal interface ISinkModule
    {
        /// <summary>
        /// TBD
        /// </summary>
        Shape Shape { get; }
        /// <summary>
        /// TBD
        /// </summary>
        object Create(MaterializationContext context, out object materializer);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    [InternalApi]
    public abstract class SinkModule<TIn, TMat> : AtomicModule, ISinkModule
    {
        private readonly SinkShape<TIn> _shape;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        protected SinkModule(SinkShape<TIn> shape)
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected abstract SinkModule<TIn, TMat> NewInstance(SinkShape<TIn> shape);

        /// <summary>
        /// Create the Subscriber or VirtualPublisher that consumes the incoming
        /// stream, plus the materialized value. Since Subscriber and VirtualPublisher
        /// do not share a common supertype apart from AnyRef this is what the type
        /// union devolves into; unfortunately we do not have union types at our
        /// disposal at this point.
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public abstract object Create(MaterializationContext context, out TMat materializer);

        object ISinkModule.Create(MaterializationContext context, out object materializer)
        {
            TMat m;
            var result = Create(context, out m);
            materializer = m;
            return result;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <exception cref="NotSupportedException">TBD</exception>
        /// <returns>TBD</returns>
        public override IModule ReplaceShape(Shape shape)
        {
            if (Equals(_shape, shape))
                return this;

            throw new NotSupportedException(
                "cannot replace the shape of a Sink, you need to wrap it in a Graph for that");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override IModule CarbonCopy()
            => NewInstance(new SinkShape<TIn>(Inlet.Create<TIn>(_shape.Inlet.CarbonCopy())));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attrs">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TIn">TBD</typeparam>
    [InternalApi]
    internal class PublisherSink<TIn> : SinkModule<TIn, IPublisher<TIn>>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public PublisherSink(Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
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
            => new PublisherSink<TIn>(attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
            => new PublisherSink<TIn>(Attributes, shape);

        /// <summary>
        /// This method is the reason why SinkModule.create may return something that is
        /// not a Subscriber: a VirtualPublisher is used in order to avoid the immediate
        /// subscription a VirtualProcessor would perform (and it also saves overhead).
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TStreamBuffer">TBD</typeparam>
    internal sealed class FanoutPublisherSink<TIn, TStreamBuffer> : SinkModule<TIn, IPublisher<TIn>> where TStreamBuffer : IStreamBuffer<TIn>
    {
        private readonly Action _onTerminated;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        /// <param name="onTerminated">TBD</param>
        public FanoutPublisherSink(Attributes attributes, SinkShape<TIn> shape, Action onTerminated = null) : base(shape)
        {
            Attributes = attributes;
            _onTerminated = onTerminated;
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
            => new FanoutPublisherSink<TIn, TStreamBuffer>(attributes, AmendShape(attributes), _onTerminated);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<TIn, IPublisher<TIn>> NewInstance(SinkShape<TIn> shape)
            => new FanoutPublisherSink<TIn, TStreamBuffer>(Attributes, shape, _onTerminated);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TIn">TBD</typeparam>
    [InternalApi]
    public sealed class SubscriberSink<TIn> : SinkModule<TIn, NotUsed>
    {
        private readonly ISubscriber<TIn> _subscriber;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public SubscriberSink(ISubscriber<TIn> subscriber, Attributes attributes, SinkShape<TIn> shape) : base(shape)
        {
            Attributes = attributes;
            _subscriber = subscriber;
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
            => new SubscriberSink<TIn>(_subscriber, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<TIn, NotUsed> NewInstance(SinkShape<TIn> shape)
            => new SubscriberSink<TIn>(_subscriber, Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class CancelSink<T> : SinkModule<T, NotUsed>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public CancelSink(Attributes attributes, SinkShape<T> shape)
            : base(shape)
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
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<T, NotUsed> NewInstance(SinkShape<T> shape)
            => new CancelSink<T>(Attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public override object Create(MaterializationContext context, out NotUsed materializer)
        {
            materializer = NotUsed.Instance;
            return new CancellingSubscriber<T>();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new CancelSink<T>(attributes, AmendShape(attributes));
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Creates and wraps an actor into <see cref="ISubscriber{T}"/> from the given <see cref="Props"/>,
    /// which should be <see cref="Props"/> for an <see cref="ActorSubscriber"/>.
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    [InternalApi]
    public sealed class ActorSubscriberSink<TIn> : SinkModule<TIn, IActorRef>
    {
        private readonly Props _props;
        private readonly Attributes _attributes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="props">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public ActorSubscriberSink(Props props, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _props = props;
            _attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes => _attributes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new ActorSubscriberSink<TIn>(_props, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<TIn, IActorRef> NewInstance(SinkShape<TIn> shape)
            => new ActorSubscriberSink<TIn>(_props, _attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
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
    /// <typeparam name="TIn">TBD</typeparam>
    [InternalApi]
    public sealed class ActorRefSink<TIn> : SinkModule<TIn, NotUsed>
    {
        private readonly IActorRef _ref;
        private readonly object _onCompleteMessage;
        private readonly Attributes _attributes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="ref">TBD</param>
        /// <param name="onCompleteMessage">TBD</param>
        /// <param name="attributes">TBD</param>
        /// <param name="shape">TBD</param>
        public ActorRefSink(IActorRef @ref, object onCompleteMessage, Attributes attributes, SinkShape<TIn> shape)
            : base(shape)
        {
            _ref = @ref;
            _onCompleteMessage = onCompleteMessage;
            _attributes = attributes;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override Attributes Attributes => _attributes;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public override IModule WithAttributes(Attributes attributes)
            => new ActorRefSink<TIn>(_ref, _onCompleteMessage, attributes, AmendShape(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        /// <returns>TBD</returns>
        protected override SinkModule<TIn, NotUsed> NewInstance(SinkShape<TIn> shape)
            => new ActorRefSink<TIn>(_ref, _onCompleteMessage, _attributes, shape);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public override object Create(MaterializationContext context, out NotUsed materializer)
        {
            var actorMaterializer = ActorMaterializerHelper.Downcast(context.Materializer);
            var effectiveSettings = actorMaterializer.EffectiveSettings(context.EffectiveAttributes);
            var subscriberRef = actorMaterializer.ActorOf(context,
                ActorRefSinkActor.Props(_ref, effectiveSettings.MaxInputBufferSize, _onCompleteMessage));

            materializer = null;
            return new ActorSubscriberImpl<TIn>(subscriberRef);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("LastOrDefault.in");

        /// <summary>
        /// TBD
        /// </summary>
        public LastOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<T>();
            return new LogicAndMaterializedValue<Task<T>>(new Logic(promise, this), promise.Task);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
                if(!_completionSignalled)
                    _promise.TrySetException(new AbruptStageTerminationException(this));
            }
            
            public override void PreStart() => Pull(_stage.In);
        }

        #endregion

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("FirstOrDefault.in");

        /// <summary>
        /// TBD
        /// </summary>
        public FirstOrDefaultStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<T>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<T>();
            return new LogicAndMaterializedValue<Task<T>>(new Logic(promise, this), promise.Task);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
        /// TBD
        /// </summary>
        public SeqStage()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.SeqSink;

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("Seq.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<IImmutableList<T>>> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
        {
            var promise = new TaskCompletionSource<IImmutableList<T>>();
            return new LogicAndMaterializedValue<Task<IImmutableList<T>>>(new Logic(this, promise), promise.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "SeqStage";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    [InternalApi]
    public sealed class QueueSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, ISinkQueue<T>>
    {
        #region stage logic

        private sealed class Logic : GraphStageLogicWithCallbackWrapper<TaskCompletionSource<Option<T>>>, IInHandler
        {
            private readonly QueueSink<T> _stage;
            private readonly int _maxBuffer;
            private IBuffer<Result<Option<T>>> _buffer;
            private Option<TaskCompletionSource<Option<T>>> _currentRequest;

            public Logic(QueueSink<T> stage, int maxBuffer) : base(stage.Shape)
            {
                _stage = stage;
                _maxBuffer = maxBuffer;
                _currentRequest = new Option<TaskCompletionSource<Option<T>>>();

                SetHandler(stage.In, this);
            }

            public void OnPush()
            {
                EnqueueAndNotify(new Result<Option<T>>(Grab(_stage.In)));
                if (_buffer.Used < _maxBuffer) Pull(_stage.In);
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
                StopCallback(
                    promise =>
                            promise.SetException(new IllegalStateException("Stream is terminated. QueueSink is detached")));
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

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("QueueSink.in");

        /// <summary>
        /// TBD
        /// </summary>
        public QueueSink()
        {
            Shape = new SinkShape<T>(In);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.QueueSink;

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "QueueSink";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    internal sealed class LazySink<TIn, TMat> : GraphStageWithMaterializedValue<SinkShape<TIn>, Task<TMat>>
    {
        #region Logic

        private sealed class Logic : InGraphStageLogic
        {
            private readonly LazySink<TIn, TMat> _stage;
            private readonly TaskCompletionSource<TMat> _completion;
            private readonly Lazy<Decider> _decider;

            private bool _completed;

            public Logic(LazySink<TIn, TMat> stage, Attributes inheritedAttributes,
                TaskCompletionSource<TMat> completion) : base(stage.Shape)
            {
                _stage = stage;
                _completion = completion;
                _decider = new Lazy<Decider>(() =>
                {
                    var attr = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return attr != null ? attr.Decider : Deciders.StoppingDecider;
                });

                SetHandler(stage.In, this);
            }

            public override void OnPush()
            {
                try
                {
                    var element = Grab(_stage.In);
                    var callback = GetAsyncCallback<Result<Sink<TIn, TMat>>>(result =>
                    {
                        if (result.IsSuccess)
                            InitInternalSource(result.Value, element);
                        else
                            Failure(result.Exception);
                    });
                    _stage._sinkFactory(element)
                        .ContinueWith(t => callback(Result.FromTask(t)),
                            TaskContinuationOptions.ExecuteSynchronously);
                    SetHandler(_stage.In, new LambdaInHandler(
                            onPush: () => { },
                            onUpstreamFinish: GotCompletionEvent,
                            onUpstreamFailure: Failure
                        ));
                }
                catch (Exception ex)
                {
                    if (_decider.Value(ex) == Directive.Stop)
                        Failure(ex);
                    else
                        Pull(_stage.In);
                }
            }

            public override void OnUpstreamFinish()
            {
                CompleteStage();
                try
                {
                    _completion.TrySetResult(_stage._zeroMaterialised());
                }
                catch (Exception ex)
                {
                    _completion.SetException(ex);
                }
            }

            public override void OnUpstreamFailure(Exception e) => Failure(e);

            private void GotCompletionEvent()
            {
                SetKeepGoing(true);
                _completed = true;
            }

            public override void PreStart() => Pull(_stage.In);

            private void Failure(Exception ex)
            {
                FailStage(ex);
                _completion.SetException(ex);
            }

            private void InitInternalSource(Sink<TIn, TMat> sink, TIn firstElement)
            {
                var sourceOut = new SubSource(this, firstElement);

                try {
                    var matVal = Source.FromGraph(sourceOut.Source)
                        .RunWith(sink, Interpreter.SubFusingMaterializer);
                    _completion.TrySetResult(matVal);
                }
                catch (Exception ex)
                {
                    _completion.TrySetException(ex);
                    FailStage(ex);
                }
                
            }

            #region SubSource

            private sealed class SubSource : SubSourceOutlet<TIn>
            {
                private readonly Logic _logic;
                private readonly LazySink<TIn, TMat> _stage;

                public SubSource(Logic logic, TIn firstElement) : base(logic, "LazySink")
                {
                    _logic = logic;
                    _stage = logic._stage;

                    SetHandler(new LambdaOutHandler(onPull: () =>
                    {
                        Push(firstElement);
                        if (_logic._completed)
                            SourceComplete();
                        else
                            SwitchToFinalHandler();
                    }, onDownstreamFinish: SourceComplete));

                    logic.SetHandler(_stage.In, new LambdaInHandler(
                        onPush: () => Push(logic.Grab(_stage.In)),
                        onUpstreamFinish: logic.GotCompletionEvent,
                        onUpstreamFailure: SourceFailure));
                }

                private void SourceFailure(Exception ex)
                {
                    Fail(ex);
                    _logic.FailStage(ex);
                }

                private void SwitchToFinalHandler()
                {
                    SetHandler(new LambdaOutHandler(
                        onPull: () => _logic.Pull(_stage.In),
                        onDownstreamFinish: SourceComplete));

                    _logic.SetHandler(_stage.In, new LambdaInHandler(
                        onPush: () => Push(_logic.Grab(_stage.In)),
                        onUpstreamFinish: SourceComplete,
                        onUpstreamFailure: SourceFailure));
                }

                private void SourceComplete()
                {
                    Complete();
                    _logic.CompleteStage();
                }
            }

            #endregion
        }

        #endregion

        private readonly Func<TIn, Task<Sink<TIn, TMat>>> _sinkFactory;
        private readonly Func<TMat> _zeroMaterialised;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sinkFactory">TBD</param>
        /// <param name="zeroMaterialised">TBD</param>
        public LazySink(Func<TIn, Task<Sink<TIn, TMat>>> sinkFactory, Func<TMat> zeroMaterialised)
        {
            _sinkFactory = sinkFactory;
            _zeroMaterialised = zeroMaterialised;

            Shape = new SinkShape<TIn>(In);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.LazySink;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<TIn> In { get; } = new Inlet<TIn>("lazySink.in");

        /// <summary>
        /// TBD
        /// </summary>
        public override SinkShape<TIn> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public override ILogicAndMaterializedValue<Task<TMat>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var completion = new TaskCompletionSource<TMat>();
            var stageLogic = new Logic(this, inheritedAttributes, completion);
            return new LogicAndMaterializedValue<Task<TMat>>(stageLogic, completion.Task);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "LazySink";
    }

    internal sealed class ObservableSinkStage<T> : GraphStageWithMaterializedValue<SinkShape<T>, IObservable<T>>
    {
        #region internal classes

        private sealed class ObserverDisposable : IDisposable
        {
            private readonly ObservableLogic _logic;
            private readonly IObserver<T> _observer;
            private readonly AtomicBoolean _disposed = new AtomicBoolean(false);

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

        public Inlet<T> Inlet { get; } = new Inlet<T>("observable.in");
        public override SinkShape<T> Shape { get; }
        public override ILogicAndMaterializedValue<IObservable<T>> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var observable = new ObservableLogic(this);
            return new LogicAndMaterializedValue<IObservable<T>>(observable, observable);
        }
    }
}
