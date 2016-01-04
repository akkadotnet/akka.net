using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Stages;

namespace Akka.Streams.Dsl
{
    /**
     * Merge several streams, taking elements as they arrive from input streams
     * (picking randomly when several have elements ready).
     *
     * '''Emits when''' one of the inputs has an element available
     *
     * '''Backpressures when''' downstream backpressures
     *
     * '''Completes when''' all upstreams complete
     *
     * '''Cancels when''' downstream cancels
     */
    public class Merge<T> : IGraph<UniformFanInShape<T, T>, object>
    {
        /**
         * Create a new `Merge` with the specified number of input ports.
         *
         * @param inputPorts number of input ports
         */
        public static Merge<T> Create(int inputPorts)
        {
            var shape = new UniformFanInShape<T,T>(inputPorts);
            return new Merge<T>(inputPorts, shape, new Junctions.MergeModule<T>(shape, Attributes.CreateName("Merge")));
        }

        public readonly int InputPorts;
        private readonly UniformFanInShape<T, T> _shape;
        private readonly IModule _module;

        public Merge(int inputPorts, UniformFanInShape<T, T> shape, IModule module)
        {
            InputPorts = inputPorts;
            _shape = shape;
            _module = module;
        }

        public UniformFanInShape<T, T> Shape { get { return _shape; } }
        public IModule Module { get { return _module; } }
        public IGraph<UniformFanInShape<T, T>, object> WithAttributes(Attributes attributes)
        {
            return new Merge<T>(InputPorts, _shape, _module.WithAttributes(attributes).Nest());
        }

        public IGraph<UniformFanInShape<T, T>, object> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }

    /**
     * Merge several streams, taking elements as they arrive from input streams
     * (picking from preferred when several have elements ready).
     *
     * A `MergePreferred` has one `out` port, one `preferred` input port and 0 or more secondary `in` ports.
     *
     * '''Emits when''' one of the inputs has an element available, preferring
     * a specified input if multiple have elements available
     *
     * '''Backpressures when''' downstream backpressures
     *
     * '''Completes when''' all upstreams complete
     *
     * '''Cancels when''' downstream cancels
     *
     * A `Broadcast` has one `in` port and 2 or more `out` ports.
     */
    public class MergePreffered<T> : IGraph<MergePreffered<T>.MergePrefferedShape, object>
    {
        /**
         * Create a new `MergePreferred` with the specified number of secondary input ports.
         *
         * @param secondaryPorts number of secondary input ports
         */
        public static MergePreffered<T> Create(int secondaryPorts)
        {
            var shape = new MergePrefferedShape(secondaryPorts, "MergePreferred");
            return new MergePreffered<T>(secondaryPorts, shape, new Junctions.MergePreferredModule<T>(shape, Attributes.CreateName("MergePreferred")));
        }

        public class MergePrefferedShape : UniformFanInShape<T, T>
        {
            public readonly Inlet<T> Preferred;

            public MergePrefferedShape(int n, IInit init) : base(n, init)
            {
                Preferred = NewInlet<T>("preferred");
            }
            public MergePrefferedShape(int n, string name) : this(n, new InitName(name)) { }

            protected override FanInShape<T> Construct(IInit init)
            {
                return new MergePrefferedShape(N, init);
            }
        }

        public readonly int SecondaryPorts;
        private readonly MergePrefferedShape _shape;
        private readonly IModule _module;

        public MergePreffered(int secondaryPorts, MergePrefferedShape shape, IModule module)
        {
            SecondaryPorts = secondaryPorts;
            _shape = shape;
            _module = module;
        }

        public MergePrefferedShape Shape { get { return _shape; } }
        public IModule Module { get { return _module; } }
        public IGraph<MergePrefferedShape, object> WithAttributes(Attributes attributes)
        {
            return new MergePreffered<T>(SecondaryPorts, _shape, _module.WithAttributes(attributes).Nest());
        }

        public IGraph<MergePrefferedShape, object> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }

