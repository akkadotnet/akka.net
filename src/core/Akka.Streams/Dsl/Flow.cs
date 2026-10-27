//-----------------------------------------------------------------------
// <copyright file="Flow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Util;
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
        /// Creates a flow around the specified graph module.
        /// </summary>
        /// <param name="module">The module that contains the flow shape and materialized value.</param>
        internal Flow(IModule module)
        {
            Module = module;
        }

        /// <summary>
        /// The input and output ports of this flow.
        /// </summary>
        public FlowShape<TIn, TOut> Shape => (FlowShape<TIn, TOut>)Module.Shape;

        /// <summary>
        /// The graph module containing this flow's processing stages and materialized value.
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
        /// <typeparam name="T2">The element type emitted by the appended flow.</typeparam>
        /// <typeparam name="TMat2">The appended flow's materialized value type.</typeparam>
        /// <param name="flow">The flow connected to this flow's output.</param>
        /// <returns>A flow that applies both processing steps and keeps this flow's materialized value.</returns>
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
        /// <typeparam name="TOut2">The element type emitted by the appended flow.</typeparam>
        /// <typeparam name="TMat2">The appended flow's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The materialized value type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="flow">The flow connected to this flow's output.</param>
        /// <param name="combine">Combines the materialized values of this flow and the appended flow.</param>
        /// <returns>A flow that applies both processing steps and materializes to the combined value.</returns>
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
        /// <param name="attributes">The attributes to apply to the contained stages.</param>
        /// <returns>This flow with the supplied attributes.</returns>
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
        /// <param name="attributes">The attributes to add to those already attached to this flow.</param>
        /// <returns>This flow with the added attributes.</returns>
        public Flow<TIn, TOut, TMat> AddAttributes(Attributes attributes)
            => WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// Add a name attribute to this Flow.
        /// </summary>
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.Named(string name) => Named(name);

        /// <summary>
        /// Add a name attribute to this Flow.
        /// </summary>
        /// <param name="name">The flow name.</param>
        /// <returns>This flow with the name attribute.</returns>
        public Flow<TIn, TOut, TMat> Named(string name) => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        IGraph<FlowShape<TIn, TOut>, TMat> IGraph<FlowShape<TIn, TOut>, TMat>.Async() => Async();

        /// <summary>
        /// Put an asynchronous boundary around this Source.
        /// </summary>
        /// <returns>This flow with an asynchronous boundary.</returns>
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
        /// <typeparam name="TMat2">The new materialized value type.</typeparam>
        /// <param name="mapFunc">Maps this flow's materialized value to the replacement value.</param>
        /// <returns>A flow with the same stages and the mapped materialized value.</returns>
        public Flow<TIn, TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc)
            => new(Module.TransformMaterializedValue(mapFunc));

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
        /// <typeparam name="TMat2">The sink's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The result type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="sink">The sink connected to this flow's output.</param>
        /// <param name="combine">Combines this flow's and the sink's materialized values.</param>
        /// <returns>A sink that accepts this flow's input and materializes to the combined value.</returns>
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
        /// <typeparam name="TMat2">The appended source's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The result type returned by <paramref name="materializedFunction"/>.</typeparam>
        /// <param name="that">The source concatenated after this flow's output.</param>
        /// <param name="materializedFunction">Combines this flow's and the source's materialized values.</param>
        /// <returns>A flow that emits the appended source after this flow completes successfully.</returns>
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
        /// <typeparam name="TMat2">The connected flow's materialized value type.</typeparam>
        /// <param name="flow">The flow connected back to this flow's input.</param>
        /// <returns>A runnable graph that runs the cycle and keeps this flow's materialized value.</returns>
        public IRunnableGraph<TMat> Join<TMat2>(IGraph<FlowShape<TOut, TIn>, TMat2> flow)
            => JoinMaterialized(flow, Keep.Left);

        /// <summary>
        /// Connects this flow to a bidirectional graph, using its two ports to form the resulting flow.
        /// </summary>
        /// <typeparam name="TIn2">The input type accepted by the resulting flow.</typeparam>
        /// <typeparam name="TOut2">The output type emitted by the resulting flow.</typeparam>
        /// <typeparam name="TMat2">The bidirectional graph's materialized value type.</typeparam>
        /// <param name="bidi">The bidirectional graph connected to this flow.</param>
        /// <returns>A flow that keeps this flow's materialized value.</returns>
        public Flow<TIn2, TOut2, TMat> Join<TIn2, TOut2, TMat2>(IGraph<BidiShape<TOut, TOut2, TIn2, TIn>, TMat2> bidi)
            => JoinMaterialized(bidi, Keep.Left);

        /// <summary>
        /// Connects this flow to a bidirectional graph and combines their materialized values.
        /// </summary>
        /// <typeparam name="TIn2">The input type accepted by the resulting flow.</typeparam>
        /// <typeparam name="TOut2">The output type emitted by the resulting flow.</typeparam>
        /// <typeparam name="TMat2">The bidirectional graph's materialized value type.</typeparam>
        /// <typeparam name="TMatRes">The result type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="bidi">The bidirectional graph connected to this flow.</param>
        /// <param name="combine">Combines this flow's and the graph's materialized values.</param>
        /// <returns>A flow that materializes to the combined value.</returns>
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
        /// <typeparam name="TMat2">The connected flow's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The result type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="flow">The flow connected back to this flow's input.</param>
        /// <param name="combine">Combines the two flows' materialized values.</param>
        /// <returns>A runnable graph that runs the cycle and materializes to the combined value.</returns>
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
        /// <typeparam name="TMat1">The source's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The sink's materialized value type.</typeparam>
        /// <param name="source">The source connected to this flow's input.</param>
        /// <param name="sink">The sink connected to this flow's output.</param>
        /// <param name="materializer">The materializer used to run the graph.</param>
        /// <returns>A tuple containing the source and sink materialized values.</returns>
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
        /// Formats the flow's shape and module for diagnostics.
        /// </summary>
        /// <returns>A string containing this flow's shape and module.</returns>
        public override string ToString() => $"Flow({Shape}, {Module})";
    }

    /// <summary>
    /// A <see cref="Flow"/> is a set of stream processing steps that has one open input and one open output.
    /// </summary>
    public static class Flow
    {
        /// <summary>
        /// Creates identity flows and flows from processing graphs.
        /// </summary>
        /// <typeparam name="T">The element type passed through the flow.</typeparam>
        /// <returns>An identity flow with materialized value <see cref="NotUsed"/>.</returns>
        public static Flow<T, T, NotUsed> Identity<T>() => new(GraphStages.Identity<T>().Module);

        /// <summary>
        /// Creates an identity flow with the specified materialized value type.
        /// </summary>
        /// <typeparam name="T">The element type passed through the flow.</typeparam>
        /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
        /// <returns>An identity flow typed with the specified materialized value type.</returns>
        public static Flow<T, T, TMat> Identity<T, TMat>() => new(GraphStages.Identity<T>().Module);

        /// <summary>
        /// Creates flow from the Reactive Streams <see cref="IProcessor{T1,T2}"/>.
        /// </summary>
        /// <typeparam name="TIn">The processor's input element type.</typeparam>
        /// <typeparam name="TOut">The processor's output element type.</typeparam>
        /// <param name="factory">Creates a processor for each materialization.</param>
        /// <returns>A flow backed by a processor factory, with materialized value <see cref="NotUsed"/>.</returns>
        public static Flow<TIn, TOut, NotUsed> FromProcessor<TIn, TOut>(Func<IProcessor<TIn, TOut>> factory)
            => FromProcessorMaterialized(() => (factory(), NotUsed.Instance));

        /// <summary>
        /// Creates a Flow from a Reactive Streams <see cref="IProcessor{T1,T2}"/> and returns a materialized value.
        /// </summary>
        /// <typeparam name="TIn">The processor's input element type.</typeparam>
        /// <typeparam name="TOut">The processor's output element type.</typeparam>
        /// <typeparam name="TMat">The type of value returned by the processor factory.</typeparam>
        /// <param name="factory">Creates a processor and its materialized value for each materialization.</param>
        /// <returns>A flow backed by the processor factory.</returns>
        public static Flow<TIn, TOut, TMat> FromProcessorMaterialized<TIn, TOut, TMat>(Func<(IProcessor<TIn, TOut>, TMat)> factory) 
            => new(new ProcessorModule<TIn, TOut, TMat>(factory));

        /// <summary>
        /// Helper to create a <see cref="Flow{TIn,TOut,TMat}"/> without a <see cref="Source"/> or <see cref="Sink"/>.
        /// </summary>
        /// <typeparam name="T">The element type passed through the flow.</typeparam>
        /// <returns>An identity flow with materialized value <see cref="NotUsed"/>.</returns>
        public static Flow<T, T, NotUsed> Create<T>() => Identity<T>();

        /// <summary>
        /// Helper to create a <see cref="Flow{TIn,TOut,TMat}"/> without a <see cref="Source"/> or <see cref="Sink"/>.
        /// </summary>
        /// <typeparam name="T">The element type passed through the flow.</typeparam>
        /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
        /// <returns>An identity flow typed with the specified materialized value type.</returns>
        public static Flow<T, T, TMat> Create<T, TMat>() => Identity<T, TMat>();

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> which will use the given function to transform its inputs to outputs. It is equivalent
        /// to <see cref="Implementation.Fusing.Select{TIn,TOut}"/>
        /// </summary>
        /// <typeparam name="TIn">The element type accepted by the flow.</typeparam>
        /// <typeparam name="TOut">The element type produced by the mapping function.</typeparam>
        /// <param name="function">Maps each input element to an output element.</param>
        /// <returns>A flow that applies <paramref name="function"/> and materializes to <see cref="NotUsed"/>.</returns>
        public static Flow<TIn, TOut, NotUsed> FromFunction<TIn, TOut>(Func<TIn, TOut> function)
            => Create<TIn>().Select(function);

        /// <summary>
        /// A graph with the shape of a flow logically is a flow, this method makes it so also in type.
        /// </summary>
        /// <typeparam name="TIn">The graph's input element type.</typeparam>
        /// <typeparam name="TOut">The graph's output element type.</typeparam>
        /// <typeparam name="TMat">The graph's materialized value type.</typeparam>
        /// <param name="graph">The graph with a flow shape to wrap.</param>
        /// <returns>The graph as a <see cref="Flow{TIn,TOut,TMat}"/>.</returns>
        public static Flow<TIn, TOut, TMat> FromGraph<TIn, TOut, TMat>(IGraph<FlowShape<TIn, TOut>, TMat> graph)
            => graph as Flow<TIn, TOut, TMat> ?? new Flow<TIn, TOut, TMat>(graph.Module);

        /// <summary>
        /// Defers the creation of a <see cref="Flow"/> until materialization. The <paramref name="factory"/> 
        /// function exposes <see cref="ActorMaterializer"/> which is going to be used during materialization and
        /// <see cref="Attributes"/> of the <see cref="Flow"/> returned by this method.
        /// </summary>
        /// <typeparam name="TIn">The flow's input element type.</typeparam>
        /// <typeparam name="TOut">The flow's output element type.</typeparam>
        /// <typeparam name="TMat">The flow factory's materialized value type.</typeparam>
        /// <param name="factory">Creates the flow when the stream is materialized.</param>
        /// <returns>A flow that materializes to a task containing the factory-created flow's materialized value.</returns>
        public static Flow<TIn, TOut, Task<TMat>> Setup<TIn, TOut, TMat>(Func<ActorMaterializer, Attributes, Flow<TIn, TOut, TMat>> factory)
            => FromGraph(new SetupFlowStage<TIn, TOut, TMat>(factory));

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> from a <see cref="Sink{TIn,TMat}"/> and a <see cref="Source{TOut,TMat}"/> where the flow's input
        /// will be sent to the sink and the flow's output will come from the source.
        /// </summary>
        /// <typeparam name="TIn">The sink's input type and the resulting flow's input type.</typeparam>
        /// <typeparam name="TOut">The source's output type and the resulting flow's output type.</typeparam>
        /// <typeparam name="TMat">The materialized value type of the sink and source graphs.</typeparam>
        /// <param name="sink">The graph that consumes the resulting flow's input.</param>
        /// <param name="source">The graph that produces the resulting flow's output.</param>
        /// <returns>A flow that discards the sink's and source's materialized values.</returns>
        public static Flow<TIn, TOut, NotUsed> FromSinkAndSource<TIn, TOut, TMat>(IGraph<SinkShape<TIn>, TMat> sink, IGraph<SourceShape<TOut>, TMat> source) 
            => FromSinkAndSource(sink, source, Keep.None);

        /// <summary>
        /// Creates a <see cref="Flow{TIn,TOut,TMat}"/> from a <see cref="Sink{TIn,TMat}"/> and a <see cref="Source{TOut,TMat}"/> where the flow's input
        /// will be sent to the sink and the flow's output will come from the source.
        /// 
        /// The <paramref name="combine"/> function is used to compose the materialized values of the <see cref="Sink{TIn,TMat}"/> and <see cref="Source{TOut,TMat}"/>
        /// into the materialized value of the resulting <see cref="Flow{TIn,TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="TIn">The sink's input type and the resulting flow's input type.</typeparam>
        /// <typeparam name="TOut">The source's output type and the resulting flow's output type.</typeparam>
        /// <typeparam name="TMat1">The sink's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The source's materialized value type.</typeparam>
        /// <typeparam name="TMat">The type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="sink">The graph that consumes the resulting flow's input.</param>
        /// <param name="source">The graph that produces the resulting flow's output.</param>
        /// <param name="combine">Combines the sink's and source's materialized values, in that order.</param>
        /// <returns>A flow composed from the sink and source.</returns>
        public static Flow<TIn, TOut, TMat> FromSinkAndSource<TIn, TOut, TMat1, TMat2, TMat>(IGraph<SinkShape<TIn>, TMat1> sink, IGraph<SourceShape<TOut>, TMat2> source, Func<TMat1, TMat2, TMat> combine) 
            => FromGraph(GraphDsl.Create(sink, source, combine, (_, @in, @out) => new FlowShape<TIn, TOut>(@in.Inlet, @out.Outlet)));

        /// <summary>
        /// Creates a real <see cref="Flow"/> upon receiving the first element. If upstream completes normally or downstream cancels before an element arrives,
        /// the internal <see cref="Flow"/> is not created and the materialized task completes with no value. An upstream failure before creation faults the task.
        /// <para>
        /// The materialized value is a task that completes with the created flow's materialized value after successful materialization,
        /// with <see cref="Option{T}.None"/> if upstream completes normally or downstream cancels before the first element, or with a failure if flow creation or upstream processing fails.
        /// </para>
        /// <para>Emits when the internal flow is successfully created and it emits</para>
        /// <para>Cancels when downstream cancels</para>
        /// </summary>
        /// <typeparam name="TIn">The flow's input element type.</typeparam>
        /// <typeparam name="TOut">The flow's output element type.</typeparam>
        /// <typeparam name="TMat">The created flow's materialized value type.</typeparam>
        /// <param name="flowFactory">Asynchronously creates the flow after the first input arrives.</param>
        /// <returns>A flow whose materialized task contains the created flow's materialized value, or no value if upstream completes normally or downstream cancels before creation; a failure faults the task.</returns>
        public static Flow<TIn, TOut, Task<Option<TMat>>> LazyInitAsync<TIn, TOut, TMat>(Func<Task<Flow<TIn, TOut, TMat>>> flowFactory) =>
            FromGraph(new LazyFlow<TIn, TOut, TMat>(_ => flowFactory()));
    }

    /// <summary>
    /// Adapts a materialized stream subscriber and publisher pair to the Reactive Streams processor interface.
    /// </summary>
    /// <typeparam name="TIn">The input element type accepted by the processor.</typeparam>
    /// <typeparam name="TOut">The output element type emitted by the processor.</typeparam>
    internal sealed class FlowProcessor<TIn, TOut> : IProcessor<TIn, TOut>
    {
        private readonly ISubscriber<TIn> _subscriber;
        private readonly IPublisher<TOut> _publisher;

        /// <summary>
        /// Creates a processor adapter from its subscriber and publisher sides.
        /// </summary>
        /// <param name="subscriber">The subscriber receiving upstream signals.</param>
        /// <param name="publisher">The publisher to which downstream subscribers attach.</param>
        public FlowProcessor(ISubscriber<TIn> subscriber, IPublisher<TOut> publisher)
        {
            _subscriber = subscriber;
            _publisher = publisher;
        }

        /// <summary>
        /// Forwards the upstream subscription to the processor's subscriber side.
        /// </summary>
        /// <param name="subscription">The subscription provided by the upstream publisher.</param>
        public void OnSubscribe(ISubscription subscription) => _subscriber.OnSubscribe(subscription);

        /// <summary>
        /// Forwards the terminal failure to the processor's subscriber side.
        /// </summary>
        /// <param name="cause">The failure signaled by the upstream publisher.</param>
        public void OnError(Exception cause) => _subscriber.OnError(cause);

        /// <summary>
        /// Forwards successful completion to the processor's subscriber side.
        /// </summary>
        public void OnComplete() => _subscriber.OnComplete();

        /// <summary>
        /// Forwards an element to the processor's subscriber side.
        /// </summary>
        /// <param name="element">The element delivered by the upstream publisher.</param>
        public void OnNext(TIn element) => _subscriber.OnNext(element);

        /// <summary>
        /// Attaches a downstream subscriber to the processor's publisher side.
        /// </summary>
        /// <param name="subscriber">The subscriber that receives processed elements.</param>
        public void Subscribe(ISubscriber<TOut> subscriber) => _publisher.Subscribe(subscriber);
    }

    /// <summary>
    /// Operations offered by Sources and Flows with a free output side: the DSL flows left-to-right only.
    /// </summary>
    /// <typeparam name="TOut">The element type emitted by the flow.</typeparam>
    /// <typeparam name="TMat">The flow's materialized value type.</typeparam>
    public interface IFlow<TOut, out TMat>
    {
        /// <summary>
        /// Transform this <see cref="IFlow{TOut,TMat}"/> by appending the given processing steps.
        /// The materialized value of the combined <see cref="IFlow{TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other flow’s value), use
        /// <see cref="ViaMaterialized{T2,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="T">The output element type of the appended flow.</typeparam>
        /// <typeparam name="TMat2">The appended flow's materialized value type.</typeparam>
        /// <param name="flow">The flow connected to this flow's output.</param>
        /// <returns>A flow that applies both steps and keeps this flow's materialized value.</returns>
        IFlow<T, TMat> Via<T, TMat2>(IGraph<FlowShape<TOut, T>, TMat2> flow);

        #region FlowOpsMat methods

        /// <summary>
        /// Transform this <see cref="IFlow{T,TMat}"/> by appending the given processing steps.
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting Flow.
        /// </summary>
        /// <typeparam name="T2">The output element type of the appended flow.</typeparam>
        /// <typeparam name="TMat2">The appended flow's materialized value type.</typeparam>
        /// <typeparam name="TMat3">The type returned by <paramref name="combine"/>.</typeparam>
        /// <param name="flow">The flow connected to this flow's output.</param>
        /// <param name="combine">Combines the two flows' materialized values.</param>
        /// <returns>A flow that materializes to the combined value.</returns>
        IFlow<T2, TMat3> ViaMaterialized<T2, TMat2, TMat3>(IGraph<FlowShape<TOut, T2>, TMat2> flow, Func<TMat, TMat2, TMat3> combine);

        /// <summary>
        /// Transform the materialized value of this Flow, leaving all other properties as they were.
        /// </summary>
        /// <typeparam name="TMat2">The new materialized value type.</typeparam>
        /// <param name="mapFunc">Maps this flow's materialized value to the replacement value.</param>
        /// <returns>A flow with the same stages and the mapped materialized value.</returns>
        IFlow<TOut, TMat2> MapMaterializedValue<TMat2>(Func<TMat, TMat2> mapFunc);

        #endregion
    }
}
