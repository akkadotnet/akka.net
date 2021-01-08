//-----------------------------------------------------------------------
// <copyright file="Flow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using Akka.Actor;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Reactive.Streams;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// A <see cref="Flow{TIn,TOut,TMat}"/> is a set of stream processing steps that has one open input and one open output.
    /// </summary>
    /// <typeparam name="TIn">Type of the flow input.</typeparam>
    /// <typeparam name="TOut">Type of the flow output.</typeparam>
    /// <typeparam name="TMat">Type of value, flow graph may materialize to.</typeparam>
    public sealed class Flow<TIn, TOut, TMat> : IFlow<TOut, TMat>, IGraph<FlowShape<TIn, TOut>, TMat>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        internal Flow(IModule module)
        {
            Module = module;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public FlowShape<TIn, TOut> Shape => (FlowShape<TIn, TOut>)Module.Shape;

        /// <summary>
        /// TBD
        /// </summary>
        public IModule Module { get; }

        private bool IsIdentity => Module == Identity<TIn>.Instance.Module;

        /// <summary>
        /// Transform this <see cref="Flow{TIn,TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="Flow{TIn,TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        IFlow<T2, TMat> IFlow<TOut, TMat>.Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow) => Via(flow);

        /// <summary>
        /// Transform this <see cref="Flow{TIn,TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="Flow{TIn,TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, T2, TMat> Via<T2, TMat2>(IGraph<FlowShape<TOut, T2>, TMat2> flow)
            => ViaMaterialized(flow, Keep.Left);

        /// <summary>
        /// Transform this <see cref="IFlow{T,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        IFlow<TOut2, TMat3> IFlow<TOut, TMat>.ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
            => ViaMaterialized(flow, combine);

        /// <summary>
        /// Transform this <see cref="Flow{TIn,TOut,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut2, TMat3> ViaMaterialized<TOut2, TMat2, TMat3>(IGraph<FlowShape<TOut, TOut2>, TMat2> flow,
            Func<TMat, TMat2, TMat3> combine)
        {
            if (IsIdentity)
            {
                var m = flow.Module;
                StreamLayout.IMaterializedValueNode materializedValueNode;

                if (Keep.IsLeft(combine))
                {
                    if (IgnorableMaterializedValueComposites.Apply(m))
                        materializedValueNode = StreamLayout.Ignore.Instance;
                    else
                        materializedValueNode = new StreamLayout.Transform(_ => NotUsed.Instance,
                            new StreamLayout.Atomic(m));
                }
                else
                    materializedValueNode = new StreamLayout.Combine((o, o1) => combine((TMat)o, (TMat2)o1),
                        StreamLayout.Ignore.Instance, new StreamLayout.Atomic(m));

                return
                    new Flow<TIn, TOut2, TMat3>(new CompositeModule(ImmutableArray<IModule>.Empty.Add(m), m.Shape,
                        m.Downstreams, m.Upstreams, materializedValueNode, m.Attributes));
            }

            var copy = flow.Module.CarbonCopy();
            return new Flow<TIn, TOut2, TMat3>(Module
                .Fuse(copy, Shape.Outlet, copy.Shape.Inlets.First(), combine)
                .ReplaceShape(new FlowShape<TIn, TOut2>(Shape.Inlet, (Outlet<TOut2>)copy.Shape.Outlets.First())));
        }

        /// <summary>
        /// Change the attributes of this <see cref="Flow{TIn,TOut,TMat}"/> to the given ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.WithAttributes(Attributes attributes)
            => WithAttributes(attributes);

        /// <summary>
        /// Change the attributes of this <see cref="Flow{TIn,TOut,TMat}"/> to the given ones. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut, TMat> WithAttributes(Attributes attributes)
            => Module is EmptyModule
                ? this
                : new Flow<TIn, TOut, TMat>(Module.WithAttributes(attributes));

        /// <summary>
        /// Add the given attributes to this <see cref="IGraph{TShape}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.AddAttributes(Attributes attributes)
            => AddAttributes(attributes);

        /// <summary>
        /// Add the given attributes to this <see cref="Flow{TIn,TOut,TMat}"/>.
        /// Further calls to <see cref="WithAttributes"/>
        /// will not remove these attributes. Note that this
        /// operation has no effect on an empty Flow (because the attributes apply
        /// only to the contained processing stages).
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut, TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// Add a name attribute to this Flow.
        /// </summary>
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.Named(string name) => Named(name);

        /// <summary>
        /// Add a name attribute to this Flow.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.Async() => Async();

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut, TMat> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));

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
        public Flow<TIn, TOut2, TMat> Ask<TOut2>(IActorRef actorRef, TimeSpan timeout, int parallelism = 2)
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
        /// Transform the materialized value of this Flow, leaving all other properties as they were.
        /// </summary>
        IFlow<TOut, TMat2> IFlow<TOut, TMat>.MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
            => MapMaterializedValue(mapFunc);

        /// <summary>
        /// Transform the materialized value of this Flow, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="mapFunc">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
            => new Flow<TIn, TOut, TMat2>(Module.TransformMaterializedValue(mapFunc));

        /// <summary>
        /// Connect this <see cref="Flow{TIn,TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>, concatenating the processing steps of both.
        /// The materialized value of the combined <see cref="Sink{TIn,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the given Sink’s value), use
        /// <see cref="ToMaterialized{TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        public Sink<TIn, TMat> To<TMat2>(IGraph<SinkShape<TOut>, TMat2> sink) => ToMaterialized(sink, Keep.Left);

        /// <summary>
        /// Connect this <see cref="Flow{TIn,TOut,TMat}"/> to a <see cref="Sink{TIn,TMat}"/>, concatenating the processing steps of both.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// Sink into the materialized value of the resulting Sink.
        /// 
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public Sink<TIn, TMat3> ToMaterialized<TMat2, TMat3>(IGraph<SinkShape<TOut>, TMat2> sink, Func<TMat, TMat2, TMat3> combine)
        {
            if (IsIdentity)
            {
                return Sink.FromGraph(sink as IGraph<SinkShape<TIn>, TMat2>)
                    .MapMaterializedValue(mat2 => combine(default(TMat), mat2));
            }

            var copy = sink.Module.CarbonCopy();
            return new Sink<TIn, TMat3>(Module
                .Fuse(copy, Shape.Outlet, copy.Shape.Inlets.First(), combine)
                .ReplaceShape(new SinkShape<TIn>(Shape.Inlet)));
        }

        /// <summary>
        /// Concatenate the given <seealso cref="Source{TOut,TMat}"/> to this <seealso cref="Flow{TIn,TOut,TMat}"/>, meaning that once this
        /// Flow’s input is exhausted and all result elements have been generated,
        /// the Source’s elements will be produced.
        ///
        /// Note that the <seealso cref="Source{TOut,TMat}"/> is materialized together with this Flow and just kept
        /// from producing elements by asserting back-pressure until its time comes.
        ///
        /// If this <seealso cref="Flow{TIn,TOut,TMat}"/> gets upstream error - no elements from the given <seealso cref="Source{TOut,TMat}"/> will be pulled.
        ///
        /// @see <seealso cref="Concat{TIn,TOut}"/>.
        ///
        /// It is recommended to use the internally optimized <see cref="Keep.Left{TLeft,TRight}"/> and <see cref="Keep.Right{TLeft,TRight}"/> combiners
        /// where appropriate instead of manually writing functions that pass through one of the values.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="that">TBD</param>
        /// <param name="materializedFunction">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn, TOut, TMat3> ConcatMaterialized<TMat2, TMat3>(IGraph<SourceShape<TOut>, TMat2> that,
            Func<TMat, TMat2, TMat3> materializedFunction)
            => ViaMaterialized(InternalFlowOperations.ConcatGraph(that), materializedFunction);

        /// <summary>
        /// Join this <see cref="Flow{TIn,TOut,TMat}"/> to another <see cref="Flow{TOut,TIn,TMat2}"/>, by cross connecting the inputs and outputs,
        /// creating a <see cref="IRunnableGraph{TMat}"/>.
        /// The materialized value of the combined <see cref="Flow{TIn,TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other Flow’s value), use
        /// <see cref="JoinMaterialized{TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat> Join<TMat2>(IGraph<FlowShape<TOut, TIn>, TMat2> flow)
            => JoinMaterialized(flow, Keep.Left);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="bidi">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn2, TOut2, TMat> Join<TIn2, TOut2, TMat2>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi)
            => JoinMaterialized(bidi, Keep.Left);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMatRes">TBD</typeparam>
        /// <param name="bidi">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn2, TOut2, TMatRes> JoinMaterialized<TIn2, TOut2, TMat2, TMatRes>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi, Func<TMat, TMat2, TMatRes> combine)
        {
            var copy = bidi.Module.CarbonCopy();
            var ins = copy.Shape.Inlets.ToArray();
            var outs = copy.Shape.Outlets.ToArray();

            return new Flow<TIn2, TOut2, TMatRes>(Module.Compose(copy, combine)
                .Wire(Shape.Outlet, ins[0])
                .Wire(outs[1], Shape.Inlet)
                .ReplaceShape(new FlowShape<TIn2, TOut2>(Inlet.Create<TIn2>(ins[1]), Outlet.Create<TOut2>(outs[0]))));
        }

        /// <summary>
        /// Join this <see cref="Flow{TIn,TOut,TMat}"/> to another <see cref="Flow{TIn,TOut,TMat}"/>, by cross connecting the inputs and outputs, creating a <see cref="IRunnableGraph{TMat}"/>
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// Flow into the materialized value of the resulting Flow.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public IRunnableGraph<TMat3> JoinMaterialized<TMat2, TMat3>(IGraph<FlowShape<TOut, TIn>, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = flow.Module.CarbonCopy();
            return new RunnableGraph<TMat3>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlet, copy.Shape.Inlets.First())
                .Wire(copy.Shape.Outlets.First(), Shape.Inlet));
        }

        /// <summary>
        /// Connect the <see cref="Source{TOut,TMat1}"/> to this <see cref="Flow{TIn,TOut,TMat}"/> and then connect it to the <see cref="Sink{TIn,TMat2}"/> and run it. 
        /// The returned tuple contains the materialized values of the <paramref name="source"/> and <paramref name="sink"/>, e.g. the <see cref="ISubscriber{T}"/> 
        /// of a <see cref="Source.AsSubscriber{T}"/> and <see cref="IPublisher{T}"/> of a <see cref="Sink.Publisher{TIn}"/>.
        /// </summary>
        /// <typeparam name="TMat1">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="source">TBD</param>
        /// <param name="sink">TBD</param>
        /// <param name="materializer">TBD</param>
        /// <returns>TBD</returns>
        public (TMat1, TMat2) RunWith<TMat1, TMat2>(IGraph<SourceShape<TIn>, TMat1> source, IGraph<SinkShape<TOut>, TMat2> sink, IMaterializer materializer)
            => Source.FromGraph(source).Via(this).ToMaterialized(sink, Keep.Both).Run(materializer);

        /// <summary>
        /// Converts this Flow to a <see cref="IRunnableGraph{TMat}"/> that materializes to a Reactive Streams <see cref="IProcessor{T1,T2}"/>
        /// which implements the operations encapsulated by this Flow. Every materialization results in a new Processor
        /// instance, i.e. the returned <see cref="IRunnableGraph{TMat}"/> is reusable.
        /// </summary>
        /// <returns>A <see cref="IRunnableGraph{TMat}"/> that materializes to a <see cref="IProcessor{T1,T2}"/> when Run() is called on it.</returns>
        public IRunnableGraph<IProcessor<TIn, TOut>> ToProcessor()
            => Source.AsSubscriber<TIn>()
                .Via(this)
                .ToMaterialized(Sink.AsPublisher<TOut>(false), Keep.Both)
                .MapMaterializedValue(t => new FlowProcessor<TIn, TOut>(t.Item1, t.Item2) as IProcessor<TIn, TOut>);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Flow({Shape}, {Module})";
    }

    /// <summary>
    /// A <see cref="Flow"/> is a set of stream processing steps that has one open input and one open output.
    /// </summary>
    public static class Flow
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Flow<T, T, NotUsed> Identity<T>() => new Flow<T, T, NotUsed>(GraphStages.Identity<T>().Module);

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Flow<T, T, TMat> Identity<T, TMat>() => new Flow<T, T, TMat>(GraphStages.Identity<T>().Module);

        /// <summary>
        /// Creates flow from the Reactive Streams <see cref="IProcessor{T1,T2}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="factory">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<TIn, TOut, NotUsed> FromProcessor<TIn, TOut>(Func<IProcessor<TIn, TOut>> factory)
            => FromProcessorMaterialized(() => (factory(), NotUsed.Instance));

        /// <summary>
        /// Creates a Flow from a Reactive Streams <see cref="IProcessor{T1,T2}"/> and returns a materialized value.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="factory">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<TIn, TOut, TMat> FromProcessorMaterialized<TIn, TOut, TMat>(Func<(IProcessor<TIn, TOut>, TMat)> factory) 
            => new Flow<TIn, TOut, TMat>(new ProcessorModule<TIn, TOut, TMat>(factory));

        /// <summary>
        /// Helper to create a <see cref="Flow{TIn,TOut,TMat}"/> without a <see cref="Source"/> or <see cref="Sink"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Flow<T, T, NotUsed> Create<T>() => Identity<T>();

        /// <summary>
        /// Helper to create a <see cref="Flow{TIn,TOut,TMat}"/> without a <see cref="Source"/> or <see cref="Sink"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <returns>TBD</returns>
        public static Flow<T, T, TMat> Create<T, TMat>() => Identity<T, TMat>();

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> which will use the given function to transform its inputs to outputs. It is equivalent
        /// to <see cref="Implementation.Fusing.Select{TIn,TOut}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="function">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<TIn, TOut, NotUsed> FromFunction<TIn, TOut>(Func<TIn, TOut> function)
            => Create<TIn>().Select(function);

        /// <summary>
        /// A graph with the shape of a flow logically is a flow, this method makes it so also in type.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<TIn, TOut, TMat> FromGraph<TIn, TOut, TMat>(IGraph<FlowShape<TIn, TOut>, TMat> graph)
            => graph as Flow<TIn, TOut, TMat> ?? new Flow<TIn, TOut, TMat>(graph.Module);

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> from a <see cref="Sink{TIn,TMat}"/> and a <see cref="Source{TOut,TMat}"/> where the flow's input
        /// will be sent to the sink and the flow's output will come from the source.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <param name="source">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<TIn, TOut, NotUsed> FromSinkAndSource<TIn, TOut, TMat>(IGraph<SinkShape<TIn>, TMat> sink, IGraph<SourceShape<TOut>, TMat> source) 
            => FromSinkAndSource(sink, source, Keep.None);

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> from a <see cref="Sink{TIn,TMat}"/> and a <see cref="Source{TOut,TMat}"/> where the flow's input
        /// will be sent to the sink and the flow's output will come from the source.
        /// 
        /// The <paramref name="combine"/> function is used to compose the materialized values of the <see cref="Sink{TIn,TMat}"/> and <see cref="Source{TOut,TMat}"/>
        /// into the materialized value of the resulting <see cref="Flow{TIn,TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat1">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="sink">TBD</param>
        /// <param name="source">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static Flow<TIn, TOut, TMat> FromSinkAndSource<TIn, TOut, TMat1, TMat2, TMat>(IGraph<SinkShape<TIn>, TMat1> sink, IGraph<SourceShape<TOut>, TMat2> source, Func<TMat1, TMat2, TMat> combine) 
            => FromGraph(GraphDsl.Create(sink, source, combine, (builder, @in, @out) => new FlowShape<TIn, TOut>(@in.Inlet, @out.Outlet)));
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    internal sealed class FlowProcessor<TIn, TOut> : IProcessor<TIn, TOut>
    {
        private readonly ISubscriber<TIn> _subscriber;
        private readonly IPublisher<TOut> _publisher;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <param name="publisher">TBD</param>
        public FlowProcessor(ISubscriber<TIn> subscriber, IPublisher<TOut> publisher)
        {
            _subscriber = subscriber;
            _publisher = publisher;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        public void OnSubscribe(ISubscription subscription) => _subscriber.OnSubscribe(subscription);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public void OnError(Exception cause) => _subscriber.OnError(cause);

        /// <summary>
        /// TBD
        /// </summary>
        public void OnComplete() => _subscriber.OnComplete();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(TIn element) => _subscriber.OnNext(element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void Subscribe(ISubscriber<TOut> subscriber) => _publisher.Subscribe(subscriber);
    }

    /// <summary>
    /// Operations offered by Sources and Flows with a free output side: the DSL flows left-to-right only.
    /// </summary>
    /// <typeparam name="TOut">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    public interface IFlow<TOut, out TMat>
    {
        /// <summary>
        /// Transform this <see cref="IFlow{TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="IFlow{TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        IFlow<T, TMat> Via<T, TMat2>(IGraph<FlowShape<TOut, T>, TMat2> flow);

        #region FlowOpsMat methods

        /// <summary>
        /// Transform this <see cref="IFlow{T,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        /// <typeparam name="T2">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine);

        /// <summary>
        /// Transform the materialized value of this Flow, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="mapFunc">TBD</param>
        /// <returns>TBD</returns>
        IFlow<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc);

        #endregion
    }
}
