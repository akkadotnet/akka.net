using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Util.Internal;

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
    public class Merge<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region graph stage logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly Merge<TIn, TOut> _stage;
            private readonly FixedSizeBuffer<Inlet<TIn>> _pendingQueue;
            private int _runningUpstreams;

            public Logic(Shape shape, Merge<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                _runningUpstreams = _stage._inputPorts;
                _pendingQueue = FixedSizeBuffer.Create<Inlet<TIn>>(_stage._inputPorts);

                var outlet = _stage.Out;
                foreach (var inlet in _stage.In)
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
                        else _pendingQueue.Enqueue(inlet);
                    },
                    onUpstreamFinish: () =>
                    {
                        if (_stage._eagerComplete)
                        {
                            foreach (var i in _stage.In) Cancel(i);
                            _runningUpstreams = 0;
                            if (!IsPending) CompleteStage();
                        }
                        else
                        {
                            _runningUpstreams--;
                            if (AreUpstreamsClosed && !IsPending) CompleteStage();
                        }
                    });
                }

                SetHandler(outlet, onPull: () =>
                {
                    if (IsPending) DequeueAndDispatch();
                });
            }

            private bool AreUpstreamsClosed => _runningUpstreams == 0;
            private bool IsPending => _pendingQueue.NonEmpty;

            public override void PreStart()
            {
                foreach (var inlet in _stage.In) TryPull(inlet);
            }

            private void DequeueAndDispatch()
            {
                var inlet = _pendingQueue.Dequeue();
                Push(_stage.Out, Grab(inlet));
                if (AreUpstreamsClosed && !IsPending) CompleteStage();
                else TryPull(inlet);
            }
        }

        #endregion

        private readonly int _inputPorts;
        private readonly bool _eagerComplete;

        public ImmutableArray<Inlet<TIn>> In { get; }
        public Outlet<TOut> Out { get; }

        public Merge(int inputPorts, bool eagerComplete = false)
        {
            // one input might seem counter intuitive but saves us from special handling in other places
            if (inputPorts < 1) throw new ArgumentException("Merge must have one or more input ports");
            _inputPorts = inputPorts;
            _eagerComplete = eagerComplete;

            var ins = ImmutableArray<Inlet<TIn>>.Empty.ToBuilder();
            for (int i = 0; i < inputPorts; i++)
                ins.Add(new Inlet<TIn>("Merge.in" + i));
            In = ins.ToImmutable();
            Out = new Outlet<TOut>("Merge.out");
                 
            Shape = new UniformFanInShape<TIn, TOut>(Out, In.ToArray());
            InitialAttributes = Attributes.CreateName("Merge");
        }

        protected override Attributes InitialAttributes { get; }
        public override UniformFanInShape<TIn, TOut> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }

        public override string ToString()
        {
            return "Merge";
        }
    }

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
    public sealed class Merge<T> : Merge<T, T>
    {
        public Merge(int inputPorts, bool eagerComplete = false) : base(inputPorts, eagerComplete)
        {
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
    /// '''Completes when''' all upstreams complete (eagerComplete=false) or one upstream completes (eagerComplete=true)
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

            public MergePreferredShape(int secondaryPorts, IInit init) : base(secondaryPorts, init)
            {
                _secondaryPorts = secondaryPorts;

                Preferred = NewInlet<T>("preferred");
            }

            public MergePreferredShape(int secondaryPorts, string name) : this(secondaryPorts, new InitName(name)) { }

            protected override FanInShape<T> Construct(IInit init)
            {
                return new MergePreferredShape(_secondaryPorts, init);
            }

            public Inlet<T> Preferred { get; }
        }

        private sealed class Logic : GraphStageLogic
        {
            /// <summary>
            /// This determines the unfairness of the merge:
            /// - at 1 the preferred will grab 40% of the bandwidth against three equally fast secondaries
            /// - at 2 the preferred will grab almost all bandwidth against three equally fast secondaries
            /// (measured with eventLimit=1 in the GraphInterpreter, so may not be accurate)
            /// </summary>
            private const int MaxEmitting = 2;
            private readonly MergePreferred<T> _stage;
            private readonly Action[] _pullMe;
            private int _openInputs;
            private int _preferredEmitting;

            public Logic(Shape shape, MergePreferred<T> stage) : base(shape)
            {
                _stage = stage;
                _openInputs = stage._secondaryPorts + 1;
                _pullMe = new Action[stage._secondaryPorts];
                for (var i = 0; i < stage._secondaryPorts; i++)
                {
                    var inlet = stage.In(i);
                    _pullMe[i] = () => TryPull(inlet);
                }

                SetHandler(_stage.Out, EagerTerminateOutput);

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
                if (_stage._eagerClose || _openInputs == 0) CompleteStage();
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

            public override void PreStart()
            {
                TryPull(_stage.Preferred);
                _stage.Shape.Ins.ForEach(TryPull);
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

        public Outlet<T> Out => Shape.Out;
        public Inlet<T> Preferred => Shape.Preferred;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }
    }

    public static class Interleave
    {
        public static IGraph<UniformFanInShape<T, T>, Unit> Create<T>(int inputPorts, int segmentSize,
            bool eagerClose = false)
        {
            return GraphStages.WithDetachedInputs<T, Unit>(new Interleave<T, T>(inputPorts, segmentSize, eagerClose));
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
    /// '''Completes when''' all upstreams complete (eagerComplete=false) or one upstream completes (eagerComplete=true)
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary> 
    public sealed class Interleave<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic

        private sealed class InterleaveStageLogic : GraphStageLogic
        {
            private readonly Interleave<TIn, TOut> _stage;
            private int _counter;
            private int _currentUpstreamIndex;
            private int _runningUpstreams;

            public InterleaveStageLogic(Shape shape, Interleave<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                _runningUpstreams = _stage._inputPorts;

                foreach (var inlet in _stage.Inlets)
                {
                    SetHandler(inlet, onPush: () =>
                    {
                        Push(_stage.Out, Grab(inlet));
                        _counter++;
                        if (_counter == _stage._segmentSize)
                            SwitchToNextInput();
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
                                    if (IsAvailable(_stage.Out))
                                        Pull(CurrentUpstream);
                                }
                            }
                            else CompleteStage();
                        }
                        else CompleteStage();
                    });
                }

                SetHandler(_stage.Out, onPull: () =>
                {
                    if (!HasBeenPulled(CurrentUpstream))
                        TryPull(CurrentUpstream);
                });
            }

            private bool IsUpstreamClosed => _runningUpstreams == 0;

            private Inlet<TIn> CurrentUpstream => _stage.Inlets[_currentUpstreamIndex];

            private void SwitchToNextInput()
            {
                _counter = 0;
                var index = _currentUpstreamIndex;
                while (true)
                {
                    var successor = index + 1 == _stage._inputPorts ? 0 : index + 1;

                    if (!IsClosed(_stage.Inlets[successor]))
                        _currentUpstreamIndex = successor;
                    else
                    {
                        if (successor != _currentUpstreamIndex)
                        {
                            index = successor;
                            continue;
                        }

                        CompleteStage();
                        _currentUpstreamIndex = 0; // return dummy/min value to exit stage logic gracefully
                    }

                    break;
                }
            }
        }

        #endregion

        private readonly int _inputPorts;
        private readonly int _segmentSize;
        private readonly bool _eagerClose;

        private Outlet<TOut> Out { get; }
        private Inlet<TIn>[] Inlets { get; }

        internal Interleave(int inputPorts, int segmentSize, bool eagerClose = false)
        {
            if (inputPorts <= 1) throw new ArgumentException("Interleave input ports count must be greater than 1", nameof(inputPorts));
            if (segmentSize <= 0) throw new ArgumentException("Interleave segment size must be greater than 0", nameof(segmentSize));

            _inputPorts = inputPorts;
            _segmentSize = segmentSize;
            _eagerClose = eagerClose;

            Out = new Outlet<TOut>("Interleave.out");
            Inlets = new Inlet<TIn>[inputPorts];
            for (int i = 0; i < inputPorts; i++)
                Inlets[i] = new Inlet<TIn>("Interleave.in" + i);

            Shape = new UniformFanInShape<TIn, TOut>(Out, Inlets);
        }

        public override UniformFanInShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new InterleaveStageLogic(Shape, this);

        public override string ToString() => "Interleave";
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
    public sealed class MergeSorted<T> : GraphStage<FanInShape<T, T, T>>
    {
        #region stage logic

        private sealed class MergeSortedStageLogic : GraphStageLogic
        {
            private readonly MergeSorted<T> _stage;
            private T _other;

            private readonly Action<T> _dispatchRight;
            private readonly Action<T> _dispatchLeft;
            private readonly Action _passRight;
            private readonly Action _passLeft;
            private readonly Action _readRight;
            private readonly Action _readLeft;

            public MergeSortedStageLogic(Shape shape, MergeSorted<T> stage) : base(shape)
            {
                _stage = stage;
                _dispatchRight = right => Dispatch(_other, right);
                _dispatchLeft = left => Dispatch(left, _other);
                _passRight = () => Emit(_stage._out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage._right, _stage._out, doPull: true);
                });
                _passLeft = () => Emit(_stage._out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage._left, _stage._out, doPull: true);
                });
                _readRight = () => Read(_stage._right, _dispatchRight, _passLeft);
                _readLeft = () => Read(_stage._left, _dispatchLeft, _passRight);

                SetHandler(_stage._left, IgnoreTerminateInput);
                SetHandler(_stage._right, IgnoreTerminateInput);
                SetHandler(_stage._out, EagerTerminateOutput);
            }

            public override void PreStart()
            {
                // all fan-in stages need to eagerly pull all inputs to get cycles started
                Pull(_stage._right);
                Read(_stage._left, left =>
                {
                    _other = left;
                    _readRight();
                },
                () => PassAlong(_stage._right, _stage._out));
            }

            private void NullOut()
            {
                _other = default(T);
            }

            private void Dispatch(T left, T right)
            {
                if (_stage._compare(left, right) == -1)
                {
                    _other = right;
                    Emit(_stage._out, left, _readLeft);
                }
                else
                {
                    _other = left;
                    Emit(_stage._out, right, _readRight);
                }
            }
        }

        #endregion

        private readonly Func<T, T, int> _compare;

        private readonly Inlet<T> _left = new Inlet<T>("left");
        private readonly Inlet<T> _right = new Inlet<T>("right");
        private readonly Outlet<T> _out = new Outlet<T>("out");

        public MergeSorted(Func<T, T, int> compare)
        {
            _compare = compare;
            Shape = new FanInShape<T, T, T>(_out, _left, _right);
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
        #region stage logic
        private sealed class Logic : GraphStageLogic
        {
            private readonly Broadcast<T> _stage;
            private readonly bool[] _pending;
            private int _pendingCount;
            private int _downstreamsRunning;

            public Logic(Shape shape, Broadcast<T> stage) : base(shape)
            {
                _stage = stage;
                _pendingCount = _downstreamsRunning = stage._outputPorts;
                _pending = new bool[stage._outputPorts];
                for (int i = 0; i < stage._outputPorts; i++) _pending[i] = true;

                SetHandler(_stage.In, onPush: () =>
                {
                    _pendingCount = _downstreamsRunning;
                    var element = Grab(stage.In);
                    var idx = 0;
                    var enumerator = stage.Out.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        var o = (Outlet<T>)enumerator.Current;
                        var i = idx;
                        if (!IsClosed(o))
                        {
                            Push(o, element);
                            _pending[i] = true;
                        }
                        idx++;
                    }
                });

                var outIdx = 0;
                var outEnumerator = stage.Out.GetEnumerator();
                while (outEnumerator.MoveNext())
                {
                    var o = (Outlet<T>)outEnumerator.Current;
                    var i = outIdx;
                    SetHandler(o, onPull: () =>
                    {
                        _pending[i] = false;
                        _pendingCount--;
                        TryPull();
                    },
                    onDownstreamFinish: () =>
                    {
                        if (stage._eagerCancel) CompleteStage();
                        else
                        {
                            _downstreamsRunning--;
                            if (_downstreamsRunning == 0) CompleteStage();
                            else if (_pending[i])
                            {
                                _pending[i] = false;
                                _pendingCount--;
                                TryPull();
                            }
                        }
                    });
                    outIdx++;
                }
            }

            private void TryPull()
            {
                if (_pendingCount == 0 && !HasBeenPulled(_stage.In)) Pull(_stage.In);
            }
        }
        #endregion

        private readonly int _outputPorts;
        private readonly bool _eagerCancel;

        public readonly Inlet<T> In = new Inlet<T>("Broadcast.in");
        public readonly Outlet<T>[] Out;

        public Broadcast(int outputPorts, bool eagerCancel = false)
        {
            if (outputPorts < 1) throw new ArgumentException("A Broadcast must have one or more output ports", nameof(outputPorts));
            _outputPorts = outputPorts;
            _eagerCancel = eagerCancel;

            Out = new Outlet<T>[outputPorts];
            for (int i = 0; i < outputPorts; i++)
                Out[i] = new Outlet<T>("Broadcast.out" + i);

            Shape = new UniformFanOutShape<T, T>(In, Out);
            InitialAttributes = Attributes.CreateName("Broadcast");
        }

        protected override Attributes InitialAttributes { get; }
        public override UniformFanOutShape<T, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }

        public override string ToString()
        {
            return "Broadcast";
        }
    }

    /// <summary>
    /// Fan-out the stream to several streams. Each upstream element is emitted to the first available downstream consumer.
    /// It will not shut down until the subscriptions
    /// for at least two downstream subscribers have been established.
    /// 
    /// A <see cref="Balance{T}"/> has one <see cref="In"/> port and 2 or more <see cref="Out"/> ports.
    /// <para>
    /// '''Emits when''' any of the outputs stops backpressuring; emits the element to the first available output
    /// </para>
    /// '''Backpressures when''' all of the outputs backpressure
    /// <para>
    /// '''Completes when''' upstream completes
    /// </para>
    /// '''Cancels when''' all downstreams cancel
    /// </summary>
    public sealed class Balance<T> : GraphStage<UniformFanOutShape<T, T>>
    {
        #region stage logic
        private sealed class Logic : GraphStageLogic
        {
            private readonly Balance<T> _stage;
            private readonly FixedSizeBuffer<Outlet<T>> _pendingQueue;
            private int _needDownstreamPulls;
            private int _downstreamsRunning;
            public Logic(Shape shape, Balance<T> stage) : base(shape)
            {
                _stage = stage;
                _pendingQueue = FixedSizeBuffer.Create<Outlet<T>>(_stage._outputPorts);
                _downstreamsRunning = _stage._outputPorts;

                _needDownstreamPulls = _stage._waitForAllDownstreams ? _stage._outputPorts : 0;

                SetHandler(_stage.In, onPush: DequeueAndDispatch);

                foreach (var outlet in _stage.Out)
                {
                    var hasPulled = false;
                    SetHandler(outlet, onPull: () =>
                    {
                        if (!hasPulled)
                        {
                            hasPulled = true;
                            if (_needDownstreamPulls > 0) _needDownstreamPulls--;
                        }

                        if (_needDownstreamPulls == 0)
                        {
                            if (IsAvailable(_stage.In))
                            {
                                if (NoPending) Push(outlet, Grab(_stage.In));
                            }
                            else
                            {
                                if (!HasBeenPulled(_stage.In)) Pull(_stage.In);
                                _pendingQueue.Enqueue(outlet);
                            }
                        }
                        else _pendingQueue.Enqueue(outlet);
                    },
                    onDownstreamFinish: () =>
                    {
                        _downstreamsRunning--;
                        if (_downstreamsRunning == 0) CompleteStage();
                        else if (!hasPulled && _needDownstreamPulls > 0)
                        {
                            _needDownstreamPulls--;
                            if (_needDownstreamPulls == 0 && !HasBeenPulled(_stage.In)) Pull(_stage.In);
                        }
                    });
                }
            }

            private bool NoPending => _pendingQueue.IsEmpty;

            private void DequeueAndDispatch()
            {
                var outlet = _pendingQueue.Dequeue();
                Push(outlet, Grab(_stage.In));
                if (!NoPending) Pull(_stage.In);
            }
        }
        #endregion

        private readonly int _outputPorts;
        private readonly bool _waitForAllDownstreams;

        public Balance(int outputPorts, bool waitForAllDownstreams = false)
        {
            if (outputPorts < 1) throw new ArgumentException("A Balance must have one or more output ports");
            _outputPorts = outputPorts;
            _waitForAllDownstreams = waitForAllDownstreams;

            Out = new Outlet<T>[outputPorts];
            for (int i = 0; i < outputPorts; i++) Out[i] = new Outlet<T>("Balance.out" + i);

            InitialAttributes = Attributes.CreateName("Balance");
            Shape = new UniformFanOutShape<T, T>(In, Out);
        }

        public Inlet<T> In { get; } = new Inlet<T>("Balance.in");
        public Outlet<T>[] Out { get; }

        protected override Attributes InitialAttributes { get; }
        public override UniformFanOutShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new Logic(Shape, this);
        }

        public override string ToString()
        {
            return "Balance";
        }
    }

    /// <summary>
    /// Combine the elements of 2 streams into a stream of tuples.
    /// 
    /// A <see cref="Zip{T1,T2}"/> has a `left` and a `right` input port and one `out` port
    /// <para>
    /// '''Emits when''' all of the inputs has an element available
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' any upstream completes
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary>
    public sealed class Zip<T1, T2> : ZipWith<T1, T2, Tuple<T1, T2>>
    {
        public Zip() : base((a, b) => new Tuple<T1, T2>(a, b)) { }

        public override string ToString()
        {
            return "Zip";
        }
    }

    /// <summary>
    /// Combine the elements of multiple streams into a stream of combined elements using a combiner function.
    /// <para>
    /// '''Emits when''' all of the inputs has an element available
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' any upstream completes
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary>
    public sealed partial class ZipWith
    {
        public static readonly ZipWith Instance = new ZipWith();
        private ZipWith() { }
    }

    /// <summary>
    /// Takes a stream of pair elements and splits each pair to two output streams.
    /// 
    /// An <see cref="UnZip{T1,T2}"/> has one `in` port and one `left` and one `right` output port.
    /// <para>
    /// '''Emits when''' all of the outputs stops backpressuring and there is an input element available
    /// </para>
    /// '''Backpressures when''' any of the outputs backpressures
    /// <para>
    /// '''Completes when''' upstream completes
    /// </para>
    /// '''Cancels when''' any downstream cancels
    /// </summary>
    public sealed class UnZip<T1, T2> : UnzipWith<KeyValuePair<T1, T2>, T1, T2>
    {
        public UnZip() : base(kv => Tuple.Create(kv.Key, kv.Value)) { }
    }

    /// <summary>
    /// Transforms each element of input stream into multiple streams using a splitter function.
    /// <para>
    /// '''Emits when''' all of the outputs stops backpressuring and there is an input element available
    /// </para>
    /// '''Backpressures when''' any of the outputs backpressures
    /// <para>
    /// '''Completes when''' upstream completes
    /// </para>
    /// '''Cancels when''' any downstream cancels
    /// </summary>
    public partial class UnzipWith
    {
        public static readonly UnzipWith Instance = new UnzipWith();
        private UnzipWith() { }
    }

    public static class Concat
    {
        public static IGraph<UniformFanInShape<T, T>, Unit> Create<T>(int inputPorts = 2)
        {
            return GraphStages.WithDetachedInputs<T, Unit>(new Concat<T, T>(inputPorts));
        }
    }

    /// <summary>
    /// Takes two streams and outputs one stream formed from the two input streams
    /// by first emitting all of the elements from the first stream and then emitting
    /// all of the elements from the second stream.
    /// 
    /// A <see cref="Concat{T,TMat}"/> has one `first` port, one `second` port and one `out` port.
    /// <para>
    /// '''Emits when''' the current stream has an element available; if the current input completes, it tries the next one
    /// </para>
    /// '''Backpressures when''' downstream backpressures
    /// <para>
    /// '''Completes when''' all upstreams complete
    /// </para>
    /// '''Cancels when''' downstream cancels
    /// </summary>
    public class Concat<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic
        private sealed class ConcatStageLogic : GraphStageLogic
        {
            public ConcatStageLogic(Shape shape, Concat<TIn, TOut> stage) : base(shape)
            {
                var activeStream = 0;
                var iidx = 0;
                var inEnumerator = (stage.Ins as IEnumerable<Inlet>).GetEnumerator();
                while (inEnumerator.MoveNext())
                {
                    var i = (Inlet<TIn>)inEnumerator.Current;
                    var idx = iidx;
                    SetHandler(i,
                        onPush: () => Push(stage.Out, Grab(i)),
                        onUpstreamFinish: () =>
                        {
                            if (idx == activeStream)
                            {
                                activeStream++;
                                // skip closed inputs
                                while (activeStream < stage._inputPorts && IsClosed(stage.Ins[activeStream])) activeStream++;
                                if (activeStream == stage._inputPorts) CompleteStage();
                                else if (IsAvailable(stage.Out)) Pull(stage.Ins[activeStream]);
                            }
                        });
                    iidx++;
                }

                SetHandler(stage.Out, onPull: () => Pull(stage.Ins[activeStream]));
            }
        }
        #endregion

        private readonly int _inputPorts;

        internal Concat(int inputPorts = 2)
        {
            if (inputPorts <= 1) throw new ArgumentException("A Concat must have more than 1 input port");
            _inputPorts = inputPorts;

            Ins = new Inlet<TIn>[inputPorts];
            for (int i = 0; i < inputPorts; i++) Ins[i] = new Inlet<TIn>("Concat.in" + i);

            Shape = new UniformFanInShape<TIn, TOut>(Out, Ins);
            InitialAttributes = Attributes.CreateName("Concat");
        }

        public Inlet<TIn>[] Ins { get; }
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Concat.out");

        protected override Attributes InitialAttributes { get; }
        public override UniformFanInShape<TIn, TOut> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new ConcatStageLogic(Shape, this);
        }
    }
}