    /**
     * Fan-out the stream to several streams emitting each incoming upstream element to all downstream consumers.
     * It will not shut down until the subscriptions for at least two downstream subscribers have been established.
     *
     * '''Emits when''' all of the outputs stops backpressuring and there is an input element available
     *
     * '''Backpressures when''' any of the outputs backpressure
     *
     * '''Completes when''' upstream completes
     *
     * '''Cancels when'''
     *   If eagerCancel is enabled: when any downstream cancels; otherwise: when all downstreams cancel
     *
     */
    public class Broadcast<T> : IGraph<UniformFanOutShape<T, T>, object>
    {
        /**
         * Create a new `Broadcast` with the specified number of output ports.
         *
         * @param outputPorts number of output ports
         * @param eagerCancel if true, broadcast cancels upstream if any of its downstreams cancel.
         */
        public static Broadcast<T> Create(int outputPorts, bool eagerCancel = false)
        {
            var shape = new UniformFanOutShape<T, T>(outputPorts);
            return new Broadcast<T>(outputPorts, shape, new Junctions.BroadcastModule<T>(shape, eagerCancel, Attributes.CreateName("Broadcast")));
        }

        public readonly int OutputPorts;
        private readonly UniformFanOutShape<T, T> _shape;
        private readonly IModule _module;

        private Broadcast(int outputPorts, UniformFanOutShape<T, T> shape, IModule module)
        {
            OutputPorts = outputPorts;
            _shape = shape;
            _module = module;
        }

        public UniformFanOutShape<T, T> Shape { get { return _shape; } }
        public IModule Module { get { return _module; } }
        public IGraph<UniformFanOutShape<T, T>, object> WithAttributes(Attributes attributes)
        {
            return new Broadcast<T>(OutputPorts, _shape, _module.WithAttributes(attributes).Nest());
        }

        public IGraph<UniformFanOutShape<T, T>, object> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }

    /**
     * Fan-out the stream to several streams. Each upstream element is emitted to the first available downstream consumer.
     * It will not shut down until the subscriptions
     * for at least two downstream subscribers have been established.
     *
     * A `Balance` has one `in` port and 2 or more `out` ports.
     *
     * '''Emits when''' any of the outputs stops backpressuring; emits the element to the first available output
     *
     * '''Backpressures when''' all of the outputs backpressure
     *
     * '''Completes when''' upstream completes
     *
     * '''Cancels when''' all downstreams cancel
     */
    public class Balance<T> : IGraph<UniformFanOutShape<T, T>, object>
    {
        /**
         * Create a new `Balance` with the specified number of output ports.
         *
         * @param outputPorts number of output ports
         * @param waitForAllDownstreams if you use `waitForAllDownstreams = true` it will not start emitting
         *   elements to downstream outputs until all of them have requested at least one element,
         *   default value is `false`
         */
        public static Balance<T> Create(int outputPorts, bool waitForAllDownstreams = false)
        {
            var shape = new UniformFanOutShape<T,T>(outputPorts);
            return new Balance<T>(outputPorts, waitForAllDownstreams, shape, new Junctions.BalanceModule<T>(shape, waitForAllDownstreams, Attributes.CreateName("Balance")));
        }

        public readonly int OutputPorts;
        public readonly bool WaitForAllDownstreams;
        private readonly UniformFanOutShape<T, T> _shape;
        private readonly IModule _module;

        public Balance(int outputPorts, bool waitForAllDownstreams, UniformFanOutShape<T, T> shape, IModule module)
        {
            OutputPorts = outputPorts;
            WaitForAllDownstreams = waitForAllDownstreams;
            _shape = shape;
            _module = module;
        }

        public UniformFanOutShape<T, T> Shape { get { return _shape; } }
        public IModule Module { get { return _module; } }
        public IGraph<UniformFanOutShape<T, T>, object> WithAttributes(Attributes attributes)
        {
            return new Balance<T>(OutputPorts, WaitForAllDownstreams, _shape, _module.WithAttributes(attributes).Nest());
        }

        public IGraph<UniformFanOutShape<T, T>, object> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }

    /**
     * Combine the elements of 2 streams into a stream of tuples.
     *
     * A `Zip` has a `left` and a `right` input port and one `out` port
     *
     * '''Emits when''' all of the inputs has an element available
     *
     * '''Backpressures when''' downstream backpressures
     *
     * '''Completes when''' any upstream completes
     *
     * '''Cancels when''' downstream cancels
     */
    public class Zip<T1, T2>
    {

    }

