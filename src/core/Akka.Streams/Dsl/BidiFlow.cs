using System;
using Akka.Streams.Implementation;

namespace Akka.Streams.Dsl
{
    public static class BidiFlow
    {
        /**
         * A graph with the shape of a flow logically is a flow, this method makes
         * it so also in type.
         */
        public static BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> Wrap<TIn1, TOut1, TIn2, TOut2, TMat>(
            IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> graph)
        {
            return graph as BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> ?? new BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>(graph.Module);
        }
    }

    public class BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> : IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat>
    {
        private readonly IModule _module;

        public BidiFlow(IModule module)
        {
            _module = module;
        }

        public BidiShape<TIn1, TOut1, TIn2, TOut2> Shape { get { return (BidiShape<TIn1, TOut1, TIn2, TOut2>)_module.Shape; } }
        public IModule Module { get { return _module; } }

        public IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> WithAttributes(Attributes attributes)
        {
            return new BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>(_module.WithAttributes(attributes).Nest());
        }

        public IGraph<BidiShape<TIn1, TOut1, TIn2, TOut2>, TMat> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }

        /**
         * Turn this BidiFlow around by 180 degrees, logically flipping it upside down in a protocol stack.
         */
        public BidiFlow<TIn2, TOut2, TIn1, TOut1, TMat> Reversed()
        {
            return new BidiFlow<TIn2, TOut2, TIn1, TOut1, TMat>(Module.ReplaceShape(Shape.Reversed()));
        } 

        /**
         * Add the given BidiFlow as the next step in a bidirectional transformation
         * pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
         * layer, the closest to the metal.
         * {{{
         *     +----------------------------+
         *     | Resulting BidiFlow         |
         *     |                            |
         *     |  +------+        +------+  |
         * I1 ~~> |      |  ~O1~> |      | ~~> OO1
         *     |  | this |        | bidi |  |
         * O2 <~~ |      | <~I2~  |      | <~~ II2
         *     |  +------+        +------+  |
         *     +----------------------------+
         * }}}
         * The materialized value of the combined [[BidiFlow]] will be the materialized
         * value of the current flow (ignoring the other BidiFlow’s value), use
         * [[BidiFlow#atopMat atopMat]] if a different strategy is needed.
         */
        public BidiFlow<TIn1, TOut12, TIn21, TOut2, TMat> Atop<TOut12, TIn21, TMat2>(BidiFlow<TOut1, TOut12, TIn21, TIn2, TMat2> bidi)
        {
            return AtopMat(bidi, Keep.Left<TMat, TMat2, TMat>);
        }

        /**
         * Add the given BidiFlow as the next step in a bidirectional transformation
         * pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
         * layer, the closest to the metal.
         * {{{
         *     +----------------------------+
         *     | Resulting BidiFlow         |
         *     |                            |
         *     |  +------+        +------+  |
         * I1 ~~> |      |  ~O1~> |      | ~~> OO1
         *     |  | this |        | bidi |  |
         * O2 <~~ |      | <~I2~  |      | <~~ II2
         *     |  +------+        +------+  |
         *     +----------------------------+
         * }}}
         * The `combine` function is used to compose the materialized values of this flow and that
         * flow into the materialized value of the resulting BidiFlow.
         */
        public BidiFlow<TIn1, TOut12, TIn21, TOut2, TMat> AtopMat<TOut12, TIn21, TMat2, TMat3>(BidiFlow<TOut1, TOut12, TIn21, TIn2, TMat2> bidi, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = bidi.Module.CarbonCopy();
            var ins = copy.Shape.Inlets;
            var outs = copy.Shape.Outlets;

            return new BidiFlow<TIn1, TOut12, TIn21, TOut2, TMat>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlet1, ins[0])
                .Wire(outs[1], Shape.Inlet1)
                .ReplaceShape(new BidiShape<TIn1, TOut12, TIn21, TOut2>((Inlet<TIn1>)Shape.Inlets[0], (Outlet<TOut12>)outs[0], (Inlet<TIn21>)ins[1], (Outlet<TOut2>)Shape.Outlets[1])));
        }

        /**
         * Add the given Flow as the final step in a bidirectional transformation
         * pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
         * layer, the closest to the metal.
         * {{{
         *     +---------------------------+
         *     | Resulting Flow            |
         *     |                           |
         *     |  +------+        +------+ |
         * I1 ~~> |      |  ~O1~> |      | |
         *     |  | this |        | flow | |
         * O2 <~~ |      | <~I2~  |      | |
         *     |  +------+        +------+ |
         *     +---------------------------+
         * }}}
         * The materialized value of the combined [[Flow]] will be the materialized
         * value of the current flow (ignoring the other Flow’s value), use
         * [[BidiFlow#joinMat joinMat]] if a different strategy is needed.
         */
        public Flow<TIn1, TOut2, TMat> Join<TMat2>(Flow<TOut1, TIn2, TMat2> flow)
        {
            return JoinMat(flow, Keep.Left<TMat, TMat2, TMat>);
        }

        /**
         * Add the given Flow as the final step in a bidirectional transformation
         * pipeline. By convention protocol stacks are growing to the left: the right most is the bottom
         * layer, the closest to the metal.
         * {{{
         *     +---------------------------+
         *     | Resulting Flow            |
         *     |                           |
         *     |  +------+        +------+ |
         * I1 ~~> |      |  ~O1~> |      | |
         *     |  | this |        | flow | |
         * O2 <~~ |      | <~I2~  |      | |
         *     |  +------+        +------+ |
         *     +---------------------------+
         * }}}
         * The `combine` function is used to compose the materialized values of this flow and that
         * flow into the materialized value of the resulting [[Flow]].
         */
        public Flow<TIn1, TOut2, TMat> JoinMat<TMat2, TMat3>(Flow<TOut1, TIn2, TMat2> flow, Func<TMat, TMat2, TMat3> combine)
        {
            var copy = flow.Module.CarbonCopy();
            var inlet = copy.Shape.Inlets[0];
            var outlet = copy.Shape.Outlets[0];
            return new Flow<TIn1, TOut2, TMat>(Module
                .Compose(copy, combine)
                .Wire(Shape.Outlets[0], inlet)
                .Wire(outlet, Shape.Inlets[1])
                .ReplaceShape(new FlowShape<TIn1, TOut2>((Inlet<TIn1>)Shape.Inlets[0], (Outlet<TOut2>)Shape.Outlets[1])));
        }
    }
}