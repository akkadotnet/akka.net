//-----------------------------------------------------------------------
// <copyright file="GraphDsl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Helpers for constructing stream graphs by importing stages and connecting their ports.
    /// </summary>
    public static partial class GraphDsl
    {
        /// <summary>
        /// Builds a stream graph incrementally through its typed ports.
        /// </summary>
        /// <typeparam name="T">The graph's materialized value type.</typeparam>
        public sealed class Builder<T>
        {
            #region internal API

            /// <summary>
            /// Creates an empty graph builder.
            /// </summary>
            internal Builder() { }

            private IModule _moduleInProgress = EmptyModule.Instance;

            /// <summary>
            /// Connects an outlet to an inlet in the graph being built.
            /// </summary>
            /// <typeparam name="T1">The element type emitted by the outlet.</typeparam>
            /// <typeparam name="T2">The inlet element type; this method constrains <c>T2 : T1</c>.</typeparam>
            /// <param name="from">The outlet that supplies elements.</param>
            /// <param name="to">The inlet that receives elements.</param>
            internal void AddEdge<T1, T2>(Outlet<T1> from, Inlet<T2> to) where T2 : T1
            {
                _moduleInProgress = _moduleInProgress.Wire(from, to);
            }

            /// <summary>
            /// INTERNAL API. 
            /// This is only used by the materialization-importing apply methods of Source,
            /// Flow, Sink and Graph.
            /// </summary>
            /// <typeparam name="TShape">The shape type of the imported graph.</typeparam>
            /// <typeparam name="TMat">The imported graph's materialized value type.</typeparam>
            /// <typeparam name="TMat2">The transformed materialized value type.</typeparam>
            /// <param name="graph">The graph to copy into this builder.</param>
            /// <param name="transform">Transforms the imported graph's materialized value.</param>
            /// <returns>The imported graph's copied shape, with ports that can be connected in this builder.</returns>
            internal TShape Add<TShape, TMat, TMat2>(IGraph<TShape, TMat> graph, Func<TMat, TMat2> transform) where TShape : Shape
            {
                if (StreamLayout.IsDebug)
#pragma warning disable CS0162 // Unreachable code detected
                    StreamLayout.Validate(graph.Module);
#pragma warning restore CS0162 // Unreachable code detected

                var copy = graph.Module.CarbonCopy();
                _moduleInProgress = _moduleInProgress.Compose<TMat,TMat2,TMat2>(copy.TransformMaterializedValue(transform), Keep.Right);
                return (TShape)graph.Shape.CopyFromPorts(copy.Shape.Inlets, copy.Shape.Outlets);
            }

            /// <summary>
            /// INTERNAL API. 
            /// This is only used by the materialization-importing apply methods of Source,
            /// Flow, Sink and Graph.
            /// </summary>
            /// <typeparam name="TShape">The shape type of the imported graph.</typeparam>
            /// <typeparam name="TMat1">The current builder materialized value type.</typeparam>
            /// <typeparam name="TMat2">The imported graph materialized value type.</typeparam>
            /// <typeparam name="TMat3">The combined materialized value type.</typeparam>
            /// <param name="graph">The graph to copy into this builder.</param>
            /// <param name="combine">Combines the existing builder and imported graph materialized values, in that order.</param>
            /// <returns>The imported graph's copied shape, with ports that can be connected in this builder.</returns>
            internal TShape Add<TShape, TMat1, TMat2, TMat3>(IGraph<TShape> graph, Func<TMat1, TMat2, TMat3> combine) where TShape : Shape
            {
                if (StreamLayout.IsDebug)
#pragma warning disable CS0162 // Unreachable code detected
                    StreamLayout.Validate(graph.Module);
#pragma warning restore CS0162 // Unreachable code detected

                var copy = graph.Module.CarbonCopy();
                _moduleInProgress = _moduleInProgress.Compose(copy, combine);
                return (TShape)graph.Shape.CopyFromPorts(copy.Shape.Inlets, copy.Shape.Outlets);
            }

            #endregion

            /// <summary>
            /// Import a graph into this module, performing a deep copy, discarding its
            /// materialized value and returning the copied Ports that are now to be connected.
            /// </summary>
            /// <typeparam name="TShape">The shape type of the imported graph.</typeparam>
            /// <typeparam name="TMat">The imported graph materialized value type, which is discarded by this overload.</typeparam>
            /// <param name="graph">The graph to copy into this builder.</param>
            /// <returns>The imported graph's copied shape, with ports that can be connected in this builder.</returns>
            public TShape Add<TShape, TMat>(IGraph<TShape, TMat> graph)
                where TShape : Shape
            {
                if (StreamLayout.IsDebug)
#pragma warning disable CS0162 // Unreachable code detected
                    StreamLayout.Validate(graph.Module);
#pragma warning restore CS0162 // Unreachable code detected

                var copy = graph.Module.CarbonCopy();
                _moduleInProgress = _moduleInProgress.Compose<object, TMat, object>(copy, Keep.Left);
                return (TShape)graph.Shape.CopyFromPorts(copy.Shape.Inlets, copy.Shape.Outlets);
            }

            /// <summary>
            /// Returns an <see cref="Outlet{T}"/> that gives access to the materialized value of this graph. Once the graph is materialized
            /// this outlet will emit exactly one element which is the materialized value. It is possible to expose this
            /// outlet as an externally accessible outlet of a <see cref="Source{TOut,TMat}"/>, <see cref="Sink{TIn,TMat}"/>, 
            /// <see cref="Flow{TIn,TOut,TMat}"/> or <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/>.
            /// 
            /// It is possible to call this method multiple times to get multiple <see cref="Outlet{T}"/> instances if necessary. All of
            /// the outlets will emit the materialized value.
            /// 
            /// Be careful to not to feed the result of this outlet to a stage that produces the materialized value itself (for
            /// example to a <see cref="Sink.Aggregate{TIn,TOut}"/> that contributes to the materialized value) since that might lead to an unresolvable
            /// dependency cycle.
            /// </summary> 
            public Outlet<T> MaterializedValue
            {
                get
                {
                   /*
                    * This brings the graph into a homogenous shape: if only one `add` has
                    * been performed so far, the moduleInProgress will be a CopiedModule
                    * that upon the next `composeNoMat` will be wrapped together with the
                    * MaterializedValueSource into a CompositeModule, leading to its
                    * relevant computation being an Atomic() for the CopiedModule. This is
                    * what we must reference, and we can only get this reference if we
                    * create that computation up-front: just making one up will not work
                    * because that computation node would not be part of the tree and
                    * the source would not be triggered.
                    */
                    if (_moduleInProgress is CopiedModule module)
                        _moduleInProgress = CompositeModule.Create(module, module.Shape);

                    var source = new MaterializedValueSource<T>(_moduleInProgress.MaterializedValueComputation);
                    _moduleInProgress = _moduleInProgress.ComposeNoMaterialized(source.Module);
                    return source.Outlet;
                }
            }

            /// <summary>
            /// The module assembled by this builder so far.
            /// </summary>
            public IModule Module => _moduleInProgress;

            /// <summary>
            /// Starts a connection from the specified outlet.
            /// </summary>
            /// <typeparam name="TOut">The element type emitted by the outlet.</typeparam>
            /// <param name="outlet">The outlet from which to continue building the graph.</param>
            /// <returns>Operations for connecting this outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TOut>(Outlet<TOut> outlet)
            {
                return new ForwardOps<TOut, T>(this, outlet);
            }
            /// <summary>
            /// Starts a connection from the outlet of the specified source shape.
            /// </summary>
            /// <typeparam name="TOut">The element type emitted by the source outlet.</typeparam>
            /// <param name="source">The source shape whose outlet starts the connection.</param>
            /// <returns>Operations for connecting this outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TOut>(SourceShape<TOut> source)
            {
                return new ForwardOps<TOut, T>(this, source.Outlet);
            }
            /// <summary>
            /// Imports a source graph and starts a connection from its copied outlet.
            /// </summary>
            /// <typeparam name="TOut">The element type emitted by the source outlet.</typeparam>
            /// <param name="source">The source graph to add to the builder.</param>
            /// <returns>Operations for connecting the imported outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TOut>(IGraph<SourceShape<TOut>, T> source)
            {
                return new ForwardOps<TOut, T>(this, Add(source).Outlet);
            }
            /// <summary>
            /// Starts a connection from the outlet of the specified flow shape.
            /// </summary>
            /// <typeparam name="TIn">The flow inlet element type.</typeparam>
            /// <typeparam name="TOut">The flow outlet element type.</typeparam>
            /// <param name="flow">The flow shape whose outlet starts the connection.</param>
            /// <returns>Operations for connecting this outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(FlowShape<TIn, TOut> flow)
            {
                return new ForwardOps<TOut, T>(this, flow.Outlet);
            }
            /// <summary>
            /// Imports a flow graph and starts a connection from its copied outlet.
            /// </summary>
            /// <typeparam name="TIn">The flow inlet element type.</typeparam>
            /// <typeparam name="TOut">The flow outlet element type.</typeparam>
            /// <param name="flow">The flow graph to add to the builder.</param>
            /// <returns>Operations for connecting the imported outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(IGraph<FlowShape<TIn, TOut>, T> flow)
            {
                return new ForwardOps<TOut, T>(this, Add(flow).Outlet);
            }
            /// <summary>
            /// Starts a connection from the outlet of the specified fan-in shape.
            /// </summary>
            /// <typeparam name="TIn">The element type of the fan-in inlets.</typeparam>
            /// <typeparam name="TOut">The element type emitted by the fan-in outlet.</typeparam>
            /// <param name="fanIn">The fan-in shape whose outlet starts the connection.</param>
            /// <returns>Operations for connecting this outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(UniformFanInShape<TIn, TOut> fanIn)
            {
                return new ForwardOps<TOut, T>(this, fanIn.Out);
            }
            /// <summary>
            /// Starts a connection from the first currently unconnected outlet of a fan-out shape.
            /// </summary>
            /// <typeparam name="TIn">The element type of the fan-out inlet.</typeparam>
            /// <typeparam name="TOut">The element type emitted by its outlets.</typeparam>
            /// <param name="fanOut">The fan-out shape whose first free outlet starts the connection.</param>
            /// <returns>Operations for connecting this outlet to another port or stage.</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(UniformFanOutShape<TIn, TOut> fanOut)
            {
                return new ForwardOps<TOut, T>(this, FindOut(this, fanOut, 0));
            }

            /// <summary>
            /// Selects the specified inlet as the destination of a connection.
            /// </summary>
            /// <typeparam name="TIn">The inlet element type.</typeparam>
            /// <param name="inlet">The inlet to which elements will be connected.</param>
            /// <returns>Operations for connecting an outlet to this inlet.</returns>
            public ReverseOps<TIn, T> To<TIn>(Inlet<TIn> inlet)
            {
                return new ReverseOps<TIn, T>(this, inlet);
            }
            /// <summary>
            /// Selects the inlet of the specified sink shape as the destination of a connection.
            /// </summary>
            /// <typeparam name="TIn">The sink inlet element type.</typeparam>
            /// <param name="sink">The sink shape whose inlet receives elements.</param>
            /// <returns>Operations for connecting an outlet to this inlet.</returns>
            public ReverseOps<TIn, T> To<TIn>(SinkShape<TIn> sink)
            {
                return new ReverseOps<TIn, T>(this, sink.Inlet);
            }
            /// <summary>
            /// Imports a sink graph and selects its copied inlet as the destination.
            /// </summary>
            /// <typeparam name="TIn">The sink inlet element type.</typeparam>
            /// <typeparam name="TMat">The sink graph materialized value type.</typeparam>
            /// <param name="sink">The sink graph to add to this builder.</param>
            /// <returns>Operations for connecting an outlet to the imported inlet.</returns>
            public ReverseOps<TIn, T> To<TIn, TMat>(IGraph<SinkShape<TIn>, TMat> sink)
            {
                return new ReverseOps<TIn, T>(this, Add(sink).Inlet);
            }
            /// <summary>
            /// Imports a flow graph and selects its copied inlet as the destination.
            /// </summary>
            /// <typeparam name="TIn">The flow inlet element type.</typeparam>
            /// <typeparam name="TOut">The flow outlet element type.</typeparam>
            /// <typeparam name="TMat">The flow graph materialized value type.</typeparam>
            /// <param name="flow">The flow graph to add to this builder.</param>
            /// <returns>Operations for connecting an outlet to the imported inlet.</returns>
            public ReverseOps<TIn, T> To<TIn, TOut, TMat>(IGraph<FlowShape<TIn, TOut>, TMat> flow)
            {
                return new ReverseOps<TIn, T>(this, Add(flow).Inlet);
            }
            /// <summary>
            /// Selects the inlet of the specified flow shape as the destination of a connection.
            /// </summary>
            /// <typeparam name="TIn">The flow inlet element type.</typeparam>
            /// <typeparam name="TOut">The flow outlet element type.</typeparam>
            /// <param name="flow">The flow shape whose inlet receives elements.</param>
            /// <returns>Operations for connecting an outlet to this inlet.</returns>
            public ReverseOps<TIn, T> To<TIn, TOut>(FlowShape<TIn, TOut> flow)
            {
                return new ReverseOps<TIn, T>(this, flow.Inlet);
            }
            /// <summary>
            /// Selects the inlet of the specified fan-out shape as the destination.
            /// </summary>
            /// <typeparam name="TIn">The fan-out inlet element type.</typeparam>
            /// <typeparam name="TOut">The element type emitted by the fan-out outlets.</typeparam>
            /// <param name="fanOut">The fan-out shape whose inlet receives elements.</param>
            /// <returns>Operations for connecting an outlet to this inlet.</returns>
            public ReverseOps<TIn, T> To<TIn, TOut>(UniformFanOutShape<TIn, TOut> fanOut)
            {
                return new ReverseOps<TIn, T>(this, fanOut.In);
            }
            /// <summary>
            /// Selects the first unconnected indexed inlet of a fan-in shape as the destination. Extra ports such as
            /// the separate <c>MergePreferred.Preferred</c> inlet must be connected explicitly.
            /// </summary>
            /// <typeparam name="TIn">The fan-in inlet element type.</typeparam>
            /// <typeparam name="TOut">The element type emitted by the fan-in outlet.</typeparam>
            /// <param name="fanOut">The fan-in shape whose first free indexed inlet receives elements.</param>
            /// <returns>Operations for connecting an outlet to the selected indexed inlet.</returns>
            public ReverseOps<TIn, T> To<TIn, TOut>(UniformFanInShape<TIn, TOut> fanOut)
            {
                return new ReverseOps<TIn, T>(this, FindIn(this, fanOut, 0));
            }
        }

        /// <summary>
        /// Holds an outlet and builder while a graph connection is being assembled in the forward direction.
        /// </summary>
        /// <typeparam name="TOut">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TMat">The materialized value type of the graph being built.</typeparam>
        public sealed class ForwardOps<TOut, TMat>
        {
            /// <summary>
            /// Gets the builder that owns this connection.
            /// </summary>
            internal readonly Builder<TMat> Builder;

            /// <summary>
            /// Creates forward connection operations for an outlet in the supplied builder.
            /// </summary>
            /// <param name="builder">The graph builder to update as connections are added.</param>
            /// <param name="outlet">The outlet from which the connection continues.</param>
            public ForwardOps(Builder<TMat> builder, Outlet<TOut> outlet)
            {
                Builder = builder;
                Out = outlet;
            }

            /// <summary>
            /// Gets the outlet from which the next connection operation continues.
            /// </summary>
            public Outlet<TOut> Out { get; }
        }

        /// <summary>
        /// Holds an inlet and builder while a graph connection is being assembled in the reverse direction.
        /// </summary>
        /// <typeparam name="TIn">The element type accepted by the current inlet.</typeparam>
        /// <typeparam name="TMat">The materialized value type of the graph being built.</typeparam>
        public sealed class ReverseOps<TIn, TMat>
        {
            /// <summary>
            /// Gets the builder that owns this connection.
            /// </summary>
            internal readonly Builder<TMat> Builder;

            /// <summary>
            /// Creates reverse connection operations for an inlet in the supplied builder.
            /// </summary>
            /// <param name="builder">The graph builder to update as connections are added.</param>
            /// <param name="inlet">The inlet to which the connection continues.</param>
            public ReverseOps(Builder<TMat> builder, Inlet<TIn> inlet)
            {
                Builder = builder;
                In = inlet;
            }

            /// <summary>
            /// Gets the inlet to which the next connection operation continues.
            /// </summary>
            public Inlet<TIn> In { get; }
        }

        /// <summary>
        /// Finds the first unconnected outlet at or after the supplied index on a fan-out junction.
        /// </summary>
        /// <typeparam name="TIn">The element type accepted by the fan-out inlet.</typeparam>
        /// <typeparam name="TOut">The element type emitted by its outlets.</typeparam>
        /// <typeparam name="T">The graph builder's materialized value type.</typeparam>
        /// <param name="builder">The builder whose existing connections determine which outlet is free.</param>
        /// <param name="junction">The fan-out junction to inspect.</param>
        /// <param name="n">The first outlet index to inspect.</param>
        /// <exception cref="ArgumentException">No unconnected outlet exists at or after <paramref name="n"/>.</exception>
        /// <returns>The first outlet at or after <paramref name="n"/> that has no downstream connection.</returns>
        internal static Outlet<TOut> FindOut<TIn, TOut, T>(Builder<T> builder, UniformFanOutShape<TIn, TOut> junction, int n)
        {
            var count = junction.Outlets.Count();
            while (n < count)
            {
                var outlet = junction.Out(n);
                if (builder.Module.Downstreams.ContainsKey(outlet)) n++;
                else return outlet;
            }

            throw new ArgumentException("No more outlets on junction");
        }

        /// <summary>
        /// Finds the first unconnected indexed inlet at or after the supplied index on a fan-in junction. Extra ports
        /// such as the separate <c>MergePreferred.Preferred</c> inlet are not addressable through this helper and must
        /// be connected explicitly. On such shapes, an index beyond the indexed ports can fail during lookup.
        /// </summary>
        /// <typeparam name="TIn">The element type accepted by the fan-in inlets.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the fan-in outlet.</typeparam>
        /// <typeparam name="T">The graph builder's materialized value type.</typeparam>
        /// <param name="builder">The builder whose existing connections determine which inlet is free.</param>
        /// <param name="junction">The fan-in junction to inspect.</param>
        /// <param name="n">The first inlet index to inspect.</param>
        /// <returns>The first indexed inlet at or after <paramref name="n"/> that has no upstream connection.</returns>
        internal static Inlet<TIn> FindIn<TIn, TOut, T>(Builder<T> builder, UniformFanInShape<TIn, TOut> junction, int n)
        {
            var count = junction.Inlets.Count();
            while (n < count)
            {
                var inlet = junction.In(n);
                if (builder.Module.Upstreams.ContainsKey(inlet)) n++;
                else return inlet;
            }

            throw new ArgumentException("No more inlets on junction");
        }
    }

    /// <summary>
    /// Extension methods for continuing a graph connection from an outlet.
    /// </summary>
    public static class ForwardOps
    {
        /// <summary>
        /// Connects the current outlet to an inlet.
        /// </summary>
        /// <typeparam name="TIn">The inlet element type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="inlet">The inlet to connect.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat>(this GraphDsl.ForwardOps<TOut, TMat> ops, Inlet<TIn> inlet)
            where TIn : TOut
        {
            ops.Builder.AddEdge(ops.Out, inlet);
            return ops.Builder;
        }

        /// <summary>
        /// Connects the current outlet to a sink shape's inlet.
        /// </summary>
        /// <typeparam name="TIn">The sink inlet type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="sink">The sink shape whose inlet receives elements.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat>(this GraphDsl.ForwardOps<TOut, TMat> ops, SinkShape<TIn> sink)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, sink.Inlet);
            return b;
        }

        /// <summary>
        /// Connects the current outlet to a flow shape's inlet.
        /// </summary>
        /// <typeparam name="TIn">The flow inlet type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="flow">The flow shape whose inlet receives elements.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat>(this GraphDsl.ForwardOps<TOut, TMat> ops, FlowShape<TIn, TOut> flow)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, flow.Inlet);
            return b;
        }

        /// <summary>
        /// Connects the current outlet to an imported sink graph's copied inlet.
        /// </summary>
        /// <typeparam name="TIn">The sink inlet type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TOut">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <typeparam name="TMat2">The imported sink graph's materialized value type, which this operation does not retain.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="sink">The sink graph to copy into the builder.</param>
        /// <returns>The graph builder after adding and connecting the sink.</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat, TMat2>(this GraphDsl.ForwardOps<TOut, TMat> ops, IGraph<SinkShape<TIn>, TMat2> sink)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, b.Add(sink).Inlet);
            return b;
        }

        /// <summary>
        /// Connects the current outlet to the first unconnected indexed inlet of a fan-in junction. Extra ports such as
        /// the separate <c>MergePreferred.Preferred</c> inlet must be connected explicitly.
        /// </summary>
        /// <typeparam name="TIn">The fan-in inlet type; this method constrains <c>TIn : TOut1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TOut2">The element type emitted by the fan-in outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="junction">The fan-in junction whose first free indexed inlet receives elements.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanInShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var b = ops.Builder;
            var inlet = GraphDsl.FindIn(b, junction, 0);
            b.AddEdge(ops.Out, inlet);
            return b;
        }

        /// <summary>
        /// Connects the current outlet to the inlet of a fan-out junction.
        /// </summary>
        /// <typeparam name="TIn">The fan-out inlet type; this method constrains <c>TIn : TOut1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TOut2">The element type emitted by the fan-out outlets.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="junction">The fan-out junction whose inlet receives elements.</param>
        /// <exception cref="ArgumentException">The junction inlet is already connected.</exception>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanOutShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var b = ops.Builder;

            if (!b.Module.Upstreams.ContainsKey(junction.In))
            {
                b.AddEdge(ops.Out, junction.In);
                return b;
            }

            throw new ArgumentException("No more inlets free on junction", nameof(junction));
        }

        private static Outlet<TOut2> Bind<TIn, TOut1, TOut2, TMat>(GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanOutShape<TIn, TOut2> junction) where TIn : TOut1
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, junction.In);
            return GraphDsl.FindOut(b, junction, 0);
        }

        /// <summary>
        /// Connects the current outlet through a flow shape and continues from its outlet.
        /// </summary>
        /// <typeparam name="TIn">The flow inlet type; this method constrains <c>TIn : TOut1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TOut2">The element type emitted by the flow outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="flow">The flow shape to connect.</param>
        /// <returns>Forward operations starting at the flow outlet.</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, FlowShape<TIn, TOut2> flow)
            where TIn : TOut1
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, flow.Inlet);
            return new GraphDsl.ForwardOps<TOut2, TMat>(b, flow.Outlet);
        }

        /// <summary>
        /// Imports a flow graph, connects the current outlet to its copied inlet, and continues from its copied outlet.
        /// </summary>
        /// <typeparam name="TIn">The flow inlet type; this method constrains <c>TIn : TOut1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TOut2">The element type emitted by the flow outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="flow">The flow graph to import; its materialized value is discarded.</param>
        /// <returns>Forward operations starting at the imported flow's copied outlet.</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, IGraph<FlowShape<TIn, TOut2>, NotUsed> flow)
            where TIn : TOut1
        {
            var b = ops.Builder;
            var s = b.Add(flow);
            b.AddEdge(ops.Out, s.Inlet);
            return new GraphDsl.ForwardOps<TOut2, TMat>(b, s.Outlet);
        }

        /// <summary>
        /// Connects the current outlet to the first free indexed fan-in inlet and continues from the fan-in outlet.
        /// The separate <c>MergePreferred.Preferred</c> inlet must be connected explicitly.
        /// </summary>
        /// <typeparam name="TIn">The fan-in inlet type; this method constrains <c>TIn : TOut1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TOut2">The element type emitted by the fan-in outlet.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="junction">The fan-in junction to connect.</param>
        /// <returns>Forward operations starting at the fan-in outlet.</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanInShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var b = To(ops, junction);
            return b.From(junction.Out);
        }

        /// <summary>
        /// Connects the current outlet to a fan-out inlet and continues from its first free outlet.
        /// </summary>
        /// <typeparam name="TIn">The fan-out inlet type; this method constrains <c>TIn : TOut1</c>.</typeparam>
        /// <typeparam name="TOut1">The element type emitted by the current outlet.</typeparam>
        /// <typeparam name="TOut2">The element type emitted by the fan-out outlets.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current outlet and its graph builder.</param>
        /// <param name="junction">The fan-out junction to connect.</param>
        /// <returns>Forward operations starting at the first unconnected fan-out outlet.</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanOutShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var outlet = Bind(ops, junction);
            return ops.Builder.From(outlet);
        }
    }

    /// <summary>
    /// Extension methods for continuing a graph connection backward from an inlet.
    /// </summary>
    public static class ReverseOps
    {
        /// <summary>
        /// Connects an outlet to the current inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut">The outlet element type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="outlet">The outlet to connect.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, Outlet<TOut> outlet)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(outlet, ops.In);
            return b;
        }

        /// <summary>
        /// Connects a source shape's outlet to the current inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut">The source outlet element type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="source">The source shape whose outlet supplies elements.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, SourceShape<TOut> source)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(source.Outlet, ops.In);
            return b;
        }

        /// <summary>
        /// Imports a source graph and connects its copied outlet to the current inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut">The source outlet element type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="source">The source graph to import; its materialized value is discarded.</param>
        /// <returns>The graph builder after importing and connecting the source.</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, IGraph<SourceShape<TOut>, TMat> source)
            where TIn : TOut
        {
            var b = ops.Builder;
            var s = b.Add(source);
            b.AddEdge(s.Outlet, ops.In);
            return b;
        }

        /// <summary>
        /// Connects a flow shape's outlet to the current inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut">The flow outlet element type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="flow">The flow shape whose outlet supplies elements.</param>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, FlowShape<TIn, TOut> flow)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(flow.Outlet, ops.In);
            return b;
        }

        /// <summary>
        /// Connects a fan-in junction's outlet to the current inlet and returns the builder. Its lookup uses indexed
        /// inlets; the separate <c>MergePreferred.Preferred</c> inlet must be connected explicitly.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut">The fan-in outlet element type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="junction">The fan-in junction whose outlet connects to the current inlet.</param>
        /// <returns>The graph builder after connecting the fan-in outlet to the current inlet.</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanInShape<TIn, TOut> junction)
            where TIn : TOut
        {
            Bind(ops, junction);
            return ops.Builder;
        }

        private static Inlet<TIn> Bind<TIn, TOut, TMat>(GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanInShape<TIn, TOut> junction)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(junction.Out, ops.In);
            return GraphDsl.FindIn(b, junction, 0);
        }

        /// <summary>
        /// Connects the first free outlet of a fan-out junction to the current inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut1">The fan-out inlet element type.</typeparam>
        /// <typeparam name="TOut2">The fan-out outlet element type; this method constrains <c>TIn : TOut2</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="junction">The fan-out junction whose first unconnected outlet supplies elements.</param>
        /// <exception cref="ArgumentException">Every outlet on the fan-out junction is already connected.</exception>
        /// <returns>The graph builder after adding the connection.</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut1, TOut2, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanOutShape<TOut1, TOut2> junction)
            where TIn : TOut2
        {
            var b = ops.Builder;
            var count = junction.Outlets.Count();
            for (var n = 0; n < count; n++)
            {
                var outlet = junction.Out(n);
                if (!b.Module.Downstreams.ContainsKey(outlet))
                {
                    b.AddEdge(outlet, ops.In);
                    return b;
                }
            }

            throw new ArgumentException("No more inlets free on junction", nameof(junction));
        }

        /// <summary>
        /// Connects a flow shape's outlet to the current inlet and continues backward from its inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut1">The flow inlet element type.</typeparam>
        /// <typeparam name="TOut2">The flow outlet element type; this method constrains <c>TIn : TOut2</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="flow">The flow shape to connect.</param>
        /// <returns>Reverse operations targeting the flow inlet.</returns>
        public static GraphDsl.ReverseOps<TOut1, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, FlowShape<TOut1, TOut2> flow)
            where TIn : TOut2
        {
            var b = ops.Builder;
            b.AddEdge(flow.Outlet, ops.In);
            return new GraphDsl.ReverseOps<TOut1, TMat>(b, flow.Inlet);
        }

        /// <summary>
        /// Imports a flow graph, connects its copied outlet to the current inlet, and continues from its copied inlet.
        /// </summary>
        /// <typeparam name="TIn">The current inlet element type.</typeparam>
        /// <typeparam name="TOut1">The flow inlet element type.</typeparam>
        /// <typeparam name="TOut2">The flow outlet element type; this method constrains <c>TIn : TOut2</c>.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="flow">The flow graph to import; its materialized value is discarded.</param>
        /// <returns>Reverse operations targeting the imported flow's copied inlet.</returns>
        public static GraphDsl.ReverseOps<TOut1, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, IGraph<FlowShape<TOut1, TOut2>, TMat> flow)
            where TIn : TOut2
        {
            var b = ops.Builder;
            var f = b.Add(flow);
            b.AddEdge(f.Outlet, ops.In);
            return new GraphDsl.ReverseOps<TOut1, TMat>(b, f.Inlet);
        }

        /// <summary>
        /// Connects the fan-in outlet to the current inlet and uses the first free indexed fan-in inlet as the next
        /// target. The separate <c>MergePreferred.Preferred</c> inlet must be connected explicitly.
        /// </summary>
        /// <typeparam name="TIn">The fan-in inlet type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TOut">The fan-in outlet element type.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="junction">The fan-in junction to connect.</param>
        /// <returns>Reverse operations targeting the first unconnected indexed fan-in inlet.</returns>
        public static GraphDsl.ReverseOps<TIn, TMat> Via<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanInShape<TIn, TOut> junction)
            where TIn : TOut
        {
            var inlet = Bind(ops, junction);
            return ops.Builder.To(inlet);
        }

        /// <summary>
        /// Connects the fan-out outlet to the current inlet and selects the fan-out inlet as the next target.
        /// </summary>
        /// <typeparam name="TIn">The fan-out inlet type; this method constrains <c>TIn : TOut</c>.</typeparam>
        /// <typeparam name="TOut">The fan-out outlet element type.</typeparam>
        /// <typeparam name="TMat">The graph builder's materialized value type.</typeparam>
        /// <param name="ops">The current inlet and its graph builder.</param>
        /// <param name="junction">The fan-out junction to connect.</param>
        /// <returns>Reverse operations targeting the fan-out inlet.</returns>
        public static GraphDsl.ReverseOps<TIn, TMat> Via<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanOutShape<TIn, TOut> junction)
            where TIn : TOut
        {
            var b = From(ops, junction);
            return b.To(junction.In);
        }
    }
}