    /**
     * Combine the elements of multiple streams into a stream of combined elements using a combiner function.
     *
     * '''Emits when''' all of the inputs has an element available
     *
     * '''Backpressures when''' downstream backpressures
     *
     * '''Completes when''' any upstream completes
     *
     * '''Cancels when''' downstream cancels
     */
    public sealed class ZipWith
    {
        public static readonly ZipWith Instance = new ZipWith();
        private ZipWith() { }
    }

    /**
     * Takes a stream of pair elements and splits each pair to two output streams.
     *
     * An `Unzip` has one `in` port and one `left` and one `right` output port.
     *
     * '''Emits when''' all of the outputs stops backpressuring and there is an input element available
     *
     * '''Backpressures when''' any of the outputs backpressures
     *
     * '''Completes when''' upstream completes
     *
     * '''Cancels when''' any downstream cancels
     */
    public class UnZip<T1, T2>
    {

    }

    /**
     * Transforms each element of input stream into multiple streams using a splitter function.
     *
     * '''Emits when''' all of the outputs stops backpressuring and there is an input element available
     *
     * '''Backpressures when''' any of the outputs backpressures
     *
     * '''Completes when''' upstream completes
     *
     * '''Cancels when''' any downstream cancels
     */
    public sealed class UnZipWith
    {
        public static readonly UnZipWith Instance = new UnZipWith();
        private UnZipWith() { }
    }

    /**
     * Takes two streams and outputs one stream formed from the two input streams
     * by first emitting all of the elements from the first stream and then emitting
     * all of the elements from the second stream.
     *
     * A `Concat` has one `first` port, one `second` port and one `out` port.
     *
     * '''Emits when''' the current stream has an element available; if the current input completes, it tries the next one
     *
     * '''Backpressures when''' downstream backpressures
     *
     * '''Completes when''' all upstreams complete
     *
     * '''Cancels when''' downstream cancels
     */
    public class Concat<T> : IGraph<UniformFanInShape<T, T>, object>
    {
        /**
         * Create a new `Concat`.
         */
        public static Concat<T> Create()
        {
            var shape = new UniformFanInShape<T, T>(2);   
            return new Concat<T>(shape, new Junctions.ConcatModule<T>(shape, Attributes.CreateName("Concat")));
        }

        private readonly UniformFanInShape<T, T> _shape;
        private readonly IModule _module;

        private Concat(UniformFanInShape<T, T> shape, IModule module)
        {
            _shape = shape;
            _module = module;
        }

        public UniformFanInShape<T, T> Shape { get { return _shape; } }
        public IModule Module { get { return _module; } }
        public IGraph<UniformFanInShape<T, T>, object> WithAttributes(Attributes attributes)
        {
            return new Concat<T>(_shape, _module.WithAttributes(attributes).Nest());
        }

        public IGraph<UniformFanInShape<T, T>, object> Named(string name)
        {
            return WithAttributes(Attributes.CreateName(name));
        }
    }

    public static class FlowGraph
    {
        public class Builder<T>
        {
            private IModule _moduleInProgress = EmptyModule.Instance;

            internal protected IModule Module { get { return _moduleInProgress; } }

            public void AddEdge<T1, T2, TMat2>(Outlet<T1> from, IGraph<FlowShape<T1, T2>, TMat2> via, Inlet<T2> to)
            {
                throw new NotImplementedException();
            }

            public void AddEdge<T>(Outlet<T> from, Inlet<T> to)
            {
                _moduleInProgress = _moduleInProgress.Wire(from, to);
            }

            /**
             * Import a graph into this module, performing a deep copy, discarding its
             * materialized value and returning the copied Ports that are now to be
             * connected.
             */
            public TShape Add<TShape, TMat>(IGraph<TShape, TMat> graph)
                where TShape : Shape
            {
                throw new NotImplementedException();
            }

            public Outlet<TOut> Add<TOut, TMat>(Source<TOut, TMat> source)
            {
                return Add(source as IGraph<SourceShape<TOut>, TMat>).Outlets[0] as Outlet<TOut>;
            }

            public Inlet<TIn> Add<TIn, TMat>(Sink<TIn, TMat> source)
            {
                return Add(source as IGraph<SinkShape<TIn>, TMat>).Inlets[0] as Inlet<TIn>;
            }

