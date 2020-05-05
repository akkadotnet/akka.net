//-----------------------------------------------------------------------
// <copyright file="BidiFlow.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class BidiFlow
    {
        /// <summary>
        /// A graph with the shape of a flow logically is a flow, this method makes
        /// it so also in type.
        /// </summary>
        /// <typeparam name="TIn1">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="graph">TBD</param>
        /// <returns>TBD</returns>
        public static BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> FromGraph<TIn1, TOut1, TIn2, TOut2, TMat>(
            IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> graph)
        {
            return graph is BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>
                ? (BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>) graph
                : new BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>(graph.Module);
        }

        /// <summary>
        /// Wraps two Flows to create a <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/>. The materialized value of the resulting BidiFlow is determined
        /// by the combiner function passed in the second argument list.
        /// <![CDATA[ 
        /// {{{
        ///     +----------------------------+
        ///     | Resulting BidiFlow         |
        ///     |                            |
        ///     |  +----------------------+  |
        /// I1 ~~> |        Flow1         | ~~> O1
        ///     |  +----------------------+  |
        ///     |                            |
        ///     |  +----------------------+  |
        /// O2 \<~~ |        Flow2         | <~~ I2
        ///     |  +----------------------+  |
        ///     +----------------------------+
        /// }}}
        /// ]]>
        /// </summary>
        /// <typeparam name="TIn1">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat1">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat">TBD</typeparam>
        /// <param name="flow1">TBD</param>
        /// <param name="flow2">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public static BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> FromFlowsMat
            <TIn1, TOut1, TIn2, TOut2, TMat1, TMat2, TMat>(IGraph<FlowShape<TIn1, TOut1>, TMat1> flow1,
                IGraph<FlowShape<TIn2, TOut2>, TMat2> flow2, Func<TMat1, TMat2, TMat> combine)
        {
            return FromGraph(GraphDsl.Create(flow1, flow2, combine,
                 (builder, f1, f2) => new BidiShape<TIn1, TOut1, TIn2, TOut2>(f1.Inlet, f1.Outlet, f2.Inlet, f2.Outlet)));
        }

        /// <summary>
        /// Wraps two Flows to create a <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/>. The materialized value of the resulting BidiFlow is NotUsed.
        /// <![CDATA[ 
        /// {{{
        ///     +----------------------------+
        ///     | Resulting BidiFlow         |
        ///     |                            |
        ///     |  +----------------------+  |
        /// I1 ~~> |        Flow1         | ~~> O1
        ///     |  +----------------------+  |
        ///     |                            |
        ///     |  +----------------------+  |
        /// O2 <~~ |        Flow2         | <~~ I2
        ///     |  +----------------------+  |
        ///     +----------------------------+
        /// }}}
        /// ]]>
        /// </summary>
        /// <typeparam name="TIn1">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <typeparam name="TMat1">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow1">TBD</param>
        /// <param name="flow2">TBD</param>
        /// <returns>TBD</returns>
        public static BidiFlow<TIn1, TOut1, TIn2, TOut2, NotUsed> FromFlows<TIn1, TOut1, TIn2, TOut2, TMat1, TMat2>(
            IGraph<FlowShape<TIn1, TOut1>, TMat1> flow1, IGraph<FlowShape<TIn2, TOut2>, TMat2> flow2)
        {
            return FromFlowsMat(flow1, flow2, Keep.None);
        }

        /// <summary>
        /// Create a <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/> where the top and bottom flows are just one simple mapping
        /// stage each, expressed by the two functions.
        /// </summary>
        /// <typeparam name="TIn1">TBD</typeparam>
        /// <typeparam name="TOut1">TBD</typeparam>
        /// <typeparam name="TIn2">TBD</typeparam>
        /// <typeparam name="TOut2">TBD</typeparam>
        /// <param name="outbound">TBD</param>
        /// <param name="inbound">TBD</param>
        /// <returns>TBD</returns>
        public static BidiFlow<TIn1, TOut1, TIn2, TOut2, NotUsed> FromFunction<TIn1, TOut1, TIn2, TOut2>(Func<TIn1, TOut1> outbound, Func<TIn2, TOut2> inbound)
        {
            return FromFlows(Flow.Create<TIn1>().Select(outbound), Flow.Create<TIn2>().Select(inbound));
        }

        /// <summary>
        /// If the time between two processed elements ///in any direction/// exceed the provided timeout, the stream is failed
        /// with a <see cref="TimeoutException"/>.
        ///
        /// There is a difference between this stage and having two idleTimeout Flows assembled into a BidiStage.
        /// If the timeout is configured to be 1 seconds, then this stage will not fail even though there are elements flowing
        /// every second in one direction, but no elements are flowing in the other direction. I.e. this stage considers
        /// the ///joint/// frequencies of the elements in both directions.
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public static BidiFlow<TIn, TIn, TOut, TOut, NotUsed> BidirectionalIdleTimeout<TIn, TOut>(TimeSpan timeout)
        {
            return FromGraph(new IdleTimeoutBidi<TIn, TOut>(timeout));
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TIn1">TBD</typeparam>
    /// <typeparam name="TOut1">TBD</typeparam>
    /// <typeparam name="TIn2">TBD</typeparam>
    /// <typeparam name="TOut2">TBD</typeparam>
    /// <typeparam name="TMat">TBD</typeparam>
    public class BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> : IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat>
    {
        private readonly IModule _module;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="module">TBD</param>
        public BidiFlow(IModule module)
        {
            _module = module;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public BidiShape<TIn1, TOut1, TIn2, TOut2> Shape => (BidiShape<TIn1, TOut1, TIn2, TOut2>)_module.Shape;

        /// <summary>
        /// TBD
        /// </summary>
        public IModule Module => _module;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> WithAttributes(Attributes attributes)
        {
            return new BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>(_module.WithAttributes(attributes));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> AddAttributes(Attributes attributes)
        {
            return WithAttributes(Module.Attributes.And(attributes));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> Named(string name)
        {
            return AddAttributes(Attributes.CreateName(name));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> Async()
        {
            return AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }

        /// <summary>
        /// Turn this BidiFlow around by 180 degrees, logically flipping it upside down in a protocol stack.
        /// </summary>
        /// <returns>TBD</returns>
        public BidiFlow<TIn2, TOut2, TIn1, TOut1, TMat> Reversed()
        {
            return new BidiFlow<TIn2, TOut2, TIn1, TOut1, TMat>(Module.ReplaceShape(Shape.Reversed()));
        }

        /// <summary>
        /// Add the given BidiFlow as the next step in a bidirectional transformation
        /// pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
        /// layer, the closest to the metal.
        /// <![CDATA[ 
        /// {{{
        ///     +----------------------------+
        ///     | Resulting BidiFlow         |
        ///     |                            |
        ///     |  +------+        +------+  |
        /// I1 ~~> |      |  ~O1~> |      | ~~> OO1
        ///     |  | this |        | bidi |  |
        /// O2 <~~ |      | <~I2~  |      | <~~ II2
        ///     |  +------+        +------+  |
        ///     +----------------------------+
        /// }}}
        /// ]]>
        /// The materialized value of the combined <see cref="BidiFlow{TIn1,TOut1,TIn2,TOut2,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other BidiFlow’s value), use
        /// <see cref="AtopMat{TOut12,TIn21,TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="TOut12">TBD</typeparam>
        /// <typeparam name="TIn21">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="bidi">TBD</param>
        /// <returns>TBD</returns>
        public BidiFlow<TIn1, TOut12, TIn21, TOut2, TMat> Atop<TOut12, TIn21, TMat2>(BidiFlow<TOut1, TOut12, TIn21, TIn2, TMat2> bidi)
        {
            return AtopMat(bidi, Keep.Left);
        }

        /// <summary>
        /// Add the given BidiFlow as the next step in a bidirectional transformation
        /// pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
        /// layer, the closest to the metal.
        ///  <![CDATA[ 
        /// {{{
        ///     +----------------------------+
        ///     | Resulting BidiFlow         |
        ///     |                            |
        ///     |  +------+        +------+  |
        /// I1 ~~> |      |  ~O1~> |      | ~~> OO1
        ///     |  | this |        | bidi |  |
        /// O2 <~~ |      | <~I2~  |      | <~~ II2
        ///     |  +------+        +------+  |
        ///     +----------------------------+
        /// }}}
        ///  ]]>
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting BidiFlow.
        /// </summary>
        /// <typeparam name="TOut12">TBD</typeparam>
        /// <typeparam name="TIn21">TBD</typeparam>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="bidi">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public BidiFlow<TIn1, TOut12, TIn21, TOut2, TMat3> AtopMat<TOut12, TIn21, TMat2, TMat3>(BidiFlow<TOut1, TOut12, TIn21, TIn2, TMat2> bidi, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = bidi.Module.CarbonCopy();
            var ins = copy.Shape.Inlets.ToArray();
            var outs = copy.Shape.Outlets.ToArray();

            return new BidiFlow<TIn1, TOut12, TIn21, TOut2, TMat3>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlet1, ins[0])
                .Wire(outs[1], Shape.Inlet2)
                .ReplaceShape(new BidiShape<TIn1, TOut12, TIn21, TOut2>(Shape.Inlet1, (Outlet<TOut12>)outs[0], (Inlet<TIn21>)ins[1], Shape.Outlet2)));
        }

        /// <summary>
        /// Add the given Flow as the final step in a bidirectional transformation
        /// pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
        /// layer, the closest to the metal.
        /// <![CDATA[ 
        /// {{{
        ///     +---------------------------+
        ///     | Resulting Flow            |
        ///     |                           |
        ///     |  +------+        +------+ |
        /// I1 ~~> |      |  ~O1~> |      | |
        ///     |  | this |        | flow | |
        /// O2 <~~ |      | <~I2~  |      | |
        ///     |  +------+        +------+ |
        ///     +---------------------------+
        /// }}}
        /// ]]>
        /// The materialized value of the combined <see cref="Flow{TIn,TOut,TMat}"/> will be the materialized
        /// value of the current flow (ignoring the other Flow’s value), use
        /// <see cref="JoinMat{TMat2,TMat3}"/> if a different strategy is needed.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn1, TOut2, TMat> Join<TMat2>(Flow<TOut1, TIn2, TMat2> flow)
        {
            return JoinMat(flow, Keep.Left);
        }

        /// <summary>
        /// Add the given Flow as the final step in a bidirectional transformation
        /// pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
        /// layer, the closest to the metal.
        /// <![CDATA[ 
        /// {{{
        ///     +---------------------------+
        ///     | Resulting Flow            |
        ///     |                           |
        ///     |  +------+        +------+ |
        /// I1 ~~> |      |  ~O1~> |      | |
        ///     |  | this |        | flow | |
        /// O2 <~~ |      | <~I2~  |      | |
        ///     |  +------+        +------+ |
        ///     +---------------------------+
        /// }}}
        /// ]]>
        /// The <paramref name="combine"/> function is used to compose the materialized values of this flow and that
        /// flow into the materialized value of the resulting <see cref="Flow{TIn,TOut,TMat}"/>.
        /// </summary>
        /// <typeparam name="TMat2">TBD</typeparam>
        /// <typeparam name="TMat3">TBD</typeparam>
        /// <param name="flow">TBD</param>
        /// <param name="combine">TBD</param>
        /// <returns>TBD</returns>
        public Flow<TIn1, TOut2, TMat3> JoinMat<TMat2, TMat3>(Flow<TOut1, TIn2, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = flow.Module.CarbonCopy();
            var inlet = copy.Shape.Inlets.First();
            var outlet = copy.Shape.Outlets.First();
            return new Flow<TIn1, TOut2, TMat3>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlets.First(), inlet)
                .Wire(outlet, Shape.Inlets.ElementAt(1))
                .ReplaceShape(new FlowShape<TIn1, TOut2>((Inlet<TIn1>)Shape.Inlets.First(), (Outlet<TOut2>)Shape.Outlets.ElementAt(1))));
        }
    }
}
