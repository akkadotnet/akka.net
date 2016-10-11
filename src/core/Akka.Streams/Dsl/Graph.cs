//-----------------------------------------------------------------------
// <copyright file="Graph.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

                SetHandler(outlet, onPull: () =>
                {
                    if (IsPending)
                        DequeueAndDispatch();
                });
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

        public Merge(int inputPorts, bool eagerComplete = false)
        {
            // one input might seem counter intuitive but saves us from special handling in other places
            if (inputPorts < 1) throw new ArgumentException("Merge must have one or more input ports");
            _inputPorts = inputPorts;
            _eagerComplete = eagerComplete;

            var ins = ImmutableArray<Inlet<TIn>>.Empty.ToBuilder();
            for (var i = 0; i < inputPorts; i++)
                ins.Add(new Inlet<TIn>("Merge.in" + i));

            Out = new Outlet<TOut>("Merge.out");
            Shape = new UniformFanInShape<TIn, TOut>(Out, ins.ToArray());
        }

        public Inlet<TIn> In(int id) => Shape.In(id);

        public Outlet<TOut> Out { get; }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Merge");

        public override UniformFanInShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

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
        }

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("MergePreferred");

        public override MergePreferredShape Shape { get; }

        public Inlet<T> In(int id) => Shape.In(id);

        public Outlet<T> Out => Shape.Out;

        public Inlet<T> Preferred => Shape.Preferred;

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);
    }

    public static class Interleave
    {
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
    public sealed class Interleave<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic

        private sealed class Logic : GraphStageLogic
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

                SetHandler(_stage.Out, onPull: () =>
                {
                    if (!HasBeenPulled(CurrentUpstream))
                        TryPull(CurrentUpstream);
                });
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

        public Outlet<TOut> Out { get; }

        public Inlet<TIn> In(int id) => Shape.In(id); 

        public override UniformFanInShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

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
    public sealed class MergeSorted<T> : GraphStage<FanInShape<T, T, T>>
    {
        #region stage logic

        private sealed class Logic : GraphStageLogic
        {
            private readonly MergeSorted<T> _stage;
            private T _other;

            private readonly Action<T> _dispatchRight;
            private readonly Action<T> _dispatchLeft;
            private readonly Action _passRight;
            private readonly Action _passLeft;
            private readonly Action _readRight;
            private readonly Action _readLeft;

            public Logic(MergeSorted<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _dispatchRight = right => Dispatch(_other, right);
                _dispatchLeft = left => Dispatch(left, _other);
                _passRight = () => Emit(_stage.Out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage.Right, _stage.Out, doPull: true);
                });
                _passLeft = () => Emit(_stage.Out, _other, () =>
                {
                    NullOut();
                    PassAlong(_stage.Left, _stage.Out, doPull: true);
                });
                _readRight = () => Read(_stage.Right, _dispatchRight, _passLeft);
                _readLeft = () => Read(_stage.Left, _dispatchLeft, _passRight);

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

            private void NullOut()
            {
                _other = default(T);
            }

            private void Dispatch(T left, T right)
            {
                if (_stage._compare(left, right) == -1)
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

        public MergeSorted(Func<T, T, int> compare)
        {
            _compare = compare;
            Shape = new FanInShape<T, T, T>(Out, Left, Right);
        }

        public readonly Inlet<T> Left = new Inlet<T>("left");

        public readonly Inlet<T> Right = new Inlet<T>("right");

        public readonly Outlet<T> Out = new Outlet<T>("out");

        public override FanInShape<T, T, T> Shape { get; }

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
                    var enumerator = stage.Shape.Outlets.GetEnumerator();
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

            private void TryPull()
            {
                if (_pendingCount == 0 && !HasBeenPulled(_stage.In)) Pull(_stage.In);
            }
        }
        #endregion

        private readonly int _outputPorts;
        private readonly bool _eagerCancel;

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

        public readonly Inlet<T> In = new Inlet<T>("Broadcast.in");

        public Outlet<T> Out(int id) => Shape.Out(id);

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Broadcast");

        public override UniformFanOutShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

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
    public sealed class Partition<T> : GraphStage<UniformFanOutShape<T, T>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private object _outPendingElement;
            private int _outPendingIndex;
            private int _downstreamRunning;

            public Logic(Partition<T> stage) : base(stage.Shape)
            {
                _downstreamRunning = stage._outputPorts;

                SetHandler(stage.In, onPush: () =>
                {
                    var element = Grab(stage.In);
                    var index = stage._partitioner(element);
                    if (index < 0 || index >= stage._outputPorts)
                        FailStage(
                            new PartitionOutOfBoundsException(
                                $"partitioner must return an index in the range [0,{stage._outputPorts - 1}]. returned: [{index}] for input [{element.GetType().Name}]."));
                    else if (!IsClosed(stage.Out(index)))
                    {
                        if (IsAvailable(stage.Out(index)))
                        {
                            Push(stage.Out(index), element);
                            if (stage.Shape.Outlets.Any(IsAvailable))
                                Pull(stage.In);
                        }
                        else
                        {
                            _outPendingElement = element;
                            _outPendingIndex = index;
                        }
                    }
                    else if (stage.Shape.Outlets.Any(IsAvailable))
                        Pull(stage.In);
                }, onUpstreamFinish: () =>
                {
                    if(_outPendingElement == null)
                        CompleteStage();
                });

                for (var i = 0; i < stage._outputPorts; i++)
                {
                    var output = stage.Shape.Outlets[i];
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
                        _downstreamRunning--;
                        if(_downstreamRunning == 0)
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
        }

        #endregion

        private readonly int _outputPorts;
        private readonly Func<T, int> _partitioner;

        public Partition(int outputPorts, Func<T, int> partitioner)
        {
            _outputPorts = outputPorts;
            _partitioner = partitioner;
            var outlets = Enumerable.Range(0, outputPorts).Select(i => new Outlet<T>("Partition.out" + i)).ToArray();
            Shape = new UniformFanOutShape<T, T>(In, outlets);
        }

        public readonly Inlet<T> In = new Inlet<T>("Partition.in");

        public Outlet Out(int id) => Shape.Out(id);

        public override UniformFanOutShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => $"Partition({_outputPorts})";

    }

    public sealed class PartitionOutOfBoundsException : Exception
    {
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

                foreach (var outlet in _stage.Shape.Outs)
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

            var outlets = new Outlet<T>[outputPorts];
            for (var i = 0; i < outputPorts; i++)
                outlets[i] = new Outlet<T>("Balance.out" + i);
            
            Shape = new UniformFanOutShape<T, T>(In, outlets);
        }

        public Inlet<T> In { get; } = new Inlet<T>("Balance.in");

        public Outlet<T> Out(int id) => Shape.Out(id);

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Balance");

        public override UniformFanOutShape<T, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);

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
    public sealed class Zip<T1, T2> : ZipWith<T1, T2, Tuple<T1, T2>>
    {
        public Zip() : base((a, b) => new Tuple<T1, T2>(a, b)) { }

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
    /// Backpressures when any of the outputs backpressures
    /// <para>
    /// Completes when upstream completes
    /// </para>
    /// Cancels when any downstream cancels
    /// </summary>
    public sealed class UnZip<T1, T2> : UnzipWith<KeyValuePair<T1, T2>, T1, T2>
    {
        public UnZip() : base(kv => Tuple.Create(kv.Key, kv.Value)) { }

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
        public static readonly UnzipWith Instance = new UnzipWith();
        private UnzipWith() { }
    }

    public static class ZipN
    {
        /// <summary>
        /// Create a new <see cref="ZipN{T}"/>.
        /// </summary>
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
    public sealed class ZipN<T> : ZipWithN<T, IImmutableList<T>>
    {
        public ZipN(int n) : base(x => x, n)
        {
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ZipN;

        public override string ToString() => "ZipN";
    }

    public static class ZipWithN
    {
        /// <summary>
        /// Creates a new <see cref="ZipWithN{TIn,TOut}"/>
        /// </summary>
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
    public class ZipWithN<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>>
    {
        #region Logic 

        private sealed class Logic : GraphStageLogic
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

                SetHandler(stage.Out, onPull: () =>
                {
                    _pending += stage._n;
                    if(_pending == 0)
                        PushAll();
                });
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
        
        public ZipWithN(Func<IImmutableList<TIn>, TOut> zipper, int n)
        {
            _zipper = zipper;
            _n = n;
            Shape = new UniformFanInShape<TIn, TOut>(n);
            Out = Shape.Out;
            Inlets = Shape.Ins;
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.ZipWithN;

        public Outlet<TOut> Out { get; }

        public IImmutableList<Inlet<TIn>> Inlets { get; }

        public Inlet<TIn> In(int i) => Inlets[i];

        public override UniformFanInShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "ZipWithN";
    }

    public static class Concat
    {
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
    public class Concat<TIn, TOut> : GraphStage<UniformFanInShape<TIn, TOut>> where TIn : TOut
    {
        #region stage logic

        private sealed class Logic : GraphStageLogic
        {
            public Logic(Shape shape, Concat<TIn, TOut> stage) : base(shape)
            {
                var activeStream = 0;
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
                            if (idx == activeStream)
                            {
                                activeStream++;
                                // skip closed inputs
                                while (activeStream < stage._inputPorts && IsClosed(stage.In(activeStream))) activeStream++;
                                if (activeStream == stage._inputPorts) CompleteStage();
                                else if (IsAvailable(stage.Out)) Pull(stage.In(activeStream));
                            }
                        });
                    iidx++;
                }

                SetHandler(stage.Out, onPull: () => Pull(stage.In(activeStream)));
            }
        }

        #endregion

        private readonly int _inputPorts;

        public Concat(int inputPorts = 2)
        {
            if (inputPorts <= 1) throw new ArgumentException("A Concat must have more than 1 input port");
            _inputPorts = inputPorts;

            var inlets = new Inlet<TIn>[inputPorts];
            for (var i = 0; i < inputPorts; i++)
                inlets[i] = new Inlet<TIn>("Concat.in" + i);

            Shape = new UniformFanInShape<TIn, TOut>(Out, inlets);
        }

        public Inlet<TIn> In(int id) => Shape.In(id);

        public Outlet<TOut> Out { get; } = new Outlet<TOut>("Concat.out");

        protected override Attributes InitialAttributes { get; } = Attributes.CreateName("Concat");

        public override UniformFanInShape<TIn, TOut> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(Shape, this);
    }
}