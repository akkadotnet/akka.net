//-----------------------------------------------------------------------
// <copyright file="Graph.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Util.Internal;
// ReSharper disable MemberCanBePrivate.Global

namespace Akka.Streams.Dsl
{
    /// <summary>
    /// Merge several streams, taking elements as they arrive from input streams
    /// (picking randomly when several have elements ready).
    /// <para>
    /// Emits when one of the inputs has an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class Merge<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region graph stage logic

        private sealed class Logic : OutGraphStageLogic
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
                foreach (var inlet in _stage.Shape.Ins)
                {
                    SetHandler(inlet, onPush: () =>
                    {
                        if (IsAvailable(outlet))
                        {
                            // isAvailable(out) implies !pending
                            // -> grab and push immediately
                            Push(outlet, Grab(inlet));
                            TryPull(inlet);
                        }
                        else _pendingQueue.Enqueue(inlet);
                    },
                    onUpstreamFinish: () =>
                    {
                        if (_stage._eagerComplete)
                        {
                            foreach (var i in _stage.Shape.Ins) Cancel(i);
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

                SetHandler(outlet, this);
            }

            public override void OnPull()
            {
                if (IsPending)
                    DequeueAndDispatch();
            }

            private bool AreUpstreamsClosed => _runningUpstreams == 0;

            private bool IsPending => _pendingQueue.NonEmpty;

            public override void PreStart()
            {
                foreach (var inlet in _stage.Shape.Ins)
                    TryPull(inlet);
            }

            private void DequeueAndDispatch()
            {
                var inlet = _pendingQueue.Dequeue();
                if (inlet == null)
                {
                    // inlet is null if we reached the end of the queue
                    if(AreUpstreamsClosed)
                        CompleteStage();
                }
                else if (IsAvailable(inlet))
                {
                    Push(_stage.Out, Grab(inlet));
                    if(AreUpstreamsClosed && !IsPending)
                        CompleteStage();
                    else
                        TryPull(inlet);
                }
                else
                    // inlet was closed after being enqueued
                    // try next in queue
                    DequeueAndDispatch();
            }
        }

        #endregion

        private readonly int _inputPorts;
        private readonly bool _eagerComplete;

        /// <summary>
        /// Initializes a new instance of the <see cref="Merge{TIn, TOut}"/> class.
        /// </summary>
        /// <param name="inputPorts">TBD</param>
        /// <param name="eagerComplete">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="inputPorts"/> is less than one.
        /// </exception>
        public Merge(int inputPorts, bool eagerComplete = false)
        {
            // one input might seem counter intuitive but saves us from special handling in other places
            if (inputPorts < 1) throw new ArgumentException("Merge must have one or more input ports", nameof(inputPorts));
            _inputPorts = inputPorts;
            _eagerComplete = eagerComplete;

            var ins = ImmutableArray<Inlet<TIn>>.Empty.ToBuilder();
            for (var i = 0; i < inputPorts; i++)
                ins.Add(new Inlet<TIn>("Merge.in" + i));

            Out = new Outlet<TOut>("Merge.out");
            Shape = new UniformFanInShape<TIn, TOut>(Out, ins.ToArray());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Inlet<TIn> In(int id) => Shape.In(id);

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Merge");

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanInShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Merge";
    }

    /// <summary>
    /// Merge several streams, taking elements as they arrive from input streams
    /// (picking randomly when several have elements ready).
    /// <para>
    /// Emits when one of the inputs has an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class Merge<T> : Merge<T, T>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Merge{T}"/> class.
        /// </summary>
        /// <param name="inputPorts">TBD</param>
        /// <param name="eagerComplete">TBD</param>
        public Merge(int inputPorts, bool eagerComplete = false) : base(inputPorts, eagerComplete)
        {
        }
    }

    /// <summary>
    /// Merge several streams, taking elements as they arrive from input streams
    /// (picking from preferred when several have elements ready).
    /// 
    /// A <see cref="MergePreferred{T}"/> has one <see cref="Out"/> port, one <see cref="Preferred"/> input port and 0 or more secondary <see cref="In"/> ports.
    /// <para>
    /// Emits when one of the inputs has an element available, preferring
    /// a specified input if multiple have elements available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete (eagerComplete=false) or one upstream completes (eagerComplete=true)
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class MergePreferred<T> : GraphStage<MergePreferred<T>.MergePreferredShape>
    {
        #region internal classes

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class MergePreferredShape : UniformFanInShape<T, T>
        {
            private readonly int _secondaryPorts;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="secondaryPorts">TBD</param>
            /// <param name="init">TBD</param>
            public MergePreferredShape(int secondaryPorts, IInit init) : base(secondaryPorts, init)
            {
                _secondaryPorts = secondaryPorts;

                Preferred = NewInlet<T>("preferred");
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="secondaryPorts">TBD</param>
            /// <param name="name">TBD</param>
            public MergePreferredShape(int secondaryPorts, string name) : this(secondaryPorts, new InitName(name)) { }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="init">TBD</param>
            /// <returns>TBD</returns>
            protected override FanInShape<T> Construct(IInit init) => new MergePreferredShape(_secondaryPorts, init);

            /// <summary>
            /// TBD
            /// </summary>
            public Inlet<T> Preferred { get; }
        }

        private sealed class Logic : InGraphStageLogic
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
                SetHandler(_stage.Preferred, this);

                for (int i = 0; i < stage._secondaryPorts; i++)
                {
                    var port = stage.In(i);
                    var pullPort = _pullMe[i];

                    SetHandler(port, onPush: () =>
                    {
                        if (_preferredEmitting > 0)
                        {
                             /* blocked */
                        }
                        else
                            Emit(_stage.Out, Grab(port), pullPort);
                    },
                    onUpstreamFinish: OnComplete);
                }
            }

            public override void OnPush()
            {
                if (_preferredEmitting == MaxEmitting)
                {
                     /* blocked */
                }
                else
                    EmitPreferred();
            }

            public override void OnUpstreamFinish() => OnComplete();

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

        /// <summary>
        /// Initializes a new instance of the <see cref="MergePreferred{T}"/> class.
        /// </summary>
        /// <param name="secondaryPorts">TBD</param>
        /// <param name="eagerClose">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="secondaryPorts"/> is less than one.
        /// </exception>
        public MergePreferred(int secondaryPorts, bool eagerClose = false)
        {
            if (secondaryPorts < 1) throw new ArgumentException("A MergePreferred must have at least one secondary port", nameof(secondaryPorts));
            _secondaryPorts = secondaryPorts;
            _eagerClose = eagerClose;

            Shape = new MergePreferredShape(_secondaryPorts, "MergePreferred");
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("MergePreferred");

        /// <summary>
        /// TBD
        /// </summary>
        public override MergePreferredShape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Inlet<T> In(int id) => Shape.In(id);

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<T> Out => Shape.Out;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> Preferred => Shape.Preferred;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);
    }

    /// <summary>
    /// Merge several streams, taking elements as they arrive from input streams
    /// (picking from prioritized once when several have elements ready).
    /// A <see cref="MergePrioritized{T}" /> has one <see cref="MergePrioritized{T}.Out" /> port, one or more input port with their priorities.
    /// <para>
    /// Emits when one of the inputs has an element available, preferring
    /// a input based on its priority if multiple have elements available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete (eagerComplete=false) or one upstream completes (eagerComplete=true), default value is `false`
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    public sealed class MergePrioritized<T> : GraphStage<UniformFanInShape<T, T>>
    {
        internal IList<int> Priorities { get; }
        internal bool EagerComplete { get; }
        internal int InputPorts { get; }

        #region internal classes
        internal sealed class MergePrioritizedLogic : OutGraphStageLogic
        {
            private readonly MergePrioritized<T> _stage;
            private readonly List<FixedSizeBuffer<Inlet<T>>> _allBuffers;
            private int _runningUpstreams;
            private readonly Random _randomGen = new Random();

            public MergePrioritizedLogic(MergePrioritized<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _allBuffers = new List<FixedSizeBuffer<Inlet<T>>>(stage.Priorities.Count);

                foreach (int priority in stage.Priorities)
                    _allBuffers.Add(FixedSizeBuffer.Create<Inlet<T>>(priority));

                _runningUpstreams = stage.InputPorts;

                for (int i = 0; i < stage.In.Count; i++)
                {
                    var inlet = stage.In[i];
                    var buffer = _allBuffers[i];

                    SetHandler(inlet, onPush: () =>
                    {
                        if (IsAvailable(_stage.Out) && !HasPending)
                        {
                            Push(_stage.Out, Grab(inlet));
                            TryPull(inlet);
                        }
                        else
                            buffer.Enqueue(inlet);
                    }, onUpstreamFinish: () =>
                    {
                        if (_stage.EagerComplete)
                        {
                            _stage.In.ForEach(Cancel);
                            _runningUpstreams = 0;
                            if (!HasPending) CompleteStage();
                        }
                        else
                        {
                            _runningUpstreams -= 1;
                            if (UpstreamsClosed && !HasPending) CompleteStage();
                        }
                    });
                }

                SetHandler(_stage.Out, this);
            }

            public override void PreStart() => _stage.In.ForEach(TryPull);

            public override void OnPull()
            {
                if (HasPending)
                    DequeueAndDispatch();
            }

            public bool HasPending => _allBuffers.Any(c => c.NonEmpty);

            public bool UpstreamsClosed => _runningUpstreams == 0;

            private void DequeueAndDispatch()
            {
                var input = SelectNextElement();
                Push(_stage.Out, Grab(input));
                if (UpstreamsClosed && !HasPending)
                    CompleteStage();
                else
                    TryPull(input);
            }

            private Inlet<T> SelectNextElement()
            {
                var tp = 0;
                var ix = 0;

                while (ix < _stage.In.Count)
                {
                    if (_allBuffers[ix].NonEmpty)
                        tp += _stage.Priorities[ix];
                    ix += 1;
                }

                int r = _randomGen.Next(tp);
                Inlet<T> next = null;
                ix = 0;

                while (ix < _stage.In.Count && next == null)
                {
                    if (_allBuffers[ix].NonEmpty)
                    {
                        r -= _stage.Priorities[ix];
                        if (r < 0)
                            next = _allBuffers[ix].Dequeue();
                    }
                    ix += 1;
                }

                return next;
            }

            public override string ToString() => "MergePrioritized";
        }
        #endregion

        /// <summary>
        /// Create a new <see cref="MergePrioritized{T}" /> with specified number of input ports.
        /// </summary>
        /// <param name="priorities">Priorities of the input ports</param>
        /// <param name="eagerComplete">If true, the merge will complete as soon as one of its inputs completes</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="priorities"/> is less or equal zero.
        /// </exception>
        public MergePrioritized(IEnumerable<int> priorities, bool eagerComplete = false)
        {
            Priorities = priorities.ToList();
            EagerComplete = eagerComplete;
            InputPorts = Priorities.Count;
            if (InputPorts <= 0)
                throw new ArgumentException("A Merge must have one or more input ports");
            if (!Priorities.All(x => x > 0))
                throw new ArgumentException("Priorities should be positive integers");

            var input = new List<Inlet<T>>();
            for (int i = 1; i <= InputPorts; i++)
                input.Add(new Inlet<T>("MergePrioritized.in" + i));
            In = input;

            Out = new Outlet<T>("MergePrioritized.out");
            Shape = new UniformFanInShape<T, T>(Out, In.ToArray());
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("MergePrioritized");

        public override UniformFanInShape<T, T> Shape { get; }

        public IReadOnlyList<Inlet<T>> In { get; }

        public Outlet<T> Out { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new MergePrioritizedLogic(this);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Interleave
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inputPorts">TBD</param>
        /// <param name="segmentSize">TBD</param>
        /// <param name="eagerClose">TBD</param>
        /// <returns>TBD</returns>
        public static IGraph<UniformFanInShape<T, T>, NotUsed> Create<T>(int inputPorts, int segmentSize,
            bool eagerClose = false)
        {
            return GraphStages.WithDetachedInputs(new Interleave<T, T>(inputPorts, segmentSize, eagerClose));
        }
    }

    /// <summary>
    /// Interleave represents deterministic merge which takes N elements per input stream,
    /// in-order of inputs, emits them downstream and then cycles/"wraps-around" the inputs.
    /// <para>
    /// Emits when element is available from current input (depending on phase)
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete (eagerComplete=false) or one upstream completes (eagerComplete=true)
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public sealed class Interleave<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly Interleave<TIn, TOut> _stage;
            private int _counter;
            private int _currentUpstreamIndex;
            private int _runningUpstreams;

            public Logic(Shape shape, Interleave<TIn, TOut> stage) : base(shape)
            {
                _stage = stage;
                _runningUpstreams = _stage._inputPorts;

                foreach (var inlet in _stage.Shape.Ins)
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

                SetHandler(_stage.Out, this);
            }

            public override void OnPull()
            {
                if (!HasBeenPulled(CurrentUpstream))
                    TryPull(CurrentUpstream);
            }

            private bool IsUpstreamClosed => _runningUpstreams == 0;

            private Inlet<TIn> CurrentUpstream => _stage.In(_currentUpstreamIndex);

            private void SwitchToNextInput()
            {
                _counter = 0;
                var index = _currentUpstreamIndex;
                while (true)
                {
                    var successor = index + 1 == _stage._inputPorts ? 0 : index + 1;

                    if (!IsClosed(_stage.In(successor)))
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inputPorts">TBD</param>
        /// <param name="segmentSize">TBD</param>
        /// <param name="eagerClose">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="inputPorts"/> is less than or equal to one
        /// or the specified <paramref name="segmentSize"/> is less than or equal to zero.
        /// </exception>
        internal Interleave(int inputPorts, int segmentSize, bool eagerClose = false)
        {
            if (inputPorts <= 1) throw new ArgumentException("Interleave input ports count must be greater than 1", nameof(inputPorts));
            if (segmentSize <= 0) throw new ArgumentException("Interleave segment size must be greater than 0", nameof(segmentSize));

            _inputPorts = inputPorts;
            _segmentSize = segmentSize;
            _eagerClose = eagerClose;

            Out = new Outlet<TOut>("Interleave.out");

            var inlets = new Inlet<TIn>[inputPorts];
            for (var i = 0; i < inputPorts; i++)
                inlets[i] = new Inlet<TIn>("Interleave.in" + i);

            Shape = new UniformFanInShape<TIn, TOut>(Out, inlets);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Inlet<TIn> In(int id) => Shape.In(id);

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanInShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Interleave";
    }

    /// <summary>
    /// Merge two pre-sorted streams such that the resulting stream is sorted.
    /// <para>
    /// Emits when both inputs have an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class MergeSorted<T> : GraphStage<FanInShape<T, T, T>>
    {
        #region stage logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly MergeSorted<T> _stage;
            private T _other;

            private readonly Action _readRight;
            private readonly Action _readLeft;

            public Logic(MergeSorted<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                void DispatchRight(T right) => Dispatch(_other, right);
                void DispatchLeft(T left) => Dispatch(left, _other);

                void PassRight() => Emit(_stage.Out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage.Right, _stage.Out, doPull: true);
                });

                void PassLeft() => Emit(_stage.Out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage.Left, _stage.Out, doPull: true);
                });

                _readRight = () => Read(_stage.Right, DispatchRight, PassLeft);
                _readLeft = () => Read(_stage.Left, DispatchLeft, PassRight);

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
                    _readRight();
                },
                () => PassAlong(_stage.Right, _stage.Out));
            }

