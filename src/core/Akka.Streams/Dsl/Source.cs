using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> is a set of stream processing steps that has one open output. It can comprise
    /// any number of internal sources and transformations that are wired together, or it can be
    /// an “atomic” source, e.g. from a collection or a file. Materialization turns a Source into
    /// a Reactive Streams <see cref="IPublisher{T}"/> (at least conceptually).
    /// </summary>
    public sealed class Source<TOut, TMat> : IFlow<TOut, TMat>, IGraph<SourceShape<TOut>, TMat>
    {
        public Source(IModule module)
        {
            Module = module;
        }

        public SourceShape<TOut> Shape { get { return (SourceShape<TOut>)Module.Shape; } }
        public IModule Module { get; }

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>,
        /// concatenating the processing steps of both.
        /// </summary>
        public IRunnableGraph<TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink)
        {
            return ToMaterialized(sink, Keep.Left);
        }

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>,
        /// concatenating the processing steps of both.
        /// </summary>
        public IRunnableGraph<TMat3> ToMaterialized<TMat2, TMat3>(IGraph<SinkShape<TOut>, TMat2> sink, Func<TMat, TMat2, TMat3> combine)
        {
            var sinkCopy = sink.Module.CarbonCopy();
            return new RunnableGraph<TMat3>(Module.Fuse(sinkCopy, Shape.Outlet, sinkCopy.Shape.Inlets.First(), combine));
        }

        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.WithAttributes(Attributes attributes)
        {
            return WithAttributes(attributes);
        }

        public IGraph<SourceShape<TOut>, TMat> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }

        /// <summary>
        /// Nests the current Source and returns a Source with the given Attributes
        /// </summary>
        /// <param name="attributes">The attributes to add</param>
        /// <returns>A new Source with the added attributes</returns>
        public Source<TOut, TMat> WithAttributes(Attributes attributes)
        {
            return new Source<TOut, TMat>(Module.WithAttributes(attributes).Nest());
        }

        public Source<TOut2, TMat3> ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            if (flow.Module == GraphStages.Identity<TOut2>().Module) return this as Source<TOut2, TMat3>;
            else
            {
                var flowCopy = flow.Module.CarbonCopy();
                return new Source<TOut2, TMat3>(Module
                    .Fuse(flowCopy, Shape.Outlet, flowCopy.Shape.Inlets.First(), combine)
                    .ReplaceShape(new SourceShape<TOut2>((Outlet<TOut2>)flowCopy.Shape.Outlets.First())));
            }
        }

        IFlow<T, TMat3> IFlow<TOut, TMat>.ViaMaterialized<T, TMat2, TMat3>(IGraph<FlowShape<TOut, T>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            return ViaMaterialized(flow, combine);
        }

        public Source<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
        {
            return ViaMaterialized(flow, Keep.Left);
        }

        IFlow<T2, TMat> IFlow<TOut, TMat>.Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
        {
            return Via(flow);
        }

        /// <summary>
        /// Transform only the materialized value of this Source, leaving all other properties as they were.
        /// </summary>
        public Source<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
        {
            return new Source<TOut, TMat2>(Module.TransformMaterializedValue(mapFunc));
        }

        /// <summary>
        /// Connect this `Source` to a `Sink` and run it. The returned value is the materialized value
        /// of the `Sink`, e.g. the `Publisher` of a <see cref="Sink{TIn,TMat}.Publisher"/>.
        /// </summary>
        public TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            return ToMaterialized(sink, Keep.Right).Run(materializer);
        }

        /// <summary>
        /// Shortcut for running this `Source` with a fold function.
        /// The given function is invoked for every received element, giving it its previous
        /// output (or the given <paramref name="zero"/> value) and the element as input.
        /// The returned <see cref="Task{TOut2}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with `Failure`
        /// if there is a failure signaled in the stream.
        /// </summary>
        public Task<TOut2> RunFold<TOut2>(TOut2 zero, Func<TOut2, TOut, TOut2> aggregate, IMaterializer materializer)
        {
            return RunWith(Sink.Fold(zero, aggregate), materializer);
        }

        /// <summary>
        /// Shortcut for running this `Source` with a foreach procedure. The given procedure is invoked
        /// for each received element.
        /// The returned <see cref="Task"/> will be completed with `Success` when reaching the
        /// normal end of the stream, or completed with `Failure` if there is a failure signaled in
        /// the stream.
        /// </summary>
        public Task RunForeach(Action<TOut> action, IMaterializer materializer)
        {
            return RunWith(Sink.ForEach(action), materializer);
        }
    }

    public static class Source
    {
        private static SourceShape<T> Shape<T>(string name)
        {
            return new SourceShape<T>(new Outlet<T>(name + ".out"));
        }

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from `Publisher`.
        /// 
        /// Construct a transformation starting with given publisher. The transformation steps
        /// are executed by a series of <see cref="IProcessor{TIn,TOut}"/> instances
        /// that mediate the flow of elements downstream and the propagation of
        /// back-pressure upstream.
        /// </summary>
        public static Source<T, TMat> FromPublisher<T, TMat>(IPublisher<T> publisher)
        {
            return new Source<T, TMat>(new PublisherSource<T>(publisher, DefaultAttributes.PublisherSource, Shape<T>("PublisherSource")));
        }

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IEnumerator{T}"/>.
        /// Example usage: `Source.fromIterator(() => Iterator.from(0))`
        /// 
        /// Start a new `Source` from the given function that produces anIterator.
        /// The produced stream of elements will continue until the iterator runs empty
        /// or fails during evaluation of the `next()` method.
        /// Elements are pulled out of the iterator in accordance with the demand coming
        /// from the downstream transformation steps.
        /// </summary>
        public static Source<T, Unit> FromEnumerator<T>(Func<IEnumerator<T>> enumFactory)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IEnumerable{T}"/>.
        /// Example usage: `Source(Seq(1,2,3))`
        /// 
        /// Starts a new `Source` from the given `Iterable`. This is like starting from an
        /// Iterator, but every Subscriber directly attached to the Publisher of this
        /// stream will see an individual flow of elements (always starting from the
        /// beginning) regardless of when they subscribed.
        /// </summary>
        public static Source<T, Unit> From<T>(IEnumerable<T> enumerable)
        {
            return Single(enumerable).MapConcat(x => x).WithAttributes(DefaultAttributes.EnumerableSource);
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> with one element.
        /// Every connected `Sink` of this stream will see an individual stream consisting of one element.
        /// </summary>
        public static Source<T, Unit> Single<T>(T element)
        {
            return FromGraph(new SingleSource<T>(element).WithAttributes(DefaultAttributes.SingleSource));
        }

        /// <summary>
        /// A graph with the shape of a source logically is a source, this method makes
        /// it so also in type.
        /// </summary>
        public static Source<T, TMat> FromGraph<T, TMat>(IGraph<SourceShape<T>, TMat> source)
        {
            if (source is Source<T, TMat>) return source as Source<T, TMat>;
            else return new Source<T, TMat>(source.Module);
        }

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> from the given <see cref="Task{T}"/>. The stream will consist of
        /// one element when the `Future` is completed with a successful value, which
        /// may happen before or after materializing the `Flow`.
        /// The stream terminates with a failure if the `Future` is completed with a failure.
        /// </summary>
        public static Source<T, Unit> FromTask<T>(Task<T> task)
        {
            return Single(task).MapAsyncUnordered(1, x => x).WithAttributes(DefaultAttributes.TaskSource);
        }

        /// <summary>
        /// Elements are emitted periodically with the specified interval.
        /// The tick element will be delivered to downstream consumers that has requested any elements.
        /// If a consumer has not requested any elements at the point in time when the tick
        /// element is produced it will not receive that tick element later. It will
        /// receive new tick elements as soon as it has requested more elements.
        /// </summary>
        public static Source<T, ICancelable> Tick<T>(TimeSpan initialDelay, TimeSpan interval, T tick)
        {
            return FromGraph(new TickSource<T>(initialDelay, interval, tick)).WithAttributes(DefaultAttributes.TickSource);
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> that will continually emit the given element.
        /// </summary>
        public static Source<T, Unit> Repeat<T>(T element)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> that will unfold a value of type <typeparamref name="TState"/> into
        /// a pair of the next state <typeparamref name="TState"/> and output elements of type <typeparamref name="TElem"/>.
        /// </summary>
        /// <example>
        /// For example, all the Fibonacci numbers under 10M:
        /// <code>
        ///   Source.unfold(0 → 1) {
        ///    case (a, _) if a > 10000000 ⇒ None
        ///    case (a, b) ⇒ Some((b → (a + b)) → a)
        ///   }
        /// </code>
        /// </example>
        public static Source<TElem, Unit> Unfold<TState, TElem>(TState state, Func<TState, Tuple<TState, TElem>> unfold)
        {
            return FromGraph(new Unfold<TState, TElem>(state, unfold)).WithAttributes(DefaultAttributes.Unfold);
        }

        /// <summary>
        /// Same as <see cref="Unfold{TState,TElem}"/>, but uses an async function to generate the next state-element tuple.
        /// </summary>
        /// <example>
        /// For example, all the Fibonacci numbers under 10M:
        /// <code>
        /// Source.unfoldAsync(0 → 1) {
        ///  case (a, _) if a > 10000000 ⇒ Future.successful(None)
        ///  case (a, b) ⇒ Future{
        ///    Thread.sleep(1000)
        ///    Some((b → (a + b)) → a)
        ///  }
        /// }
        /// </code>
        /// </example>
        public static Source<TElem, Unit> UnfoldAsync<TState, TElem>(TState state, Func<TState, Task<Tuple<TState, TElem>>> unfoldAsync)
        {
            return FromGraph(new UnfoldAsync<TState, TElem>(state, unfoldAsync)).WithAttributes(DefaultAttributes.UnfoldAsync);
        }

        /// <summary>
        /// Simpler <see cref="Unfold{TState,TElem}"/>, for infinite sequences. 
        /// </summary>
        /// <example>
        /// <code>
        /// {{{
        ///   Source.unfoldInf(0 → 1) {
        ///    case (a, b) ⇒ (b → (a + b)) → a
        ///   }
        /// }}}
        /// </code>
        /// </example>
        public static Source<TElem, Unit> UnfoldInfinite<TState, TElem>(TState state, Func<TState, Tuple<TState, TElem>> unfold)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// A <see cref="Source{TOut,TMat}"/> with no elements, i.e. an empty stream that is completed immediately for every connected <see cref="Sink{TIn,TMat}"/>.
        /// </summary> 
        public static Source<T, Unit> Empty<T>()
        {
            return new Source<T, Unit>(new PublisherSource<T>(
                EmptyPublisher<T>.Instance,
                DefaultAttributes.EmptySource,
                Shape<T>("EmptySource")));
        }

        /// <summary>
        /// Create a `Source` which materializes a [[scala.concurrent.Promise]] which controls what element
        /// will be emitted by the Source.
        /// If the materialized promise is completed with a Some, that value will be produced downstream,
        /// followed by completion.
        /// If the materialized promise is completed with a None, no value will be produced downstream and completion will
        /// be signalled immediately.
        /// If the materialized promise is completed with a failure, then the returned source will terminate with that error.
        /// If the downstream of this source cancels before the promise has been completed, then the promise will be completed
        /// with None.
        /// </summary>
        public static Source<T, TaskCompletionSource<T>> Maybe<T>()
        {
            return new Source<T, TaskCompletionSource<T>>(new MaybeSource<T>(
                DefaultAttributes.MaybeSource,
                new SourceShape<T>(new Outlet<T>("MaybeSource"))));
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> that immediately ends the stream with the <paramref name="cause"/> error to every connected <see cref="Sink{TIn,TMat}"/>.
        /// </summary>
        public static Source<T, Unit> Failed<T>(Exception cause)
        {
            return new Source<T, Unit>(new PublisherSource<T>(
                new ErrorPublisher<T>(cause, "FailedSource"),
                DefaultAttributes.FailedSource,
                Shape<T>("FailedSource")));
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is materialized as a <see cref="ISubscriber{T}"/>
        /// </summary>
        public static Source<T, ISubscriber<T>> AsSubscriber<T>()
        {
            return new Source<T, ISubscriber<T>>(
                new SubscriberSource<T>(DefaultAttributes.SubscriberSource,
                Shape<T>("SubscriberSource")));
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is materialized to an <see cref="IActorRef"/> which points to an Actor
        /// created according to the passed in <see cref="Props"/>. Actor created by the `props` must
        /// be <see cref="ActorPublisher{T}"/>.
        /// </summary>
        public static Source<T, IActorRef> ActorPublisher<T>(Props props)
        {
            if (!(typeof(ActorPublisher<T>).IsAssignableFrom(props.Type))) throw new ArgumentException("Actor must be ActorPublisher");
            return new Source<T, IActorRef>(new ActorPublisherSource<T>(props, DefaultAttributes.ActorPublisherSource, Shape<T>("ActorPublisherSource")));
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is materialized as an <see cref="IActorRef"/>.
        /// Messages sent to this actor will be emitted to the stream if there is demand from downstream,
        /// otherwise they will be buffered until request for demand is received.
        /// 
        /// Depending on the defined <see cref="OverflowStrategy"/> it might drop elements if
        /// there is no space available in the buffer.
        /// 
        /// The strategy <see cref="OverflowStrategy.Backpressure"/> is not supported, and an
        /// IllegalArgument("Backpressure overflowStrategy not supported") will be thrown if it is passed as argument.
        /// 
        /// The buffer can be disabled by using <paramref name="bufferSize"/> of 0 and then received messages are dropped
        /// if there is no demand from downstream. When <paramref name="bufferSize"/> is 0 the <paramref name="overflowStrategy"/> does
        /// not matter.
        /// 
        /// The stream can be completed successfully by sending the actor reference an <see cref="Status.Success"/>
        /// message in which case already buffered elements will be signaled before signaling completion,
        /// or by sending a <see cref="PoisonPill"/> in which case completion will be signaled immediately.
        /// 
        /// The stream can be completed with failure by sending <see cref="Status.Failure"/> to the
        /// actor reference. In case the Actor is still draining its internal buffer (after having received
        /// an <see cref="Status.Success"/>) before signaling completion and it receives a <see cref="Status.Failure"/>,
        /// the failure will be signaled downstream immediately (instead of the completion signal).
        /// 
        /// The actor will be stopped when the stream is completed, failed or canceled from downstream,
        /// i.e. you can watch it to get notified when that happens.
        /// </summary>
        /// <param name="bufferSize">The size of the buffer in element count</param>
        /// <param name="overflowStrategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        public static Source<T, IActorRef> ActorRef<T>(int bufferSize, OverflowStrategy overflowStrategy)
        {
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0", "bufferSize");
            if (overflowStrategy == OverflowStrategy.Backpressure) throw new NotSupportedException("Backpressure overflow strategy is not supported");

            return new Source<T, IActorRef>(new ActorRefSource<T>(bufferSize, overflowStrategy, DefaultAttributes.ActorRefSource, Shape<T>("ActorRefSource")));
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is materialized as an <see cref="ISourceQueue{T}"/>.
        /// You can push elements to the queue and they will be emitted to the stream if there is demand from downstream,
        /// otherwise they will be buffered until request for demand is received.
        /// 
        /// Depending on the defined <see cref="OverflowStrategy"/> it might drop elements if
        /// there is no space available in the buffer.
        /// 
        /// Acknowledgement mechanism is available.
        /// <see cref="ISourceQueue{T}.OfferAsync"/> returns <see cref="Task"/> which completes with true
        /// if element was added to buffer or sent downstream. It completes
        /// with false if element was dropped.
        /// 
        /// The strategy <see cref="OverflowStrategy.Backpressure"/> will not complete `offer():Future` until buffer is full.
        /// 
        /// The buffer can be disabled by using <paramref name="bufferSize"/> of 0 and then received messages are dropped
        /// if there is no demand from downstream. When <paramref name="bufferSize"/> is 0 the <paramref name="overflowStrategy"/> does
        /// not matter.
        /// </summary>
        /// <param name="bufferSize">The size of the buffer in element count</param>
        /// <param name="overflowStrategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <param name="timeout">Timeout for ``SourceQueue.offer(T):Future[Boolean]``</param>
        public static Source<T, ISourceQueue<T>> Queue<T>(int bufferSize, OverflowStrategy overflowStrategy, TimeSpan? timeout = null)
        {
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0", "bufferSize");
            var t = timeout ?? TimeSpan.FromSeconds(5);

            //return new Source<T, ISourceQueue<T>>(new AcknowledgeSource<T>(bufferSize, overflowStrategy, DefaultAttributes.AcknowledgeSource, Shape<T>("AcknowledgeSource")));
            throw new NotImplementedException();
        }
    }
}