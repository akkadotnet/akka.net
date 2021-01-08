//-----------------------------------------------------------------------
// <copyright file="Source.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Extensions;
using Reactive.Streams;
// ReSharper disable UnusedMember.Global

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A <see cref="Source{TOut,TMat}"/> is a set of stream processing steps that has one open output. It can comprise
    /// any number of internal sources and transformations that are wired together, or it can be
    /// an "atomic" source, e.g. from a collection or a file. Materialization turns a Source into
    /// a Reactive Streams <see cref="IPublisher{T}"/> (at least conceptually).
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    public sealed class Source<TOut, TMat> : IFlow<TOut, TMat>, IGraph<SourceShape<TOut>, TMat>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        public Source(IModule module)
        {
            Module = module;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public SourceShape<TOut> Shape => (SourceShape<TOut>)Module.Shape;

        /// <summary>
        /// TBD
        /// </summary>
        public IModule Module { get; }

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>,
        /// concatenating the processing steps of both.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink) => ToMaterialized(sink, Keep.Left);

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>,
        /// concatenating the processing steps of both.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
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
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="that">TBD</param>
        /// <param name="materializedFunction">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut, TMat3> ConcatMaterialized<TMat2, TMat3>(IGraph<SourceShape<TOut>, TMat2> that,
            Func<TMat, TMat2, TMat3> materializedFunction)
            => ViaMaterialized(InternalFlowOperations.ConcatGraph(that), materializedFunction);

        /// <summary>
        /// Nests the current Source and returns a Source with the given Attributes
        /// </summary>
        /// <param name="attributes">The attributes to add</param>
        /// <returns>A new Source with the added attributes</returns>
        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.WithAttributes(Attributes attributes)
            => WithAttributes(attributes);

        /// <summary>
        /// Nests the current Source and returns a Source with the given Attributes
        /// </summary>
        /// <param name="attributes">The attributes to add</param>
        /// <returns>A new Source with the added attributes</returns>
        public Source<TOut, TMat> WithAttributes(Attributes attributes)
            => new Source<TOut, TMat>(Module.WithAttributes(attributes));

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.AddAttributes(Attributes attributes)
            => AddAttributes(attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="Source{TOut,TMat}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut, TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// Add a name attribute to this Source.
        /// </summary>
        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.Named(string name) => Named(name);

        /// <summary>
        /// Add a name attribute to this Source.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        IGraph<SourceShape<TOut>, TMat> IGraph<SourceShape<TOut>, TMat>.Async() => Async();

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        /// <returns>TBD</returns>
        public Source<TOut, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

        /// <summary>
        /// Use the `ask` pattern to send a request-reply message to the target <paramref name="actorRef"/>.
        /// If any of the asks times out it will fail the stream with a <see cref="AskTimeoutException"/>.
        /// 
        /// Parallelism limits the number of how many asks can be "in flight" at the same time.
        /// Please note that the elements emitted by this operator are in-order with regards to the asks being issued
        /// (i.e. same behaviour as <see cref="SourceOperations.SelectAsync{TIn,TOut,TMat}"/>).
        /// 
        /// The operator fails with an <see cref="WatchedActorTerminatedException"/> if the target actor is terminated,
        /// or with an <see cref="TimeoutException"/> in case the ask exceeds the timeout passed in.
        /// 
        /// Adheres to the <see cref="ActorAttributes.SupervisionStrategy"/> attribute.
        /// 
        /// '''Emits when''' the futures (in submission order) created by the ask pattern internally are completed. 
        /// '''Backpressures when''' the number of futures reaches the configured parallelism and the downstream backpressures. 
        /// '''Completes when''' upstream completes and all futures have been completed and all elements have been emitted. 
        /// '''Fails when''' the passed in actor terminates, or a timeout is exceeded in any of the asks performed. 
        /// '''Cancels when''' downstream cancels.
        /// </summary>
        public Source<TOut2, TMat> Ask<TOut2>(IActorRef actorRef, TimeSpan timeout, int parallelism = 2)
        {
            // I know this is not a place for it, but since Ask<T> generic param must be supplied, it's better
            // if it remain alone in generic params list (no need to provide types that will be infered)
            var askFlow = Flow.Create<TOut>()
                .Watch(actorRef)
                .SelectAsync(parallelism, async e => {
                    var reply = await actorRef.Ask(e, timeout: timeout);
                    switch (reply)
                    {
                        case TOut2 a: return a;
                        case Status.Success s when s.Status is TOut2 a: return a;
                        case Status.Failure f:
                            ExceptionDispatchInfo.Capture(f.Cause).Throw();
                            return default(TOut2);
                        default:
                            throw new InvalidOperationException($"Expected to receive response of type {nameof(TOut2)}, but got: {reply}");
                    }
                })
                .Named("ask");

            return ViaMaterialized(askFlow, Keep.Left);
        }

        /// <summary>
        /// Transform this <see cref="IFlow{T,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        IFlow<T, TMat3> IFlow<TOut, TMat>.ViaMaterialized<T, TMat2, TMat3>(IGraph<FlowShape<TOut, T>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
            => ViaMaterialized(flow, combine);

        /// <summary>
        /// Transform this <see cref="Source{TOut,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut2, TMat3> ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            if (flow.Module == GraphStages.Identity<TOut2>().Module)
            {
                if (Keep.IsLeft(combine))
                    return this as Source<TOut2, TMat3>;

                if (Keep.IsRight(combine))
                    return MapMaterializedValue(_ => NotUsed.Instance) as Source<TOut2, TMat3>;

                return MapMaterializedValue(value => combine(value, (TMat2)(object)NotUsed.Instance)) as Source<TOut2, TMat3>;
            }

            var flowCopy = flow.Module.CarbonCopy();
            return new Source<TOut2, TMat3>(Module
                .Fuse(flowCopy, Shape.Outlet, flowCopy.Shape.Inlets.First(), combine)
                .ReplaceShape(new SourceShape<TOut2>((Outlet<TOut2>)flowCopy.Shape.Outlets.First())));
        }

        /// <summary>
        /// Transform this <see cref="IFlow{TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="IFlow{TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        IFlow<T2, TMat> IFlow<TOut, TMat>.Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow) => Via(flow);

        /// <summary>
        /// Transform this <see cref="Source{TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="Source{TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public Source<T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
            => ViaMaterialized(flow, Keep.Left);

        /// <summary>
        /// Transform only the materialized value of this Source, leaving all other properties as they were.
        /// </summary>
        IFlow<TOut, TMat2> IFlow<TOut, TMat>.MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
            => MapMaterializedValue(mapFunc);

        /// <summary>
        /// Transform only the materialized value of this Source, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="mapFunc">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
            => new Source<TOut, TMat2>(Module.TransformMaterializedValue(mapFunc));

        /// <summary>
        ///  Materializes this Source immediately.
        /// </summary>
        /// <param name="materializer">The materializer.</param>
        /// <returns>A tuple containing the (1) materialized value and (2) a new <see cref="Source"/>
        ///  that can be used to consume elements from the newly materialized <see cref="Source"/>.</returns>
        public (TMat, Source<TOut, NotUsed>) PreMaterialize(IMaterializer materializer)
        {
            var tup = ToMaterialized(Sink.AsPublisher<TOut>(fanout: true), Keep.Both).Run(materializer);
            return (tup.Item1, Source.FromPublisher(tup.Item2));
        }

        /// <summary>
        /// Connect this <see cref="Source{TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/> and run it. The returned value is the materialized value
        /// of the <see cref="Sink{TIn,TMat}"/> , e.g. the <see cref="IPublisher{TIn}"/> of a <see cref="Sink.Publisher{TIn}"/>.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public TMat2 RunWith<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
            => ToMaterialized(sink, Keep.Right).Run(materializer);

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a fold function.
        /// The given function is invoked for every received element, giving it its previous
        /// output (or the given <paramref name="zero"/> value) and the element as input.
        /// The returned <see cref="Task{TOut2}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with Failure
        /// if there is a failure signaled in the stream.
        /// </summary>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public Task<TOut2> RunAggregate<TOut2>(TOut2 zero, Func<TOut2, TOut, TOut2> aggregate, IMaterializer materializer)
            => RunWith(Sink.Aggregate(zero, aggregate), materializer);

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a async <paramref name="aggregate"/> function.
        /// The given function is invoked for every received element, giving it its previous
        /// output (or the given <paramref name="zero"/> value) and the element as input.
        /// The returned <see cref="Task{TOut2}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with Failure
        /// if there is a failure signaled in the stream.
        /// </summary>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="zero">TBD</param>
        /// <param name="aggregate">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public Task<TOut2> RunAggregateAsync<TOut2>(TOut2 zero, Func<TOut2, TOut, Task<TOut2>> aggregate, IMaterializer materializer)
            => RunWith(Sink.AggregateAsync(zero, aggregate), materializer);

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a reduce function.
        /// The given function is invoked for every received element, giving it its previous
        /// output (from the second element) and the element as input.
        /// The returned <see cref="Task{TOut}"/> will be completed with value of the final
        /// function evaluation when the input stream ends, or completed with Failure
        /// if there is a failure signaled in the stream.
        /// </summary>
        /// <param name="reduce">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public Task<TOut> RunSum(Func<TOut, TOut, TOut> reduce, IMaterializer materializer)
            => RunWith(Sink.Sum(reduce), materializer);

        /// <summary>
        /// Shortcut for running this <see cref="Source{TOut,TMat}"/> with a foreach procedure. The given procedure is invoked
        /// for each received element.
        /// The returned <see cref="Task"/> will be completed with Success when reaching the
        /// normal end of the stream, or completed with Failure if there is a failure signaled in
        /// the stream.
        /// </summary>
        /// <param name="action">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public Task RunForeach(Action<TOut> action, IMaterializer materializer)
            => RunWith(Sink.ForEach(action), materializer);

        /// <summary>
        /// Combines several sources with fun-in strategy like <see cref="Merge{TIn,TOut}"/> or <see cref="Concat{TIn,TOut}"/> and returns <see cref="Source{TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="first">TBD</param>
        /// <param name="second">TBD</param>
        /// <param name="strategy">TBD</param>
        /// <param name="rest">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut2, NotUsed> Combine<T, TOut2>(Source<T, NotUsed> first, Source<T, NotUsed> second, Func<int, IGraph<UniformFanInShape<T, TOut2>, NotUsed>> strategy, params Source<T, NotUsed>[] rest)
            => Source.FromGraph(GraphDsl.Create(b =>
            {
                var c = b.Add(strategy(rest.Length + 2));
                b.From(first).To(c.In(0));
                b.From(second).To(c.In(1));

                for (var i = 0; i < rest.Length; i++)
                    b.From(rest[i]).To(c.In(i + 2));
                return new SourceShape<TOut2>(c.Out);
            }));

        /// <summary>
        /// Combine the elements of multiple streams into a stream of lists.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="sources">TBD</param>
        /// <returns>TBD</returns>
        public Source<IImmutableList<T>, NotUsed> ZipN<T>(IEnumerable<Source<T, NotUsed>> sources)
            => ZipWithN(x => x, sources);

        /// <summary>
        /// Combine the elements of multiple streams into a stream of sequences using a combiner function.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="zipper">TBD</param>
        /// <param name="sources">TBD</param>
        /// <returns>TBD</returns>
        public Source<TOut2, NotUsed> ZipWithN<T, TOut2>(Func<IImmutableList<T>, TOut2> zipper,
            IEnumerable<Source<T, NotUsed>> sources)
        {
            var s = sources.ToList();
            Source<TOut2, NotUsed> source;

            if (s.Count == 0)
                source = Source.Empty<TOut2>();
            else if (s.Count == 1)
                source = s[0].Select(t => zipper(ImmutableList<T>.Empty.Add(t)));
            else
                source = Combine(s[0], s[1], i => new ZipWithN<T, TOut2>(zipper, i), s.Skip(2).ToArray());

            return source.AddAttributes(DefaultAttributes.ZipWithN);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Source({Shape}, {Module})";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Source
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static SourceShape<T> Shape<T>(string name) => new SourceShape<T>(new Outlet<T>(name + ".out"));

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IPublisher{T}"/>.
        /// 
        /// Construct a transformation starting with given publisher. The transformation steps
        /// are executed by a series of <see cref="IProcessor{TIn,TOut}"/> instances
        /// that mediate the flow of elements downstream and the propagation of
        /// back-pressure upstream.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="publisher">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> FromPublisher<T>(IPublisher<T> publisher)
            => new Source<T, NotUsed>(new PublisherSource<T>(publisher, DefaultAttributes.PublisherSource, Shape<T>("PublisherSource")));

        /// <summary>
        /// Helper to create <see cref="Source{TOut,TMat}"/> from <see cref="IEnumerator{T}"/>.
        /// Example usage: Source.FromEnumerator(() => Enumerable.Range(1, 10))
        /// 
        /// Start a new <see cref="Source{TOut,TMat}"/> from the given function that produces an <see cref="IEnumerable{T}"/>.
        /// The produced stream of elements will continue until the enumerator runs empty
        /// or fails during evaluation of the <see cref="System.Collections.IEnumerator.MoveNext">IEnumerator&lt;T&gt;.MoveNext</see> method.
        /// Elements are pulled out of the enumerator in accordance with the demand coming
        /// from the downstream transformation steps.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="enumeratorFactory">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> FromEnumerator<T>(Func<IEnumerator<T>> enumeratorFactory)
            => From(new EnumeratorEnumerable<T>(enumeratorFactory));

        /// <summary>
        /// Create <see cref="Source{TOut,TMat}"/> that will continually produce given elements in specified order.
        /// Start a new cycled <see cref="Source{TOut,TMat}"/> from the given elements. The producer stream of elements
        /// will continue infinitely by repeating the sequence of elements provided by function parameter.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="enumeratorFactory">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> Cycle<T>(Func<IEnumerator<T>> enumeratorFactory)
        {
            var continualEnumerator = new ContinuallyEnumerable<T>(enumeratorFactory).GetEnumerator();
            return FromEnumerator(() => continualEnumerator).WithAttributes(DefaultAttributes.CycledSource);
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="enumerable">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> From<T>(IEnumerable<T> enumerable)
            => Single(enumerable).SelectMany(x => x).WithAttributes(DefaultAttributes.EnumerableSource);

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> with one element.
        /// Every connected <see cref="Sink{TIn,TMat}"/> of this stream will see an individual stream consisting of one element.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> Single<T>(T element)
            => FromGraph(new SingleSource<T>(element).WithAttributes(DefaultAttributes.SingleSource));

        /// <summary>
        /// A graph with the shape of a source logically is a source, this method makes
        /// it so also in type.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="source">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, TMat> FromGraph<T, TMat>(IGraph<SourceShape<T>, TMat> source)
            => source as Source<T, TMat> ?? new Source<T, TMat>(source.Module);

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> from the given <see cref="Task{T}"/>. The stream will consist of
        /// one element when the <see cref="Task{T}"/> is completed with a successful value, which
        /// may happen before or after materializing the <see cref="IFlow{TOut,TMat}"/>.
        /// The stream terminates with a failure if the task is completed with a failure.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="task">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> FromTask<T>(Task<T> task) => FromGraph(new TaskSource<T>(task));

        /// <summary>
        /// Elements are emitted periodically with the specified interval.
        /// The tick element will be delivered to downstream consumers that has requested any elements.
        /// If a consumer has not requested any elements at the point in time when the tick
        /// element is produced it will not receive that tick element later. It will
        /// receive new tick elements as soon as it has requested more elements.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        /// <param name="tick">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, ICancelable> Tick<T>(TimeSpan initialDelay, TimeSpan interval, T tick)
            => FromGraph(new TickSource<T>(initialDelay, interval, tick)).WithAttributes(DefaultAttributes.TickSource);

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> that will continually emit the given element.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="element">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> Repeat<T>(T element)
        {
            var next = (element, element);
            return Unfold(element, _ => next.AsOption()).WithAttributes(DefaultAttributes.Repeat);
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
        /// <typeparam name="TState">TBD</typeparam>
        /// <typeparam name="TElem">TBD</typeparam>
        /// <param name="state">TBD</param>
        /// <param name="unfold">TBD</param>
        /// <returns>TBD</returns>
        public static Source<TElem, NotUsed> Unfold<TState, TElem>(TState state, Func<TState, Option<(TState, TElem)>> unfold)
            => FromGraph(new Unfold<TState, TElem>(state, unfold)).WithAttributes(DefaultAttributes.Unfold);

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
        /// <typeparam name="TState">TBD</typeparam>
        /// <typeparam name="TElem">TBD</typeparam>
        /// <param name="state">TBD</param>
        /// <param name="unfoldAsync">TBD</param>
        /// <returns>TBD</returns>
        public static Source<TElem, NotUsed> UnfoldAsync<TState, TElem>(TState state, Func<TState, Task<Option<(TState, TElem)>>> unfoldAsync)
            => FromGraph(new UnfoldAsync<TState, TElem>(state, unfoldAsync)).WithAttributes(DefaultAttributes.UnfoldAsync);

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
        /// <typeparam name="TState">TBD</typeparam>
        /// <typeparam name="TElem">TBD</typeparam>
        /// <param name="state">TBD</param>
        /// <param name="unfold">TBD</param>
        /// <returns>TBD</returns>
        public static Source<TElem, NotUsed> UnfoldInfinite<TState, TElem>(TState state, Func<TState, (TState, TElem)> unfold)
            => FromGraph(new UnfoldInfinite<TState, TElem>(state, unfold)).WithAttributes(DefaultAttributes.UnfoldInf);

        /// <summary>
        /// A <see cref="Source{TOut,TMat}"/> with no elements, i.e. an empty stream that is completed immediately for every connected <see cref="Sink{TIn,TMat}"/>.
        /// </summary> 
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> Empty<T>() => FromGraph(new EmptySource<T>());

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> which materializes a <see cref="TaskCompletionSource{TResult}"/> which controls what element
        /// will be emitted by the Source.
        /// If the materialized promise is completed with a Some, that value will be produced downstream,
        /// followed by completion.
        /// If the materialized promise is completed with a None, no value will be produced downstream and completion will
        /// be signaled immediately.
        /// If the materialized promise is completed with a failure, then the returned source will terminate with that error.
        /// If the downstream of this source cancels before the promise has been completed, then the promise will be completed
        /// with None.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Source<T, TaskCompletionSource<T>> Maybe<T>()
        {
            return new Source<T, TaskCompletionSource<T>>(
                new MaybeSource<T>(DefaultAttributes.MaybeSource,
                    new SourceShape<T>(new Outlet<T>("MaybeSource"))));
        }

        /// <summary>
        /// Create a <see cref="Source{TOut,TMat}"/> that immediately ends the stream with the <paramref name="cause"/> error to every connected <see cref="Sink{TIn,TMat}"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="cause">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> Failed<T>(Exception cause)
        {
            return new Source<T, NotUsed>(new PublisherSource<T>(
                new ErrorPublisher<T>(cause, "FailedSource"),
                DefaultAttributes.FailedSource,
                Shape<T>("FailedSource")));
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is not materialized until there is downstream demand, when the source gets materialized
        /// the materialized task is completed with its value, if downstream cancels or fails without any demand the
        /// <paramref name="create"/> factory is never called and the materialized <see cref="Task{TResult}"/> is failed.
        /// </summary>
        public static Source<TOut, Task<TMat>> Lazily<TOut, TMat>(Func<Source<TOut, TMat>> create)
            => FromGraph(LazySource.Create(create));

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is materialized as a <see cref="ISubscriber{T}"/>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="props">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified actor <paramref name="props"/> is not of type <see cref="Actors.ActorPublisher{T}"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public static Source<T, IActorRef> ActorPublisher<T>(Props props)
        {
            if (!typeof(Actors.ActorPublisher<T>).IsAssignableFrom(props.Type))
                throw new ArgumentException("Actor must be ActorPublisher");

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
        /// not matter. An async boundary is added after this Source; as such, it is never safe to assume the downstream will always generate demand.
        /// 
        /// The stream can be completed successfully by sending the actor reference a <see cref="Status.Success"/>
        /// message (whose content will be ignored) in which case already buffered elements will be signaled before signaling completion,
        /// or by sending <see cref="PoisonPill"/> in which case completion will be signaled immediately.
        /// 
        /// The stream can be completed with failure by sending a <see cref="Status.Failure"/> to the
        /// actor reference. In case the Actor is still draining its internal buffer (after having received
        /// a <see cref="Status.Success"/>) before signaling completion and it receives a <see cref="Status.Failure"/>,
        /// the failure will be signaled downstream immediately (instead of the completion signal).
        /// 
        /// The actor will be stopped when the stream is completed, failed or canceled from downstream,
        /// i.e. you can watch it to get notified when that happens.
        /// </summary>
        /// <seealso cref="Queue{T}"/>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="bufferSize">The size of the buffer in element count</param>
        /// <param name="overflowStrategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="bufferSize"/> is less than zero.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown when the specified <paramref name="overflowStrategy"/> is of type <see cref="OverflowStrategy.Backpressure"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public static Source<T, IActorRef> ActorRef<T>(int bufferSize, OverflowStrategy overflowStrategy)
        {
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0", nameof(bufferSize));
            if (overflowStrategy == OverflowStrategy.Backpressure) throw new NotSupportedException("Backpressure overflow strategy is not supported");

            return new Source<T, IActorRef>(new ActorRefSource<T>(bufferSize, overflowStrategy, DefaultAttributes.ActorRefSource, Shape<T>("ActorRefSource")));
        }


        /// <summary>
        /// Combines several sources with fun-in strategy like <see cref="Merge{TIn,TOut}"/> or <see cref="Concat{TIn,TOut}"/> and returns <see cref="Source{TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="first">TBD</param>
        /// <param name="second">TBD</param>
        /// <param name="strategy">TBD</param>
        /// <param name="rest">TBD</param>
        /// <returns>TBD</returns>
        public static Source<TOut2, NotUsed> Combine<T, TOut2>(Source<T, NotUsed> first, Source<T, NotUsed> second, Func<int, IGraph<UniformFanInShape<T, TOut2>, NotUsed>> strategy, params Source<T, NotUsed>[] rest)
            => FromGraph(GraphDsl.Create(b =>
            {
                var c = b.Add(strategy(rest.Length + 2));
                b.From(first).To(c.In(0));
                b.From(second).To(c.In(1));

                for (var i = 0; i < rest.Length; i++)
                    b.From(rest[i]).To(c.In(i + 2));
                return new SourceShape<TOut2>(c.Out);
            }));

        /// <summary>
        /// Combines two sources with fan-in strategy like <see cref="Merge{TIn,TOut}"/> or <see cref="Concat{TIn,TOut}"/> and returns <see cref="Source{TOut,TMat}"/> with a materialized value.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat1">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMatOut">TBD</typeparam>
        /// <param name="first">TBD</param>
        /// <param name="second">TBD</param>
        /// <param name="strategy">TBD</param>
        /// <param name="combineMaterializers">TBD</param>
        /// <returns>TBD</returns>
        public static Source<TOut2, TMatOut> CombineMaterialized<T, TOut2, TMat1, TMat2, TMatOut>(Source<T, TMat1> first, Source<T, TMat2> second, Func<int, IGraph<UniformFanInShape<T, TOut2>, NotUsed>> strategy, Func<TMat1, TMat2, TMatOut> combineMaterializers)
        {
            var secondPartiallyCombined = GraphDsl.Create(second, (b, secondShape) =>
            {
                var c = b.Add(strategy(2));
                b.From(secondShape).To(c.In(1));
                return new FlowShape<T, TOut2>(c.In(0), c.Out);
            });
            return first.ViaMaterialized(secondPartiallyCombined, combineMaterializers);
        }

        /// <summary>
        /// Combines the elements of multiple streams into a stream of lists.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="sources">TBD</param>
        /// <returns>TBD</returns>
        public static Source<IImmutableList<T>, NotUsed> ZipN<T>(IEnumerable<Source<T, NotUsed>> sources)
            => ZipWithN(x => x, sources);

        /// <summary>
        /// Combines the elements of multiple streams into a stream of sequences using a combiner function.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="zipper">TBD</param>
        /// <param name="sources">TBD</param>
        /// <returns>TBD</returns>
        public static Source<TOut2, NotUsed> ZipWithN<T, TOut2>(Func<IImmutableList<T>, TOut2> zipper,
            IEnumerable<Source<T, NotUsed>> sources)
        {
            var s = sources.ToList();
            Source<TOut2, NotUsed> source;

            if (s.Count == 0)
                source = Empty<TOut2>();
            else if (s.Count == 1)
                source = s[0].Select(t => zipper(ImmutableList<T>.Empty.Add(t)));
            else
                source = Combine(s[0], s[1], i => new ZipWithN<T, TOut2>(zipper, i), s.Skip(2).ToArray());

            return source.AddAttributes(DefaultAttributes.ZipWithN);
        }

        /// <summary>
        /// Creates a <see cref="Source{TOut,TMat}"/> that is materialized as an <see cref="ISourceQueueWithComplete{T}"/>.
        /// You can push elements to the queue and they will be emitted to the stream if there is demand from downstream,
        /// otherwise they will be buffered until request for demand is received.
        /// 
        /// Depending on the defined <see cref="OverflowStrategy"/> it might drop elements if
        /// there is no space available in the buffer.
        /// 
        /// Acknowledgement mechanism is available.
        /// <see cref="ISourceQueue{T}.OfferAsync">ISourceQueueWithComplete&lt;T&gt;.OfferAsync</see> returns <see cref="Task"/>
        /// which completes with <see cref="QueueOfferResult.Enqueued"/> if element was added to buffer or sent downstream.
        /// It completes with <see cref="QueueOfferResult.Dropped"/> if element was dropped.
        /// Can also complete with <see cref="QueueOfferResult.Failure"/> - when stream failed
        /// or <see cref="QueueOfferResult.QueueClosed"/> when downstream is completed.
        /// 
        /// The strategy <see cref="OverflowStrategy.Backpressure"/> will not complete <see cref="ISourceQueue{T}.OfferAsync">ISourceQueueWithComplete&lt;T&gt;.OfferAsync</see> when buffer is full.
        /// 
        /// The buffer can be disabled by using <paramref name="bufferSize"/> of 0 and then received messages will wait
        /// for downstream demand unless there is another message waiting for downstream demand, in that case
        /// offer result will be completed according to the <paramref name="overflowStrategy"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="bufferSize">The size of the buffer in element count</param>
        /// <param name="overflowStrategy">Strategy that is used when incoming elements cannot fit inside the buffer</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="bufferSize"/> is less than zero.
        /// </exception>
        /// <returns>TBD</returns>
        public static Source<T, ISourceQueueWithComplete<T>> Queue<T>(int bufferSize, OverflowStrategy overflowStrategy)
        {
            if (bufferSize < 0) throw new ArgumentException("Buffer size must be greater than or equal 0", nameof(bufferSize));

            return FromGraph(new QueueSource<T>(bufferSize, overflowStrategy).WithAttributes(DefaultAttributes.QueueSource));
        }

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> from some resource which can be opened, read and closed.
        /// Interaction with resource happens in a blocking way.
        /// <para>
        /// Example:
        /// {{{
        /// Source.unfoldResource(
        ///   () => new BufferedReader(new FileReader("...")),
        ///   reader => Option(reader.readLine()),
        ///   reader => reader.close())
        /// }}}
        /// </para>
        /// <para>
        /// You can use the supervision strategy to handle exceptions for <paramref name="read"/> function. All exceptions thrown by <paramref name="create"/>
        /// or <paramref name="close"/> will fail the stream.
        /// </para>
        /// <para>
        /// <see cref="Supervision.Directive.Restart"/> supervision strategy will close and create blocking IO again. Default strategy is <see cref="Supervision.Directive.Stop"/> which means
        /// that stream will be terminated on error in `read` function by default.
        /// </para>
        /// <para>
        /// You can configure the default dispatcher for this Source by changing the `akka.stream.blocking-io-dispatcher` or
        /// set it for a given Source by using <see cref="ActorAttributes.CreateDispatcher"/>.
        /// </para>
        /// <para>
        /// Adheres to the <see cref="ActorAttributes.SupervisionStrategy"/> attribute.
        /// </para>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TSource">TBD</typeparam>
        /// <param name="create">function that is called on stream start and creates/opens resource.</param>
        /// <param name="read">function that reads data from opened resource. It is called each time backpressure signal
        /// is received. Stream calls close and completes when <paramref name="read"/> returns <see cref="Option{T}.None"/>.</param>
        /// <param name="close">TBD</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> UnfoldResource<T, TSource>(Func<TSource> create,
            Func<TSource, Option<T>> read, Action<TSource> close)
        {
            return FromGraph(new UnfoldResourceSource<T, TSource>(create, read, close));
        }

        /// <summary>
        /// <para>
        /// Start a new <see cref="Source{TOut,TMat}"/> from some resource which can be opened, read and closed.
        /// It's similar to <see cref="UnfoldResource{T,TSource}"/> but takes functions that return <see cref="Task"/>s instead of plain values.
        /// </para>
        /// <para>
        /// You can use the supervision strategy to handle exceptions for <paramref name="read"/> function or failures of produced <see cref="Task"/>s.
        /// All exceptions thrown by <paramref name="create"/> or <paramref name="close"/> as well as fails of returned futures will fail the stream.
        /// </para>
        /// <para>
        /// <see cref="Supervision.Directive.Restart"/> supervision strategy will close and create resource .Default strategy is <see cref="Supervision.Directive.Stop"/> which means
        /// that stream will be terminated on error in <paramref name="read"/> function (or task) by default.
        /// </para>
        /// <para>
        /// You can configure the default dispatcher for this Source by changing the `akka.stream.blocking-io-dispatcher` or
        /// set it for a given Source by using <see cref="ActorAttributes.CreateDispatcher"/>.
        /// </para>
        /// <para>
        /// Adheres to the <see cref="ActorAttributes.SupervisionStrategy"/> attribute.
        /// </para>
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TSource">TBD</typeparam>
        /// <param name="create">function that is called on stream start and creates/opens resource.</param>
        /// <param name="read">function that reads data from opened resource. It is called each time backpressure signal
        /// is received. Stream calls close and completes when <see cref="Task"/> from read function returns None.</param>
        /// <param name="close">function that closes resource</param>
        /// <returns>TBD</returns>
        public static Source<T, NotUsed> UnfoldResourceAsync<T, TSource>(Func<Task<TSource>> create,
            Func<TSource, Task<Option<T>>> read, Func<TSource, Task> close)
        {
            return FromGraph(new UnfoldResourceSourceAsync<T, TSource>(create, read, close));
        }

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> attached to a .NET event. In case when event will be triggered faster, than a downstream is able 
        /// to consume incoming events, a buffering will occur. It can be configured via optional <paramref name="maxBufferCapacity"/> and 
        /// <paramref name="overflowStrategy"/> parameters.
        /// </summary>
        /// <typeparam name="TDelegate">Delegate type used to attach current source.</typeparam>
        /// <typeparam name="T">Type of the event args produced as source events.</typeparam>
        /// <param name="conversion">A function used to convert provided event handler into a delegate compatible with an underlying .NET event type.</param>
        /// <param name="addHandler">Action used to attach the given event handler to the underlying .NET event.</param>
        /// <param name="removeHandler">Action used to detach the given event handler to the underlying .NET event.</param>
        /// <param name="maxBufferCapacity">Maximum size of the buffer, used in situation when amount of emitted events is higher than current processing capabilities of the downstream.</param>
        /// <param name="overflowStrategy">Overflow strategy used, when buffer (size specified by <paramref name="maxBufferCapacity"/>) has been overflown.</param>
        /// <returns></returns>
        public static Source<T, NotUsed> FromEvent<TDelegate, T>(
            Func<Action<T>, TDelegate> conversion,
            Action<TDelegate> addHandler,
            Action<TDelegate> removeHandler,
            int maxBufferCapacity = 128,
            OverflowStrategy overflowStrategy = OverflowStrategy.DropHead)
        {
            var wrapper = new EventWrapper<TDelegate, T>(addHandler, removeHandler, conversion);
            return FromGraph(new ObservableSourceStage<T>(wrapper, maxBufferCapacity, overflowStrategy));
        }

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> attached to a .NET event. In case when event will be triggered faster, than a downstream is able 
        /// to consume incoming events, a buffering will occur. It can be configured via optional <paramref name="maxBufferCapacity"/> and 
        /// <paramref name="overflowStrategy"/> parameters.
        /// </summary>
        /// <typeparam name="T">Type of the event args produced as source events.</typeparam>
        /// <param name="addHandler">Action used to attach the given event handler to the underlying .NET event.</param>
        /// <param name="removeHandler">Action used to detach the given event handler to the underlying .NET event.</param>
        /// <param name="maxBufferCapacity">Maximum size of the buffer, used in situation when amount of emitted events is higher than current processing capabilities of the downstream.</param>
        /// <param name="overflowStrategy">Overflow strategy used, when buffer (size specified by <paramref name="maxBufferCapacity"/>) has been overflown.</param>
        /// <returns></returns>
        public static Source<T, NotUsed> FromEvent<T>(
            Action<EventHandler<T>> addHandler,
            Action<EventHandler<T>> removeHandler,
            int maxBufferCapacity = 128,
            OverflowStrategy overflowStrategy = OverflowStrategy.DropHead)
        {
            Func<Action<T>, EventHandler<T>> conversion = onEvent => (sender, e) => onEvent(e);
            var wrapper = new EventWrapper<EventHandler<T>, T>(addHandler, removeHandler, conversion);
            return FromGraph(new ObservableSourceStage<T>(wrapper, maxBufferCapacity, overflowStrategy));
        }

        /// <summary>
        /// Start a new <see cref="Source{TOut,TMat}"/> attached to an existing <see cref="IObservable{T}"/>. In case when upstream (an <paramref name="observable"/>)
        /// is producing events in a faster pace, than downstream is able to consume them, a buffering will occur. It can be configured via optional 
        /// <paramref name="maxBufferCapacity"/> and <paramref name="overflowStrategy"/> parameters.
        /// </summary>
        /// <typeparam name="T">Type of the event args produced as source events.</typeparam>
        /// <param name="observable">An <see cref="IObservable{T}"/> to which current source will be subscribed.</param>
        /// <param name="maxBufferCapacity">Maximum size of the buffer, used in situation when amount of emitted events is higher than current processing capabilities of the downstream.</param>
        /// <param name="overflowStrategy">Overflow strategy used, when buffer (size specified by <paramref name="maxBufferCapacity"/>) has been overflown.</param>
        /// <returns></returns>
        public static Source<T, NotUsed> FromObservable<T>(
            IObservable<T> observable,
            int maxBufferCapacity = 128,
            OverflowStrategy overflowStrategy = OverflowStrategy.DropHead)
        {
            return FromGraph(new ObservableSourceStage<T>(observable, maxBufferCapacity, overflowStrategy));
        }
    }
}
