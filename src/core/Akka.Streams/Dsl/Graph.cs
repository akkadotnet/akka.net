using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Merge several streams, taking elements as they arrive from input streams
    /// (picking randomly when several have elements ready).
    /// <para>
    /// '''Emits when''' one of the inputs has an element available
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' all upstreams complete
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary>
    public sealed class Merge<T> : GraphStage<UniformFanInShape<T, T>>
    {
        #region graph stage logic

        private sealed class MergeStageLogic : GraphStageLogic
        {
            private readonly Merge<T> _stage;
            private readonly Inlet<T>[] _pendingQueue;

            private int _runningUpstreams;
            private int _pendingHead = 0;
            private int _pendingTail = 0;
            private bool _initialized = false;

            public MergeStageLogic(Shape shape, Merge<T> stage) : base(shape)
            {
                _stage = stage;
                _runningUpstreams = _stage._inputPorts;
                _pendingQueue = new Inlet<T>[_stage._ins.Length];

                var outlet = _stage._out;
                foreach (var inlet in _stage._ins)
                {
                    SetHandler(inlet, onPush: () =>
                    {
                        if (IsAvailable(outlet))
                        {
                            if (!IsPending)
                            {
                                Push(outlet, Grab(inlet));
                                TryPull(inlet);
                            }
                        }
                        else Enqeue(inlet);
                    },
                    onUpstreamFinish: () =>
                    {
                        if (_stage._eagerClose)
                        {
                            foreach (var i in _stage._ins) Cancel(i);
                            _runningUpstreams = 0;
                            if (!IsPending) CompleteStage<T>();
                        }
                        else
                        {
                            _runningUpstreams--;
                            if (IsUpstreamClosed && !IsPending) CompleteStage<T>();
                        }
                    });
                }

                SetHandler(outlet, onPull: () =>
                {
                    if (IsPending) DequeueAndDispatch();
                });
            }

            private bool IsUpstreamClosed { get { return _runningUpstreams == 0; } }
            private bool IsPending { get { return _pendingHead != _pendingTail; } }

            public override void PreStart()
            {
                foreach (var inlet in _stage._ins) TryPull(inlet);
            }

            private void Enqeue(Inlet<T> inlet)
            {
                _pendingQueue[_pendingTail % _stage._inputPorts] = inlet;
                _pendingTail++;
            }

            private void DequeueAndDispatch()
            {
                var inlet = _pendingQueue[_pendingHead % _stage._inputPorts];
                _pendingHead++;
                Push(_stage._out, Grab(inlet));
                if (IsUpstreamClosed && !IsPending) CompleteStage<T>();
                else TryPull(inlet);
            }
        }

        #endregion

        private readonly int _inputPorts;
        private readonly bool _eagerClose;

        private readonly Inlet<T>[] _ins;
        private readonly Outlet<T> _out = new Outlet<T>("Merge.out");

        public Merge(int inputPorts, bool eagerClose = false)
        {
            if (inputPorts <= 1) throw new ArgumentException("Merge must have more than 1 input port");
            _inputPorts = inputPorts;
            _eagerClose = eagerClose;

            _ins = new Inlet<T>[inputPorts];
            for (int i = 0; i < inputPorts; i++)
                _ins[i] = new Inlet<T>("Merge.in" + i);

            Shape = new UniformFanInShape<T, T>(_out, _ins);
            InitialAttributes = Attributes.CreateName("Merge");
        }

        protected override Attributes InitialAttributes { get; }
        public override UniformFanInShape<T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MergeStageLogic(Shape, this);
        }
    }

    /// <summary>
    /// Merge several streams, taking elements as they arrive from input streams
    /// (picking from preferred when several have elements ready).
    /// 
    /// A <see cref="MergePreferred{T}"/> has one `out` port, one `preferred` input port and 0 or more secondary `in` ports.
    /// <para>
    /// '''Emits when''' one of the inputs has an element available, preferring
    /// a specified input if multiple have elements available
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' all upstreams complete (eagerClose=false) or one upstream completes (eagerClose=true)
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// <para>
    /// A `Broadcast` has one `in` port and 2 or more `out` ports.
    /// </para>
    /// </summary>
    public sealed class MergePreferred<T> : GraphStage<MergePreferred<T>.MergePreferredShape>
    {
        #region internal classes

        public sealed class MergePreferredShape : UniformFanInShape<T, T>
        {
            private readonly int _secondaryPorts;
            private readonly IInit _init;

            public MergePreferredShape(int secondaryPorts, IInit init) : base(secondaryPorts, init)
            {
                _secondaryPorts = secondaryPorts;
                _init = init;

                Preferred = NewInlet<T>("preferred");
            }

            public MergePreferredShape(int secondaryPorts, string name) : this(secondaryPorts, new InitName(name)) { }

            protected override FanInShape<T> Construct(IInit init)
            {
                return new MergePreferredShape(_secondaryPorts, init);
            }

            public Inlet<T> Preferred { get; }
        }

        private sealed class MergePreferredStageLogic : GraphStageLogic
        {
            /// <summary>
            /// This determines the unfairness of the merge:
            /// - at 1 the preferred will grab 40% of the bandwidth against three equally fast secondaries
            /// - at 2 the preferred will grab almost all bandwidth against three equally fast secondaries
            /// (measured with eventLimit=1 in the GraphInterpreter, so may not be accurate)
            /// </summary>
            public const int MaxEmitting = 2;
            private readonly MergePreferred<T> _stage;
            private readonly Action[] _pullMe;
            private int _openInputs;
            private int _preferredEmitting = 0;
            private bool _isFirst = true;

            public MergePreferredStageLogic(Shape shape, MergePreferred<T> stage) : base(shape)
            {
                _stage = stage;
                _openInputs = stage._secondaryPorts + 1;
                _pullMe = new Action[stage._secondaryPorts];
                for (int i = 0; i < stage._secondaryPorts; i++)
                {
                    var inlet = stage.In(i);
                    _pullMe[i] = () => TryPull(inlet);
                }

                SetHandler(_stage.Out, onPull: () =>
                {
                    if (_isFirst)
                    {
                        _isFirst = false;
                        TryPull(_stage.Preferred);
                        foreach (var inlet in _stage.Shape.Inlets.Cast<Inlet<T>>())
                            TryPull(inlet);
                    }
                });

                SetHandler(_stage.Preferred,
                    onUpstreamFinish: OnComplete,
                    onPush: () =>
                    {
                        if (_preferredEmitting == MaxEmitting) { /* blocked */ }
                        else EmitPreferred();
                    });

                for (int i = 0; i < stage._secondaryPorts; i++)
                {
                    var port = stage.In(i);
                    var pullPort = _pullMe[i];

                    SetHandler(port, onPush: () =>
                    {
                        if (_preferredEmitting > 0) { /* blocked */ }
                        else Emit(_stage.Out, Grab(port), pullPort);
                    },
                    onUpstreamFinish: OnComplete);
                }
            }

            private void OnComplete()
            {
                _openInputs--;
                if (_stage._eagerClose || _openInputs == 0) CompleteStage<T>();
            }

            private void EmitPreferred()
            {
                _preferredEmitting++;
                Emit(_stage.Out, Grab(_stage.Preferred), Emitted);
                TryPull(_stage.Preferred);
            }

            private void Emitted()
            {
                _preferredEmitting--;
                if (IsAvailable(_stage.Preferred)) EmitPreferred();
                else if (_preferredEmitting == 0) EmitSecondary();
            }

            private void EmitSecondary()
            {
                for (int i = 0; i < _stage._secondaryPorts; i++)
                {
                    var port = _stage.In(i);
                    if (IsAvailable(port)) Emit(_stage.Out, Grab(port), _pullMe[i]);
                }
            }
        }

        #endregion

        private readonly int _secondaryPorts;
        private readonly bool _eagerClose;

        public MergePreferred(int secondaryPorts, bool eagerClose = false)
        {
            if (secondaryPorts < 1) throw new ArgumentException("A MergePreferred must have at least one secondary port");
            _secondaryPorts = secondaryPorts;
            _eagerClose = eagerClose;

            Shape = new MergePreferredShape(_secondaryPorts, "MergePreferred");
            InitialAttributes = Attributes.CreateName("MergePreferred");
        }

        protected override Attributes InitialAttributes { get; }
        public override MergePreferredShape Shape { get; }

        public Inlet<T> In(int id)
        {
            return Inlet.Create<T>(Shape.Inlets.ElementAt(id));
        }

        public Outlet<T> Out { get { return Shape.Out; } }
        public Inlet<T> Preferred { get { return Shape.Preferred; } }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MergePreferredStageLogic(Shape, this);
        }
    }

    /// <summary>
    /// Interleave represents deterministic merge which takes N elements per input stream,
    /// in-order of inputs, emits them downstream and then cycles/"wraps-around" the inputs.
    /// <para>
    /// '''Emits when''' element is available from current input (depending on phase)
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' all upstreams complete (eagerClose=false) or one upstream completes (eagerClose=true)
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary> 
    public sealed class Interleave<T> : GraphStage<UniformFanInShape<T, T>>
    {
        #region stage logic
        private sealed class InterleaveStageLogic : GraphStageLogic
        {
            private readonly Interleave<T> _stage;
            private int _counter = 0;
            private int _currentUpstreamIndex = 0;
            private int _runningUpstreams;

            public InterleaveStageLogic(Shape shape, Interleave<T> stage) : base(shape)
            {
                _stage = stage;
                _runningUpstreams = _stage._inputPorts;

                foreach (var inlet in _stage.Inlets)
                {
                    SetHandler(inlet, onPush: () =>
                    {
                        Push(_stage.Out, Grab(inlet));
                        _counter++;
                        if (_counter == _stage._segmentSize) SwitchToNextInput();
                    },
                    onUpstreamFinish: () =>
                    {
                        if (!_stage._eagerClose)
                        {
                            _runningUpstreams--;
                            if (!IsUpstreamClosed)
                            {
                                if (Equals(inlet, CurrentUpstream))
                                {
                                    SwitchToNextInput();
                                    if (IsAvailable(_stage.Out)) Pull(CurrentUpstream);
                                }
                            }
                            else CompleteStage<T>();
                        }
                        else CompleteStage<T>();
                    });
                }

                SetHandler(_stage.Out, onPull: () =>
                {
                    if (!HasBeenPulled(CurrentUpstream)) TryPull(CurrentUpstream);
                });
            }

            private bool IsUpstreamClosed { get { return _runningUpstreams == 0; } }
            private Inlet<T> CurrentUpstream { get { return _stage.Inlets[_currentUpstreamIndex]; } }

            private void SwitchToNextInput()
            {
                _counter = 0;
                var index = _currentUpstreamIndex;
                while (true)
                {
                    var successor = (index + 1) % _stage._inputPorts;
                    if (!IsClosed(_stage.Inlets[successor])) _currentUpstreamIndex = successor;
                    else
                    {
                        if (successor != _currentUpstreamIndex)
                        {
                            index = successor;
                            continue;
                        }
                        else
                        {
                            CompleteStage<T>();
                            _currentUpstreamIndex = 0;
                        }
                    }

                    break;
                }
            }
        }
        #endregion

        private readonly int _inputPorts;
        private readonly int _segmentSize;
        private readonly bool _eagerClose;

        public Interleave(int inputPorts, int segmentSize, bool eagerClose = false)
        {
            if (inputPorts <= 1) throw new ArgumentException("Interleave input ports count must be greater than 1", "inputPorts");
            if (segmentSize <= 0) throw new ArgumentException("Interleave segment size must be greater than 0", "segmentSize");

            _inputPorts = inputPorts;
            _segmentSize = segmentSize;
            _eagerClose = eagerClose;

            Out = new Outlet<T>("Interleave.out");
            Inlets = new Inlet<T>[inputPorts];
            for (int i = 0; i < inputPorts; i++)
                Inlets[i] = new Inlet<T>("Interleave.in" + i);

            Shape = new UniformFanInShape<T, T>(Out, Inlets);
        }

        public Outlet<T> Out { get; }
        public Inlet<T>[] Inlets { get; }

        public override UniformFanInShape<T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new InterleaveStageLogic(Shape, this);
        }
    }

    /// <summary>
    /// Merge two pre-sorted streams such that the resulting stream is sorted.
    /// <para>
    /// '''Emits when''' both inputs have an element available
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' all upstreams complete
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary>
    public sealed class MergeSorted<T> : GraphStage<FanInShape<T, T, T>> where T : IComparable<T>
    {
        #region stage logic
        private sealed class MergeSortedStageLogic : GraphStageLogic
        {
            private readonly MergeSorted<T> _stage;
            private T _other;

            readonly Action<T> DispatchRight;
            readonly Action<T> DispatchLeft;
            readonly Action PassRight;
            readonly Action PassLeft;
            readonly Action ReadRight;
            readonly Action ReadLeft;

            public MergeSortedStageLogic(Shape shape, MergeSorted<T> stage) : base(shape)
            {
                _stage = stage;
                DispatchRight = right => Dispatch(_other, right);
                DispatchLeft = left => Dispatch(left, _other);
                PassRight = () => Emit(_stage.Out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage.Right, _stage.Out, doPull: true);
                });
                PassLeft = () => Emit(_stage.Out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage.Left, _stage.Out, doPull: true);
                });
                ReadRight = () => Read(_stage.Right, DispatchRight, PassLeft);
                ReadLeft = () => Read(_stage.Left, DispatchLeft, PassRight);

                SetHandler(_stage.Left, IgnoreTerminateInput);
                SetHandler(_stage.Right, IgnoreTerminateInput);
                SetHandler(_stage.Out, EagerTerminateOutput);
            }

            public override void PreStart()
            {
                // all fan-in stages need to eagerly pull all inputs to get cycles started
                Pull(_stage.Right);
                Read(_stage.Left, left =>
                {
                    _other = left;
                },
                () => PassAlong(_stage.Right, _stage.Out));
            }

            private void NullOut()
            {
                _other = default(T);
            }

            private void Dispatch(T left, T right)
            {
                if (left.CompareTo(right) == -1)
                {
                    _other = right;
                    Emit(_stage.Out, left, ReadLeft);
                }
                else
                {
                    _other = left;
                    Emit(_stage.Out, right, ReadRight);
                }
            }
        }
        #endregion

        public readonly Inlet<T> Left = new Inlet<T>("left");
        public readonly Inlet<T> Right = new Inlet<T>("right");
        public readonly Outlet<T> Out = new Outlet<T>("out");

        public MergeSorted()
        {
            Shape = new FanInShape<T, T, T>(Left, Right, Out);
        }

        public override FanInShape<T, T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new MergeSortedStageLogic(Shape, this);
        }
    }

    /// <summary>
    /// Fan-out the stream to several streams emitting each incoming upstream element to all downstream consumers.
    /// It will not shut down until the subscriptions for at least two downstream subscribers have been established.
    /// <para>
    /// '''Emits when''' all of the outputs stops backpressuring and there is an input element available
    /// </para>
    /// '''Backpressures when''' any of the outputs backpressure
    /// <para>
    /// '''Completes when''' upstream completes
    /// </para>
    /// '''Cancels when''' If eagerCancel is enabled: when any downstream cancels; otherwise: when all downstreams cancel
    /// </summary>
    public sealed class Broadcast<T> : GraphStage<UniformFanOutShape<T, T>>
    {
        private readonly int _outputPorts;
        private readonly bool _eagerCancel;

        public readonly Inlet<T> In = new Inlet<T>("Broadcast.in");
        public readonly Outlet<T>[] Out;

        public Broadcast(int outputPorts, bool eagerCancel = false)
        {
            if (outputPorts <= 1) throw new ArgumentException("Broadcast require more than 1 output port", "outputPorts");
            _outputPorts = outputPorts;
            _eagerCancel = eagerCancel;

            Out = new Outlet<T>[outputPorts];
            for (int i = 0; i < outputPorts; i++)
                Out[i] = new Outlet<T>("Broadcast.out" + i);

            Shape = new UniformFanOutShape<T, T>();

            InitialAttributes = Attributes.CreateName("Broadcast");
        }

        protected override Attributes InitialAttributes { get; }
        public override UniformFanOutShape<T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            throw new NotImplementedException();
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
            var shape = new UniformFanOutShape<T, T>(outputPorts);
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
    public sealed partial class ZipWith
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
    public sealed partial class UnzipWith
    {
        public static readonly UnzipWith Instance = new UnzipWith();
        private UnzipWith() { }
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

    public static class GraphDsl
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