            private void NullOut() => _other = default(T);

            private void Dispatch(T left, T right)
            {
                if (_stage._compare(left, right) < 0)
                {
                    _other = right;
                    Emit(_stage.Out, left, _readLeft);
                }
                else
                {
                    _other = left;
                    Emit(_stage.Out, right, _readRight);
                }
            }
        }

        #endregion

        private readonly Func<T, T, int> _compare;

        /// <summary>
        /// Initializes a new instance of the <see cref="MergeSorted{T}"/> class.
        /// </summary>
        /// <param name="compare">TBD</param>
        public MergeSorted(Func<T, T, int> compare)
        {
            _compare = compare;
            Shape = new FanInShape<T, T, T>(Out, Left, Right);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> Left = new Inlet<T>("left");

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> Right = new Inlet<T>("right");

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Outlet<T> Out = new Outlet<T>("out");

        /// <summary>
        /// TBD
        /// </summary>
        public override FanInShape<T, T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }

    /// <summary>
    /// Fan-out the stream to several streams emitting each incoming upstream element to all downstream consumers.
    /// It will not shut down until the subscriptions for at least two downstream subscribers have been established.
    /// <para>
    /// Emits when all of the outputs stops backpressuring and there is an input element available
    /// </para>
    /// Backpressures when any of the outputs backpressure
    /// <para>
    /// Completes when upstream completes
    /// </para>
    /// Cancels when If eagerCancel is enabled: when any downstream cancels; otherwise: when all downstreams cancel
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class Broadcast<T> : GraphStage<UniformFanOutShape<T, T>>
    {
        #region stage logic
        private sealed class Logic : InGraphStageLogic
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
                for (int i = 0; i < stage._outputPorts; i++)
                    _pending[i] = true;

                SetHandler(_stage.In, this);

                var outIdx = 0;
                var outEnumerator = stage.Shape.Outlets.GetEnumerator();
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

            public override void OnPush()
            {
                _pendingCount = _downstreamsRunning;
                var element = Grab(_stage.In);
                var idx = 0;
                var enumerator = _stage.Shape.Outlets.GetEnumerator();
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
            }

            private void TryPull()
            {
                if (_pendingCount == 0 && !HasBeenPulled(_stage.In)) Pull(_stage.In);
            }
        }
        #endregion

        private readonly int _outputPorts;
        private readonly bool _eagerCancel;

        /// <summary>
        /// Initializes a new instance of the <see cref="Broadcast{T}"/> class.
        /// </summary>
        /// <param name="outputPorts">TBD</param>
        /// <param name="eagerCancel">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="outputPorts"/> is less than one.
        /// </exception>
        public Broadcast(int outputPorts, bool eagerCancel = false)
        {
            if (outputPorts < 1) throw new ArgumentException("A Broadcast must have one or more output ports", nameof(outputPorts));
            _outputPorts = outputPorts;
            _eagerCancel = eagerCancel;

            var outlets = new Outlet<T>[outputPorts];
            for (int i = 0; i < outputPorts; i++)
                outlets[i] = new Outlet<T>("Broadcast.out" + i);

            Shape = new UniformFanOutShape<T, T>(In, outlets);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("Broadcast.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Outlet<T> Out(int id) => Shape.Out(id);

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Broadcast");

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanOutShape<T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Broadcast";
    }

    /// <summary>
    /// Fan-out the stream to several streams. emitting an incoming upstream element to one downstream consumer according
    /// to the partitioner function applied to the element
    /// <para>
    /// Emits when an element is available from the input and the chosen output has demand
    /// </para>
    /// Backpressures when the currently chosen output back-pressures
    /// <para>
    /// Completes when upstream completes and no output is pending
    /// </para>
    /// Cancels when when all downstreams cancel
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class Partition<T> : GraphStage<UniformFanOutShape<T, T>>
    {
        #region internal classes

        private sealed class Logic : InGraphStageLogic
        {
            private readonly Partition<T> _stage;
            private object _outPendingElement;
            private int _outPendingIndex;

            public Logic(Partition<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                var downstreamRunning = stage._outputPorts;

                SetHandler(stage.In, this);

                for (var i = 0; i < stage._outputPorts; i++)
                {
                    var output = stage.Out(i);
                    var index = i;

                    SetHandler(output, onPull: () =>
                    {
                        if (_outPendingElement != null)
                        {
                            var element = (T) _outPendingElement;
                            if (index == _outPendingIndex)
                            {
                                Push(output, element);
                                _outPendingElement = null;
                                if (!IsClosed(stage.In))
                                {
                                    if (!HasBeenPulled(stage.In))
                                        Pull(stage.In);
                                }
                                else
                                    CompleteStage();
                            }
                        }
                        else if (!HasBeenPulled(stage.In))
                            Pull(stage.In);
                    }, onDownstreamFinish: () =>
                    {
                        downstreamRunning--;
                        if(downstreamRunning == 0)
                            CompleteStage();
                        else if (_outPendingElement != null)
                        {
                            if (index == _outPendingIndex)
                            {
                                _outPendingElement = null;
                                if(!HasBeenPulled(stage.In))
                                    Pull(stage.In);
                            }
                        }
                    });
                }
            }

            public override void OnPush()
            {
                var element = Grab(_stage.In);
                var index = _stage._partitioner(element);
                if (index < 0 || index >= _stage._outputPorts)
                    FailStage(
                        new PartitionOutOfBoundsException(
                            $"partitioner must return an index in the range [0,{_stage._outputPorts - 1}]. returned: [{index}] for input [{element.GetType().Name}]."));
                else if (!IsClosed(_stage.Out(index)))
                {
                    if (IsAvailable(_stage.Out(index)))
                    {
                        Push(_stage.Out(index), element);
                        if (_stage._outlets.Any(IsAvailable))
                            Pull(_stage.In);
                    }
                    else
                    {
                        _outPendingElement = element;
                        _outPendingIndex = index;
                    }
                }
                else if (_stage._outlets.Any(IsAvailable))
                    Pull(_stage.In);
            }

            public override void OnUpstreamFinish()
            {
                if (_outPendingElement == null)
                    CompleteStage();
            }
        }

        #endregion

        private readonly int _outputPorts;
        private readonly Func<T, int> _partitioner;
        private readonly Outlet<T>[] _outlets;

        /// <summary>
        /// Initializes a new instance of the <see cref="Partition{T}"/> class.
        /// </summary>
        /// <param name="outputPorts">TBD</param>
        /// <param name="partitioner">TBD</param>
        public Partition(int outputPorts, Func<T, int> partitioner)
        {
            _outputPorts = outputPorts;
            _partitioner = partitioner;
            _outlets = Enumerable.Range(0, outputPorts).Select(i => new Outlet<T>("Partition.out" + i)).ToArray();
            Shape = new UniformFanOutShape<T, T>(In, _outlets);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public readonly Inlet<T> In = new Inlet<T>("Partition.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Outlet<T> Out(int id) => Shape.Out(id);

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanOutShape<T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => $"Partition({_outputPorts})";

    }

    /// <summary>
    /// TBD
    /// </summary>
    public sealed class PartitionOutOfBoundsException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PartitionOutOfBoundsException" /> class.
        /// </summary>
        /// <param name="message">The message that describes the error. </param>
        public PartitionOutOfBoundsException(string message) : base(message)
        {

        }
    }

    /// <summary>
    /// Fan-out the stream to several streams. Each upstream element is emitted to the first available downstream consumer.
    /// It will not shut down until the subscriptions
    /// for at least two downstream subscribers have been established.
    /// 
    /// A <see cref="Balance{T}"/> has one <see cref="In"/> port and 2 or more <see cref="Out"/> ports.
    /// <para>
    /// Emits when any of the outputs stops backpressuring; emits the element to the first available output
    /// </para>
    /// Backpressures when all of the outputs backpressure
    /// <para>
    /// Completes when upstream completes
    /// </para>
    /// Cancels when all downstreams cancel
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class Balance<T> : GraphStage<UniformFanOutShape<T, T>>
    {
        #region stage logic
        private sealed class Logic : InGraphStageLogic
        {
            private readonly Balance<T> _stage;
            private readonly FixedSizeBuffer<Outlet<T>> _pendingQueue;

            public Logic(Shape shape, Balance<T> stage) : base(shape)
            {
                _stage = stage;
                _pendingQueue = FixedSizeBuffer.Create<Outlet<T>>(_stage._outputPorts);
                var downstreamsRunning = _stage._outputPorts;

                var needDownstreamPulls = _stage._waitForAllDownstreams ? _stage._outputPorts : 0;

                SetHandler(_stage.In, this);

                foreach (var outlet in _stage.Shape.Outs)
                {
                    var hasPulled = false;
                    SetHandler(outlet, onPull: () =>
                    {
                        if (!hasPulled)
                        {
                            hasPulled = true;
                            if (needDownstreamPulls > 0) needDownstreamPulls--;
                        }

                        if (needDownstreamPulls == 0)
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
                        downstreamsRunning--;
                        if (downstreamsRunning == 0) CompleteStage();
                        else if (!hasPulled && needDownstreamPulls > 0)
                        {
                            needDownstreamPulls--;
                            if (needDownstreamPulls == 0 && !HasBeenPulled(_stage.In)) Pull(_stage.In);
                        }
                    });
                }
            }

            public override void OnPush() => DequeueAndDispatch();

            private bool NoPending => _pendingQueue.IsEmpty;

            private void DequeueAndDispatch()
            {
                var outlet = _pendingQueue.Dequeue();

                // outlet is null if depleted _pendingQueue without reaching
                // an outlet that is not closed, in which case we just return

                if (outlet != null)
                {
                    if (!IsClosed(outlet))
                    {
                        Push(outlet, Grab(_stage.In));
                        if (!NoPending)
                            Pull(_stage.In);
                    }
                    else
                        // try to find one output that isn't closed
                        DequeueAndDispatch();
                }
            }
        }

        #endregion

        private readonly int _outputPorts;
        private readonly bool _waitForAllDownstreams;

        /// <summary>
        /// Initializes a new instance of the <see cref="Balance{T}"/> class.
        /// </summary>
        /// <param name="outputPorts">TBD</param>
        /// <param name="waitForAllDownstreams">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="outputPorts"/> is less than one.
        /// </exception>
        public Balance(int outputPorts, bool waitForAllDownstreams = false)
        {
            if (outputPorts < 1) throw new ArgumentException("A Balance must have one or more output ports", nameof(outputPorts));
            _outputPorts = outputPorts;
            _waitForAllDownstreams = waitForAllDownstreams;

            var outlets = new Outlet<T>[outputPorts];
            for (var i = 0; i < outputPorts; i++)
                outlets[i] = new Outlet<T>("Balance.out" + i);
            
            Shape = new UniformFanOutShape<T, T>(In, outlets);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> In { get; } = new Inlet<T>("Balance.in");

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Outlet<T> Out(int id) => Shape.Out(id);

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Balance");

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanOutShape<T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Balance";
    }

    /// <summary>
    /// Combine the elements of 2 streams into a stream of tuples.
    /// 
    /// A <see cref="Zip{T1,T2}"/> has a left and a right input port and one out port
    /// <para>
    /// Emits when all of the inputs has an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when any upstream completes
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    public sealed class Zip<T1, T2> : ZipWith<T1, T2, (T1, T2)>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Zip{T1,T2}"/> class.
        /// </summary>
        public Zip() : base((a, b) => (a, b)) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Zip";
    }

    /// <summary>
    /// Combine the elements of multiple streams into a stream of combined elements using a combiner function.
    /// <para>
    /// Emits when all of the inputs has an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when any upstream completes
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    public sealed partial class ZipWith
    {
        /// <summary>
        /// The singleton instance of <see cref="ZipWith"/>.
        /// </summary>
        public static readonly ZipWith Instance = new ZipWith();
        private ZipWith() { }
    }

    /// <summary>
    /// Takes a stream of pair elements and splits each pair to two output streams.
    /// 
    /// An <see cref="UnZip{T1,T2}"/> has one in port and one left and one right output port.
    /// <para>
    /// Emits when all of the outputs stops backpressuring and there is an input element available
    /// </para>
    /// Backpressures when any of the outputs backpressure
    /// <para>
    /// Completes when upstream completes
    /// </para>
    /// Cancels when any downstream cancels
    /// </summary>
    /// <typeparam name="T1">TBD</typeparam>
    /// <typeparam name="T2">TBD</typeparam>
    public sealed class UnZip<T1, T2> : UnzipWith<KeyValuePair<T1, T2>, T1, T2>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnZip{T1,T2}"/> class.
        /// </summary>
        public UnZip() : base(kv => (kv.Key, kv.Value)) { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "Unzip";
    }

    /// <summary>
    /// Transforms each element of input stream into multiple streams using a splitter function.
    /// <para>
    /// Emits when all of the outputs stops backpressuring and there is an input element available
    /// </para>
    /// Backpressures when any of the outputs backpressures
    /// <para>
    /// Completes when upstream completes
    /// </para>
    /// Cancels when any downstream cancels
    /// </summary>
    public partial class UnzipWith
    {
        /// <summary>
        /// The singleton instance of <see cref="UnzipWith"/>.
        /// </summary>
        public static readonly UnzipWith Instance = new UnzipWith();
        private UnzipWith() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class ZipN
    {
        /// <summary>
        /// Create a new <see cref="ZipN{T}"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="n">TBD</param>
        /// <returns>TBD</returns>
        public static IGraph<UniformFanInShape<T, IImmutableList<T>>> Create<T>(int n) => new ZipN<T>(n);
    }

    /// <summary>
    /// Combine the elements of multiple streams into a stream of sequences.
    /// A <see cref="ZipN{T}"/> has a n input ports and one out port
    /// 
    /// <para>
    /// Emits when all of the inputs has an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when any upstream completes
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class ZipN<T> : ZipWithN<T, IImmutableList<T>>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ZipN{T}"/> class.
        /// </summary>
        /// <param name="n">TBD</param>
        public ZipN(int n) : base(x => x, n)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ZipN;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "ZipN";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class ZipWithN
    {
        /// <summary>
        /// Creates a new <see cref="ZipWithN{TIn,TOut}"/>
        /// </summary>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <typeparam name="TOut">TBD</typeparam>
        /// <param name="zipper">TBD</param>
        /// <param name="n">TBD</param>
        public static IGraph<UniformFanInShape<TIn, TOut>> Create<TIn, TOut>(Func<IImmutableList<TIn>, TOut> zipper,
            int n) => new ZipWithN<TIn, TOut>(zipper, n);
    }

    /// <summary>
    /// Combine the elements of multiple streams into a stream of sequences using a combiner function.
    /// A <see cref="ZipWithN{TIn,TOut}"/> has a n input ports and one out port
    /// <para>
    /// Emits when all of the inputs has an element available
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when any upstream completes
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class ZipWithN<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>>
    {
        #region Logic 

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly ZipWithN<TIn, TOut> _stage;
            private int _pending;
            // Without this field the completion signalling would take one extra pull
            private bool _willShutDown;

            public Logic(ZipWithN<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;

                stage.Inlets.ForEach(i =>
                {
                    SetHandler(i, onPush: () =>
                    {
                        _pending--;
                        if(_pending == 0)
                            PushAll();
                    }, onUpstreamFinish: () =>
                    {
                        if(!IsAvailable(i))
                            CompleteStage();
                        _willShutDown = true;
                    });
                });

                SetHandler(stage.Out, this);
            }

            public override void OnPull()
            {
                _pending += _stage._n;
                if (_pending == 0)
                    PushAll();
            }

            private void PushAll()
            {
                Push(_stage.Out, _stage._zipper(_stage.Inlets.Select(i => Grab((i))).ToImmutableList()));
                if(_willShutDown)
                    CompleteStage();
                else
                    _stage.Inlets.ForEach(Pull);
            }

            public override void PreStart() => _stage.Inlets.ForEach(Pull);

            public override string ToString() => "ZipWithNLogic";
        }

        #endregion

        private readonly Func<IImmutableList<TIn>, TOut> _zipper;
        private readonly int _n;

        /// <summary>
        /// Initializes a new instance of the <see cref="ZipWithN{TIn, TOut}"/> class.
        /// </summary>
        /// <param name="zipper">TBD</param>
        /// <param name="n">TBD</param>
        public ZipWithN(Func<IImmutableList<TIn>, TOut> zipper, int n)
        {
            _zipper = zipper;
            _n = n;
            Shape = new UniformFanInShape<TIn, TOut>(n);
            Out = Shape.Out;
            Inlets = Shape.Ins;
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ZipWithN;

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public IImmutableList<Inlet<TIn>> Inlets { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="i">TBD</param>
        /// <returns>TBD</returns>
        public Inlet<TIn> In(int i) => Inlets[i];

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanInShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "ZipWithN";
    }

    /// <summary>
    /// TBD
    /// </summary>
    public static class Concat
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inputPorts">TBD</param>
        /// <returns>TBD</returns>
        public static IGraph<UniformFanInShape<T, T>, NotUsed> Create<T>(int inputPorts = 2)
        {
            return GraphStages.WithDetachedInputs(new Concat<T, T>(inputPorts));
        }
    }

    /// <summary>
    /// Takes two streams and outputs one stream formed from the two input streams
    /// by first emitting all of the elements from the first stream and then emitting
    /// all of the elements from the second stream.
    /// 
    /// A <see cref="Concat{T,TMat}"/> has one multiple <see cref="In"/> ports and one <see cref="Out"/> port.
    /// <para>
    /// Emits when the current stream has an element available; if the current input completes, it tries the next one
    /// </para>
    /// Backpressures when downstream backpressures
    /// <para>
    /// Completes when all upstreams complete
    /// </para>
    /// Cancels when downstream cancels
    /// </summary>
    /// <typeparam name="TIn">TBD</typeparam>
    /// <typeparam name="TOut">TBD</typeparam>
    public class Concat<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic

        private sealed class Logic : OutGraphStageLogic
        {
            private readonly Concat<TIn, TOut> _stage;
            private int _activeStream;

            public Logic(Concat<TIn, TOut> stage) : base(stage.Shape)
            {
                _stage = stage;
                var iidx = 0;
                var inEnumerator = stage.Shape.Ins.GetEnumerator();
                while (inEnumerator.MoveNext())
                {
                    var i = inEnumerator.Current;
                    var idx = iidx;
                    SetHandler(i,
                        onPush: () => Push(stage.Out, Grab(i)),
                        onUpstreamFinish: () =>
                        {
                            if (idx == _activeStream)
                            {
                                _activeStream++;
                                // skip closed inputs
                                while (_activeStream < stage._inputPorts && IsClosed(stage.In(_activeStream))) _activeStream++;
                                if (_activeStream == stage._inputPorts) CompleteStage();
                                else if (IsAvailable(stage.Out)) Pull(stage.In(_activeStream));
                            }
                        });
                    iidx++;
                }

                SetHandler(stage.Out, this);
            }

            public override void OnPull() => Pull(_stage.In(_activeStream));
        }

        #endregion

        private readonly int _inputPorts;

        /// <summary>
        /// Initializes a new instance of the <see cref="Concat{TIn, TOut}"/> class.
        /// </summary>
        /// <param name="inputPorts">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="inputPorts"/> is less than or equal to one.
        /// </exception>
        public Concat(int inputPorts = 2)
        {
            if (inputPorts <= 1) throw new ArgumentException("A Concat must have more than 1 input port", nameof(inputPorts));
            _inputPorts = inputPorts;

            var inlets = new Inlet<TIn>[inputPorts];
            for (var i = 0; i < inputPorts; i++)
                inlets[i] = new Inlet<TIn>("Concat.in" + i);

            Shape = new UniformFanInShape<TIn, TOut>(Out, inlets);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public Inlet<TIn> In(int id) => Shape.In(id);

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Concat.out");

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Concat");

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanInShape<TIn, TOut> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
    }


    /// <summary>
    /// TBD
    /// </summary>
    public static class OrElse
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <returns>TBD</returns>
        public static IGraph<UniformFanInShape<T, T>, NotUsed> Create<T>() => new OrElse<T>();
    }

    /// <summary>
    /// Takes two streams and passes the first through, the secondary stream is only passed
    /// through if the primary stream completes without passing any elements through. When
    /// the first element is passed through from the primary the secondary is cancelled.
    /// Both incoming streams are materialized when the stage is materialized.
    ///
    /// On errors the stage is failed regardless of source of the error.
    ///
    /// '''Emits when''' element is available from primary stream or the primary stream closed without emitting any elements and an element
    ///                  is available from the secondary stream
    ///
    /// '''Backpressures when''' downstream backpressures
    ///
    /// '''Completes when''' the primary stream completes after emitting at least one element, when the primary stream completes
    ///                      without emitting and the secondary stream already has completed or when the secondary stream completes
    ///
    /// '''Cancels when''' downstream cancels
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class OrElse<T> : GraphStage<UniformFanInShape<T, T>>
    {
        #region Logic

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly OrElse<T> _stage;
            private Inlet<T> _currentIn;
            private bool _primaryPushed;

            public Logic(OrElse<T> stage):base(stage.Shape)
            {
                _stage = stage;
                _currentIn = stage.Primary;

                SetHandler(stage.Out, this);
                SetHandler(stage.Primary, this);

                SetHandler(stage.Secondary,
                    onPush: () => Push(stage.Out, Grab(stage.Secondary)),
                    onUpstreamFinish: () =>
                    {
                        if (IsClosed(stage.Primary))
                            CompleteStage();
                    });
            }

            public override void OnPush()
            {
                // for the primary inHandler
                if (!_primaryPushed)
                {
                    _primaryPushed = true;
                    Cancel(_stage.Secondary);
                }

                var element = Grab(_stage.Primary);
                Push(_stage.Out, element);
            }

            public override void OnUpstreamFinish()
            {
                // for the primary inHandler
                if (!_primaryPushed && !IsClosed(_stage.Secondary))
                {
                    _currentIn = _stage.Secondary;
                    if (IsAvailable(_stage.Out))
                        Pull(_stage.Secondary);
                }
                else
                    CompleteStage();
            }

            public override void OnPull() => Pull(_currentIn);
        }

        #endregion

        /// <summary>
        /// Initializes a new instance of the <see cref="OrElse{T}"/> class.
        /// </summary>
        public OrElse() => Shape = new UniformFanInShape<T, T>(Out, Primary, Secondary);

        /// <summary>
        /// TBD
        /// </summary>
        protected override Attributes InitialAttributes { get; } = DefaultAttributes.OrElse;

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> Primary { get; }   = new Inlet<T>("OrElse.primary");

        /// <summary>
        /// TBD
        /// </summary>
        public Inlet<T> Secondary { get; } = new Inlet<T>("OrElse.secondary");

        /// <summary>
        /// TBD
        /// </summary>
        public Outlet<T> Out { get; } = new Outlet<T>("OrElse.out");

        /// <summary>
        /// TBD
        /// </summary>
        public override UniformFanInShape<T, T> Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "OrElse";
    }
}
