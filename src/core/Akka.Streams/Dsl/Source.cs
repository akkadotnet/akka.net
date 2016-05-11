//-----------------------------------------------------------------------
// <copyright file="Source.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Util;
using Reactive.Streams;
// ReSharper disable UnusedMember.Global

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

        public SourceShape<TOut> Shape => (SourceShape<TOut>)Module.Shape;
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

        /// <summary>
        /// Concatenate the given <see cref="Source{TOut,TMat}"/> to this <see cref="Flow{TIn,TOut,TMat}"/>, meaning that once this
        /// Flow’s input is exhausted and all result elements have been generated,
        /// the Source’s elements will be produced.
        ///
        /// Note that the <see cref="Source{TOut,TMat}"/> is materialized together with this Flow and just kept
        /// from producing elements by asserting back-pressure until its time comes.
        ///
        /// If this <see cref="Flow{TIn,TOut,TMat}"/> gets upstream error - no elements from the given <see cref="Source{TOut,TMat}"/> will be pulled.
        ///
        /// @see <see cref="Concat{TIn,TOut}"/>.
        ///
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        public Source<TOut, TMat3> ConcatMaterialized<TMat2, TMat3>(IGraph<SourceShape<TOut>, TMat2> that,
            Func<TMat, TMat2, TMat3> materializedFunction)
        {
            return ViaMaterialized(InternalFlowOperations.ConcatGraph(that), materializedFunction);
        }

        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.WithAttributes(Attributes attributes)
        {
            return WithAttributes(attributes);
        }

        public IGraph<SourceShape<TOut>, TMat> AddAttributes(Attributes attributes)
        {
            return WithAttributes(Module.Attributes.And(attributes));
        }

        public IGraph<SourceShape<TOut>, TMat> Named(string name)
        {
            return AddAttributes(Attributes.CreateName(name));
        }

        public IGraph<SourceShape<TOut>, TMat> Async()
        {
            return AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }

        /// <summary>
        /// Nests the current Source and returns a Source with the given Attributes
        /// </summary>
        /// <param name="attributes">The attributes to add</param>
        /// <returns>A new Source with the added attributes</returns>
        public Source<TOut, TMat> WithAttributes(Attributes attributes)
        {
            return new Source<TOut, TMat>(Module.WithAttributes(attributes));
        }

        public Source<TOut2, TMat3> ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            if (flow.Module == GraphStages.Identity<TOut2>().Module) return this as Source<TOut2, TMat3>;
            else
            {
                var flowCopy = flow.Module.CarbonCopy();
                return new Source<TOut2, TMat3>(Module
                    .Fuse(flowCopy, Shape.Outlet, flowCopy.Shape.Inlets.First(), combine)
                    .ReplaceShape(new SourceShape<TOut2>((Outlet<TOut2>) flowCopy.Shape.Outlets.First())));
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

        internal Source<TOut2, TMat> DeprecatedAndThen<TOut2>(StageModule<TOut, TOut2> op)
        {
            //No need to copy here, op is a fresh instance
            return new Source<TOut2, TMat>(Module
                .Fuse(op, Shape.Outlet, op.In)
                .ReplaceShape(new SourceShape<TOut2>(op.Out)));
        }

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/> and run it. The returned value is the materialized value
        /// of the <see cref="Sink{TIn,TMat}"/> , e.g. the <see cref="IPublisher{TIn}"/> of a <see cref="Sink.Publisher{TIn}"/>.
        /// </summary>
        public TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
        {
            return ToMaterialized(sink, Keep.Right).Run(materializer);
        }

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a fold function.
        /// The given function is invoked for every received element, giving it its previous
        /// output (or the given <paramref name="zero"/> value) and the element as input.
        /// The returned <see cref="Task{TOut2}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with Failure
        /// if there is a failure signaled in the stream.
        /// </summary>
        public Task<TOut2> RunAggregate<TOut2>(TOut2 zero, Func<TOut2, TOut, TOut2> aggregate, IMaterializer materializer)
        {
            return RunWith(Sink.Aggregate(zero, aggregate), materializer);
        }

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a reduce function.
        /// The given function is invoked for every received element, giving it its previous
        /// output (from the second element) and the element as input.
        /// The returned <see cref="Task{TOut}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with Failure
        /// if there is a failure signaled in the stream.
        /// </summary>
        public Task<TOut> RunSum(Func<TOut, TOut, TOut> reduce, IMaterializer materializer)
        {
            return RunWith(Sink.Sum(reduce), materializer);
        }

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a foreach procedure. The given procedure is invoked
        /// for each received element.
        /// The returned <see cref="Task"/> will be completed with Success when reaching the
        /// normal end of the stream, or completed with Failure if there is a failure signaled in
        /// the stream.
        /// </summary>
        public Task RunForeach(Action<TOut> action, IMaterializer materializer)
        {
            return RunWith(Sink.ForEach(action), materializer);
        }

        /// <summary>
        /// Combines several sources with fun-in strategy like <see cref="Merge{TIn,TOut}"/> or <see cref="Concat{TIn,TOut}"/> and returns <see cref="Source{TOut,TMat}"/>.
        /// </summary>
        public Source<U, NotUsed> Combine<T, U>(Source<T, NotUsed> first, Source<T, NotUsed> second, Source<T, NotUsed>[] rest, Func<int, IGraph<UniformFanInShape<T,U>, NotUsed>> strategy)
        {
            return Source.FromGraph(GraphDsl.Create<SourceShape<U>, NotUsed>(b =>
            {
                var c = b.Add(strategy(rest.Length + 2));
                b.From(first).To(c.In(0));
                b.From(second).To(c.In(1));

                for (var i = 0; i < rest.Length; i++)
                    b.From(rest[i]).To(c.In(i + 2));
                return new SourceShape<U>(c.Out);
            }));
        }
    }

    public static class Source
    {
        private static SourceShape<T> Shape<T>(string name)
        {
            return new SourceShape<T>(new Outlet<T>(name + ".out"));
        }

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IPublisher{T}"/>.
        /// 
        /// Construct a transformation starting with given publisher. The transformation steps
        /// are executed by a series of <see cref="IProcessor{TIn,TOut}"/> instances
        /// that mediate the flow of elements downstream and the propagation of
        /// back-pressure upstream.
        /// </summary>
        public static Source<T, NotUsed> FromPublisher<T>(IPublisher<T> publisher)
        {
            return new Source<T, NotUsed>(new PublisherSource<T>(publisher, DefaultAttributes.PublisherSource, Shape<T>("PublisherSource")));
        }

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IEnumerator{T}"/>.
        /// Example usage: Source.FromEnumerator(() => Enumerable.Range(1, 10))
        /// 
        /// Start a new <see cref="Source{TOut,TMat}"/> from the given function that produces an <see cref="IEnumerable{T}"/>.
        /// The produced stream of elements will continue until the enumerator runs empty
        /// or fails during evaluation of the <see cref="IEnumerator.MoveNext"/> method.
        /// Elements are pulled out of the enumerator in accordance with the demand coming
        /// from the downstream transformation steps.
        /// </summary>
        public static Source<T, NotUsed> FromEnumerator<T>(Func<IEnumerator<T>> enumeratorFactory)
        {
            return From(new EnumeratorEnumerable<T>(enumeratorFactory));
        }

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IEnumerable{T}"/>.
        /// Example usage: Source.From(Enumerable.Range(1, 10))
        /// 
        /// Starts a new <see cref="Source{TOut,TMat}"/> from the given <see cref="IEnumerable{T}"/>. This is like starting from an
        /// Enumerator, but every Subscriber directly attached to the Publisher of this
        /// stream will see an individual flow of elements (always starting from the
        /// beginning) regardless of when they subscribed.
        /// </summary>
        public static Source<T, NotUsed> From<T>(IEnumerable<T> enumerable)
        {
            return Single(enumerable).SelectMany(x => x).WithAttributes(DefaultAttributes.EnumerableSource);
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> with one element.
        /// Every connected <see cref="Sink{TIn,TMat}"/> of this stream will see an individual stream consisting of one element.
        /// </summary>
        public static Source<T, NotUsed> Single<T>(T element)
        {
            return FromGraph(new SingleSource<T>(element).WithAttributes(DefaultAttributes.SingleSource));
        }

        /// <summary>
        /// A graph with the shape of a source logically is a source, this method makes
        /// it so also in type.
        /// </summary>
        public static Source<T, TMat> FromGraph<T, TMat>(IGraph<SourceShape<T>, TMat> source)
        {
            return source as Source<T, TMat> ?? new Source<T, TMat>(source.Module);
        }

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> from the given <see cref="Task{T}"/>. The stream will consist of
        /// one element when the <see cref="Task{T}"/> is completed with a successful value, which
        /// may happen before or after materializing the <see cref="IFlow{TOut,TMat}"/>.
        /// The stream terminates with a failure if the task is completed with a failure.
        /// </summary>
        public static Source<T, NotUsed> FromTask<T>(Task<T> task) => FromGraph(new TaskSource<T>(task));

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
        public static Source<T, NotUsed> Repeat<T>(T element)
        {
            var next = new Tuple<T, T>(element, element);
            return Unfold(element, _ => next).WithAttributes(DefaultAttributes.Repeat);
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
        public static Source<TElem, NotUsed> Unfold<TState, TElem>(TState state, Func<TState, Tuple<TState, TElem>> unfold)
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
        public static Source<TElem, NotUsed> UnfoldAsync<TState, TElem>(TState state, Func<TState, Task<Tuple<TState, TElem>>> unfoldAsync)
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
        public static Source<TElem, NotUsed> UnfoldInfinite<TState, TElem>(TState state, Func<TState, Tuple<TState, TElem>> unfold)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// A <see cref="Source{TOut,TMat}"/> with no elements, i.e. an empty stream that is completed immediately for every connected <see cref="Sink{TIn,TMat}"/>.
        /// </summary> 
        public static Source<T, NotUsed> Empty<T>()
        {
            return new Source<T, NotUsed>(new PublisherSource<T>(
                EmptyPublisher<T>.Instance,
                DefaultAttributes.EmptySource,
                Shape<T>("EmptySource")));
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> which materializes a <see cref="TaskCompletionSource{TResult}"/> which controls what element
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
        public static Source<T, NotUsed> Failed<T>(Exception cause)
        {
            return new Source<T, NotUsed>(new PublisherSource<T>(
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
        /// created according to the passed in <see cref="Props"/>. Actor created by the <see cref="Props"/> must
        /// be <see cref="Actors.ActorPublisher{T}"/>.
        /// </summary>
        public static Source<T, IActorRef> ActorPublisher<T>(Props props)
        {
            if (!typeof(Actors.ActorPublisher<T>).IsAssignableFrom(props.Type)) throw new ArgumentException("Actor must be ActorPublisher");
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
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0", nameof(bufferSize));
            if (overflowStrategy == OverflowStrategy.Backpressure) throw new NotSupportedException("Backpressure overflow strategy is not supported");

            return new Source<T, IActorRef>(new ActorRefSource<T>(bufferSize, overflowStrategy, DefaultAttributes.ActorRefSource, Shape<T>("ActorRefSource")));
        }


        /// <summary>
        /// Combines several sources with fun-in strategy like <see cref="Merge{TIn,TOut}"/> or <see cref="Concat{TIn,TOut}"/> and returns <see cref="Source{TOut,TMat}"/>.
        /// </summary>
        public static Source<U, NotUsed> Combine<T, U>(Source<T, NotUsed> first, Source<T, NotUsed> second, Func<int, IGraph<UniformFanInShape<T, U>, NotUsed>> strategy, params Source<T, NotUsed>[] rest)
        {
            return FromGraph(GraphDsl.Create<SourceShape<U>, NotUsed>(b =>
            {
                var c = b.Add(strategy(rest.Length + 2));
                b.From(first).To(c.In(0));
                b.From(second).To(c.In(1));

                for (var i = 0; i < rest.Length; i++)
                    b.From(rest[i]).To(c.In(i + 2));
                return new SourceShape<U>(c.Out);
            }));
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
        /// The strategy <see cref="OverflowStrategy.Backpressure"/> will not complete <see cref="ISourceQueue{T}.OfferAsync"/> until buffer is full.
        /// 
        /// The buffer can be disabled by using <paramref name="bufferSize"/> of 0 and then received messages are dropped
        /// if there is no demand from downstream. When <paramref name="bufferSize"/> is 0 the <paramref name="overflowStrategy"/> does
        /// not matter.
        /// </summary>
        /// <param name="bufferSize">The size of the buffer in element count</param>
        /// <param name="overflowStrategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        public static Source<T, ISourceQueue<T>> Queue<T>(int bufferSize, OverflowStrategy overflowStrategy)
        {
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0", nameof(bufferSize));

            return FromGraph(new QueueSource<T>(bufferSize, overflowStrategy).WithAttributes(DefaultAttributes.QueueSource));
        }
    }
}