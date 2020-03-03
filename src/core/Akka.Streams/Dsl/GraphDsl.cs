//-----------------------------------------------------------------------
// <copyright file="GraphDsl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public static partial class GraphDsl
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        public sealed class Builder<T>
        {
            #region internal API

            /// <summary>
            /// TBD
            /// </summary>
            internal Builder() { }

            private IModule _moduleInProgress = EmptyModule.Instance;

            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="T1">TBD</typeparam>
            /// <typeparam name="T2">TBD</typeparam>
            /// <param name="from">TBD</param>
            /// <param name="to">TBD</param>
            internal void AddEdge<T1, T2>(Outlet<T1> from, Inlet<T2> to) where T2 : T1
            {
                _moduleInProgress = _moduleInProgress.Wire(from, to);
            }

            /// <summary>
            /// INTERNAL API. 
            /// This is only used by the materialization-importing apply methods of Source,
            /// Flow, Sink and Graph.
            /// </summary>
            /// <typeparam name="TShape">TBD</typeparam>
            /// <typeparam name="TMat">TBD</typeparam>
            /// <typeparam name="TMat2">TBD</typeparam>
            /// <param name="graph">TBD</param>
            /// <param name="transform">TBD</param>
            /// <returns>TBD</returns>
            internal TShape Add<TShape, TMat, TMat2>(IGraph<TShape, TMat> graph, Func<TMat, TMat2> transform) where TShape : Shape
            {
                if (StreamLayout.IsDebug)
                    StreamLayout.Validate(graph.Module);

                var copy = graph.Module.CarbonCopy();
                _moduleInProgress = _moduleInProgress.Compose<TMat,TMat2,TMat2>(copy.TransformMaterializedValue(transform), Keep.Right);
                return (TShape)graph.Shape.CopyFromPorts(copy.Shape.Inlets, copy.Shape.Outlets);
            }

            /// <summary>
            /// INTERNAL API. 
            /// This is only used by the materialization-importing apply methods of Source,
            /// Flow, Sink and Graph.
            /// </summary>
            /// <typeparam name="TShape">TBD</typeparam>
            /// <typeparam name="TMat1">TBD</typeparam>
            /// <typeparam name="TMat2">TBD</typeparam>
            /// <typeparam name="TMat3">TBD</typeparam>
            /// <param name="graph">TBD</param>
            /// <param name="combine">TBD</param>
            /// <returns>TBD</returns>
            internal TShape Add<TShape, TMat1, TMat2, TMat3>(IGraph<TShape> graph, Func<TMat1, TMat2, TMat3> combine) where TShape : Shape
            {
                if (StreamLayout.IsDebug)
                    StreamLayout.Validate(graph.Module);

                var copy = graph.Module.CarbonCopy();
                _moduleInProgress = _moduleInProgress.Compose(copy, combine);
                return (TShape)graph.Shape.CopyFromPorts(copy.Shape.Inlets, copy.Shape.Outlets);
            }

            #endregion

            /// <summary>
            /// Import a graph into this module, performing a deep copy, discarding its
            /// materialized value and returning the copied Ports that are now to be connected.
            /// </summary>
            /// <typeparam name="TShape">TBD</typeparam>
            /// <typeparam name="TMat">TBD</typeparam>
            /// <param name="graph">TBD</param>
            /// <returns>TBD</returns>
            public TShape Add<TShape, TMat>(IGraph<TShape, TMat> graph)
                where TShape : Shape
            {
                if (StreamLayout.IsDebug)
                    StreamLayout.Validate(graph.Module);

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
                    if (_moduleInProgress is CopiedModule)
                        _moduleInProgress = CompositeModule.Create((Module) _moduleInProgress, _moduleInProgress.Shape);

                    var source = new MaterializedValueSource<T>(_moduleInProgress.MaterializedValueComputation);
                    _moduleInProgress = _moduleInProgress.ComposeNoMaterialized(source.Module);
                    return source.Outlet;
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IModule Module => _moduleInProgress;

            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="outlet">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TOut>(Outlet<TOut> outlet)
            {
                return new ForwardOps<TOut, T>(this, outlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="source">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TOut>(SourceShape<TOut> source)
            {
                return new ForwardOps<TOut, T>(this, source.Outlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="source">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TOut>(IGraph<SourceShape<TOut>, T> source)
            {
                return new ForwardOps<TOut, T>(this, Add(source).Outlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(FlowShape<TIn, TOut> flow)
            {
                return new ForwardOps<TOut, T>(this, flow.Outlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(IGraph<FlowShape<TIn, TOut>, T> flow)
            {
                return new ForwardOps<TOut, T>(this, Add(flow).Outlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="fanIn">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(UniformFanInShape<TIn, TOut> fanIn)
            {
                return new ForwardOps<TOut, T>(this, fanIn.Out);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="fanOut">TBD</param>
            /// <returns>TBD</returns>
            public ForwardOps<TOut, T> From<TIn, TOut>(UniformFanOutShape<TIn, TOut> fanOut)
            {
                return new ForwardOps<TOut, T>(this, FindOut(this, fanOut, 0));
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <param name="inlet">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn>(Inlet<TIn> inlet)
            {
                return new ReverseOps<TIn, T>(this, inlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <param name="sink">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn>(SinkShape<TIn> sink)
            {
                return new ReverseOps<TIn, T>(this, sink.Inlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TMat">TBD</typeparam>
            /// <param name="sink">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn, TMat>(IGraph<SinkShape<TIn>, TMat> sink)
            {
                return new ReverseOps<TIn, T>(this, Add(sink).Inlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <typeparam name="TMat">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn, TOut, TMat>(IGraph<FlowShape<TIn, TOut>, TMat> flow)
            {
                return new ReverseOps<TIn, T>(this, Add(flow).Inlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="flow">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn, TOut>(FlowShape<TIn, TOut> flow)
            {
                return new ReverseOps<TIn, T>(this, flow.Inlet);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="fanOut">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn, TOut>(UniformFanOutShape<TIn, TOut> fanOut)
            {
                return new ReverseOps<TIn, T>(this, fanOut.In);
            }
            /// <summary>
            /// TBD
            /// </summary>
            /// <typeparam name="TIn">TBD</typeparam>
            /// <typeparam name="TOut">TBD</typeparam>
            /// <param name="fanOut">TBD</param>
            /// <returns>TBD</returns>
            public ReverseOps<TIn, T> To<TIn, TOut>(UniformFanInShape<TIn, TOut> fanOut)
            {
                return new ReverseOps<TIn, T>(this, FindIn(this, fanOut, 0));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        public sealed class ForwardOps<TOut, TMat>
        {
            /// <summary>
            /// TBD
            /// </summary>
            internal readonly Builder<TMat> Builder;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="builder">TBD</param>
            /// <param name="outlet">TBD</param>
            public ForwardOps(Builder<TMat> builder, Outlet<TOut> outlet)
            {
                Builder = builder;
                Out = outlet;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Outlet<TOut> Out { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        public sealed class ReverseOps<TIn, TMat>
        {
            /// <summary>
            /// TBD
            /// </summary>
            internal readonly Builder<TMat> Builder;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="builder">TBD</param>
            /// <param name="inlet">TBD</param>
            public ReverseOps(Builder<TMat> builder, Inlet<TIn> inlet)
            {
                Builder = builder;
                In = inlet;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Inlet<TIn> In { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="builder">TBD</param>
        /// <param name="junction">TBD</param>
        /// <param name="n">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="builder">TBD</param>
        /// <param name="junction">TBD</param>
        /// <param name="n">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
    /// TBD
    /// </summary>
    public static class ForwardOps
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat>(this GraphDsl.ForwardOps<TOut, TMat> ops, Inlet<TIn> inlet)
            where TIn : TOut
        {
            ops.Builder.AddEdge(ops.Out, inlet);
            return ops.Builder;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="sink">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat>(this GraphDsl.ForwardOps<TOut, TMat> ops, SinkShape<TIn> sink)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, sink.Inlet);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat>(this GraphDsl.ForwardOps<TOut, TMat> ops, FlowShape<TIn, TOut> flow)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, flow.Inlet);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="sink">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut, TMat, TMat2>(this GraphDsl.ForwardOps<TOut, TMat> ops, IGraph<SinkShape<TIn>, TMat2> sink)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, b.Add(sink).Inlet);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> To<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanInShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var b = ops.Builder;
            var inlet = GraphDsl.FindIn(b, junction, 0);
            b.AddEdge(ops.Out, inlet);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, FlowShape<TIn, TOut2> flow)
            where TIn : TOut1
        {
            var b = ops.Builder;
            b.AddEdge(ops.Out, flow.Inlet);
            return new GraphDsl.ForwardOps<TOut2, TMat>(b, flow.Outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, IGraph<FlowShape<TIn, TOut2>, NotUsed> flow)
            where TIn : TOut1
        {
            var b = ops.Builder;
            var s = b.Add(flow);
            b.AddEdge(ops.Out, s.Inlet);
            return new GraphDsl.ForwardOps<TOut2, TMat>(b, s.Outlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanInShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var b = To(ops, junction);
            return b.From(junction.Out);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ForwardOps<TOut2, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ForwardOps<TOut1, TMat> ops, UniformFanOutShape<TIn, TOut2> junction)
            where TIn : TOut1
        {
            var outlet = Bind(ops, junction);
            return ops.Builder.From(outlet);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class ReverseOps
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="outlet">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, Outlet<TOut> outlet)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(outlet, ops.In);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="source">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, SourceShape<TOut> source)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(source.Outlet, ops.In);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="source">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, IGraph<SourceShape<TOut>, TMat> source)
            where TIn : TOut
        {
            var b = ops.Builder;
            var s = b.Add(source);
            b.AddEdge(s.Outlet, ops.In);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.Builder<TMat> From<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, FlowShape<TIn, TOut> flow)
            where TIn : TOut
        {
            var b = ops.Builder;
            b.AddEdge(flow.Outlet, ops.In);
            return b;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <exception cref="ArgumentException">TBD</exception>
        /// <returns>TBD</returns>
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
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ReverseOps<TOut1, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, FlowShape<TOut1, TOut2> flow)
            where TIn : TOut2
        {
            var b = ops.Builder;
            b.AddEdge(flow.Outlet, ops.In);
            return new GraphDsl.ReverseOps<TOut1, TMat>(b, flow.Inlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ReverseOps<TOut1, TMat> Via<TIn, TOut1, TOut2, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, IGraph<FlowShape<TOut1, TOut2>, TMat> flow)
            where TIn : TOut2
        {
            var b = ops.Builder;
            var f = b.Add(flow);
            b.AddEdge(f.Outlet, ops.In);
            return new GraphDsl.ReverseOps<TOut1, TMat>(b, f.Inlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ReverseOps<TIn, TMat> Via<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanInShape<TIn, TOut> junction)
            where TIn : TOut
        {
            var inlet = Bind(ops, junction);
            return ops.Builder.To(inlet);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="ops">TBD</param>
        /// <param name="junction">TBD</param>
        /// <returns>TBD</returns>
        public static GraphDsl.ReverseOps<TIn, TMat> Via<TIn, TOut, TMat>(this GraphDsl.ReverseOps<TIn, TMat> ops, UniformFanOutShape<TIn, TOut> junction)
            where TIn : TOut
        {
            var b = From(ops, junction);
            return b.To(junction.In);
        }
    }
}