            /**
             * Returns an [[Outlet]] that gives access to the materialized value of this graph. Once the graph is materialized
             * this outlet will emit exactly one element which is the materialized value. It is possible to expose this
             * outlet as an externally accessible outlet of a [[Source]], [[Sink]], [[Flow]] or [[BidiFlow]].
             *
             * It is possible to call this method multiple times to get multiple [[Outlet]] instances if necessary. All of
             * the outlets will emit the materialized value.
             *
             * Be careful to not to feed the result of this outlet to a stage that produces the materialized value itself (for
             * example to a [[Sink#fold]] that contributes to the materialized value) since that might lead to an unresolvable
             * dependency cycle.
             *
             * @return The outlet that will emit the materialized value.
             */
            public Outlet<T> MaterializedValue
            {
                get
                {
                    var module = new MaterializedValueSource<object>();
                    _moduleInProgress = _moduleInProgress.Compose(module);
                    return Outlet.Create<T>(module.Shape.Outlets[0]);
                }
            }

            /**
             * INTERNAL API.
             *
             * This is only used by the materialization-importing apply methods of Source,
             * Flow, Sink and Graph.
             */
            internal TShape Add<TShape, T1, TMat>(IGraph<TShape, TMat> graph, Func<T1, object> transform)
                where TShape : Shape
            {
                throw new NotImplementedException();
            }

            /**
             * INTERNAL API.
             *
             * This is only used by the materialization-importing apply methods of Source,
             * Flow, Sink and Graph.
             */
            internal TShape Add<TShape, T1, T2, TMat>(IGraph<TShape, TMat> graph, Func<T1, T2, object> combine)
                where TShape : Shape
            {
                throw new NotImplementedException();
            }

            internal void AndThen(OutPort port, StageModule op)
            {
                _moduleInProgress = _moduleInProgress.Compose(op).Wire(port, op.InPorts.First());
            }

            internal IRunnableGraph<TMat> BuildRunnable<TMat>()
            {
                if (!_moduleInProgress.IsRunnable)
                    throw new ArgumentException(string.Format("Cannot build the RunnableGraph because there are unconnected ports: " +
                        string.Join(", ", _moduleInProgress.InPorts.Cast<object>().Union(_moduleInProgress.OutPorts))));

                return new RunnableGraph<TMat>(_moduleInProgress.Nest());
            }

            internal Source<TOut, TMat> BuildSource<TOut, TMat>(Outlet<TOut> outlet)
            {
                if (_moduleInProgress.IsRunnable)
                    throw new ArgumentException("Cannot build the Source since no ports remain open");
                if (!_moduleInProgress.IsSource)
                    throw new ArgumentException(string.Format("Cannot build Source with open inputs [{0}] and outputs [{1}]",
                        string.Join(", ", _moduleInProgress.InPorts), string.Join(", ", _moduleInProgress.OutPorts)));
                if (_moduleInProgress.OutPorts.First() != outlet)
                    throw new ArgumentException(string.Format("Provided Outlet [{0}] does not equal the module’s open Outlet [{1}]", outlet, _moduleInProgress.OutPorts.First()));

                return new Source<TOut, TMat>(_moduleInProgress.ReplaceShape(new SourceShape<TOut>(outlet)).Nest());
            }

            internal Sink<TIn, TMat> BuildSink<TIn, TMat>(Inlet<TIn> inlet)
            {
                if (_moduleInProgress.IsRunnable)
                    throw new ArgumentException("Cannot build the Sink since no ports remain open");
                if (!_moduleInProgress.IsSink)
                    throw new ArgumentException(string.Format("Cannot build Sink with open inputs [{0}] and outputs [{1}]",
                        string.Join(", ", _moduleInProgress.InPorts), string.Join(", ", _moduleInProgress.OutPorts)));
                if (_moduleInProgress.InPorts.First() != inlet)
                    throw new ArgumentException(string.Format("Provided Inlet [{0}] does not equal the module’s open Inlet [{1}]", inlet, _moduleInProgress.InPorts.First()));

                return new Sink<TIn, TMat>(_moduleInProgress.ReplaceShape(new SinkShape<TIn>(inlet)).Nest());
            }

            internal Flow<TIn, TOut, TMat> BuildFlow<TIn, TOut, TMat>(Inlet<TIn> inlet, Outlet<TOut> outlet)
            {
                if (!_moduleInProgress.IsFlow)
                    throw new ArgumentException(string.Format(
                        "Cannot build Flow with open inputs [{0}] and outputs [{1}]", string.Join(", ", _moduleInProgress.InPorts), string.Join(", ", _moduleInProgress.OutPorts)));
                if (_moduleInProgress.OutPorts.First() != outlet)
                    throw new ArgumentException(string.Format("provided Outlet {0} does not equal the module’s open Outlet {1}", outlet, _moduleInProgress.OutPorts.First()));
                if (_moduleInProgress.InPorts.First() != inlet)
                    throw new ArgumentException(string.Format("provided Inlet {0} does not equal the module’s open Inlet {1}", inlet, _moduleInProgress.InPorts.First()));

                return new Flow<TIn, TOut, TMat>(_moduleInProgress.ReplaceShape(new FlowShape<TIn, TOut>(inlet, outlet)).Nest());
            }

            internal BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat> BuildBidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>(
                BidiShape<TIn1, TOut1, TIn2, TOut2> shape)
            {
                if (!_moduleInProgress.IsBidiFlow)
                    throw new ArgumentException(string.Format("Cannot build BidiFlow with open inputs [{0}] and outputs [{1}]", 
                        string.Join(", ", _moduleInProgress.InPorts), string.Join(", ", _moduleInProgress.OutPorts)));

                var i1 = new SortedSet<InPort>(_moduleInProgress.InPorts);
                var i2 = new SortedSet<InPort>(shape.Inlets);
                var o1 = new SortedSet<OutPort>(_moduleInProgress.OutPorts);
                var o2 = new SortedSet<OutPort>(shape.Outlets);

                if (!i1.SetEquals(i2))
                    throw new ArgumentException(string.Format("Provided inlets [{0}] does not equal the module’s open Inlets [{1}]", string.Join(", ", i2), string.Join(", ", i1)));

                if (!o1.SetEquals(o2))
                    throw new ArgumentException(string.Format("Provided outlets [{0}] does not equal the module’s open Outlets [{1}]", string.Join(", ", o2), string.Join(", ", o1)));

                return new BidiFlow<TIn1, TOut1, TIn2, TOut2, TMat>(_moduleInProgress.ReplaceShape(shape).Nest());
            }
        }

        internal static Outlet<TOut> FindOut<TIn, TOut, T>(Builder<T> builder, UniformFanOutShape<TIn, TOut> junction, int n)
        {
            throw new NotImplementedException();
        }

        internal static Inlet<TIn> FindIn<TIn, TOut, T>(Builder<T> builder, UniformFanInShape<TIn, TOut> junction, int n)
        {
            throw new NotImplementedException();
        }

        public interface ICombiner<T>
        {
            Outlet<T> ImportAndGetPort<TBuilder>(Builder<TBuilder> builder);
            void LinkTo(Inlet<T> inlet);
        }

        public interface IReverseCombiner<T>
        {
            Inlet<T> ImportAndGetPortReverse<TBuilder>(Builder<TBuilder> builder);
            void LinkFrom(Outlet<T> outlet);
        }

        public class PortOps<TOut, TMat> : FlowBase<TOut, TMat>, ICombiner<TOut>
        {

        }

        public class DisabledPortOps<TOut, TMat> : PortOps<TOut, TMat>
        {

        }

        public class ReversePortOps<TIn> : IReverseCombiner<TIn>
        {

        }

        public class DisabledReversePortOps<TIn> : ReversePortOps<TIn>
        {

        }

        public class FanInOps<TIn, TOut> : ICombiner<TOut>, IReverseCombiner<TIn>
        {

        }

        public class FanOutOps<TIn, TOut> : IReverseCombiner<TIn>
        {

        }

        public class SinkArrow<T> : IReverseCombiner<T>
        {
        }

        public class SinkShapeArrow<T> : IReverseCombiner<T>
        {

        }

        public class FlowShapeArrow<TIn, TOut> : IReverseCombiner<TIn>
        {

        }

        public class FlowArrow<TIn, TOut, TMat>
        {

        }

        public class BidiFlowShapeArrow<TIn1, TOut1, TIn2, TOut2>
        {

        }
    }
}