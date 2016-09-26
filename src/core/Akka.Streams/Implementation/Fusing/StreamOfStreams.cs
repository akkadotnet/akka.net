//-----------------------------------------------------------------------
// <copyright file="StreamOfStreams.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Supervision;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.Fusing
{
    internal sealed class FlattenMerge<TGraph, T, TMat> : GraphStage<FlowShape<TGraph, T>> where TGraph : IGraph<SourceShape<T>, TMat>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly FlattenMerge<TGraph, T, TMat> _stage;
            private readonly HashSet<SubSinkInlet<T>> _sources = new HashSet<SubSinkInlet<T>>();
            private IBuffer<SubSinkInlet<T>> _q;
            private readonly Action _outHandler;

            public Logic(FlattenMerge<TGraph, T, TMat> stage) : base(stage.Shape)
            {
                _stage = stage;
                _outHandler = () =>
                {
                    // could be unavailable due to async input having been executed before this notification
                    if (_q.NonEmpty && IsAvailable(_stage._out))
                        PushOut();
                };

                SetHandler(_stage._in,
                    onPush: () =>
                    {
                        var source = Grab(_stage._in);
                        AddSource(source);
                        if (ActiveSources < _stage._breadth)
                            TryPull(_stage._in);
                    },
                    onUpstreamFinish: () =>
                    {
                        if (ActiveSources == 0)
                            CompleteStage();
                    });

                SetHandler(_stage._out,
                    onPull: () =>
                    {
                        Pull(_stage._in);
                        SetHandler(_stage._out, _outHandler);
                    });
            }

            private int ActiveSources => _sources.Count;

            public override void PreStart()
                => _q = Buffer.Create<SubSinkInlet<T>>(_stage._breadth, Interpreter.Materializer);

            public override void PostStop() => _sources.ForEach(s => s.Cancel());

            private void PushOut()
            {
                var src = _q.Dequeue();
                Push(_stage._out, src.Grab());
                if (!src.IsClosed)
                    src.Pull();
                else
                    RemoveSource(src);
            }

            private void RemoveSource(SubSinkInlet<T> src)
            {
                var pullSuppressed = ActiveSources == _stage._breadth;
                _sources.Remove(src);

                if (pullSuppressed)
                    TryPull(_stage._in);
                if (ActiveSources == 0 && IsClosed(_stage._in))
                    CompleteStage();
            }

            private void AddSource(IGraph<SourceShape<T>, TMat> source)
            {
                var sinkIn = CreateSubSinkInlet<T>("FlattenMergeSink");
                sinkIn.SetHandler(new LambdaInHandler(
                    onPush: () =>
                    {
                        if (IsAvailable(_stage._out))
                        {
                            Push(_stage._out, sinkIn.Grab());
                            sinkIn.Pull();
                        }
                        else
                            _q.Enqueue(sinkIn);
                    },
                    onUpstreamFinish: () =>
                    {
                        if (!sinkIn.IsAvailable)
                            RemoveSource(sinkIn);
                    }));

                sinkIn.Pull();
                _sources.Add(sinkIn);
                Source.FromGraph(source).RunWith(sinkIn.Sink, Interpreter.SubFusingMaterializer);
            }

            public override string ToString() => $"FlattenMerge({_stage._breadth})";
        }

        #endregion

        private readonly Inlet<TGraph> _in = new Inlet<TGraph>("flatten.in");
        private readonly Outlet<T> _out = new Outlet<T>("flatten.out");

        private readonly int _breadth;

        public FlattenMerge(int breadth)
        {
            _breadth = breadth;

            InitialAttributes = DefaultAttributes.FlattenMerge;
            Shape = new FlowShape<TGraph, T>(_in, _out);
        }

        protected override Attributes InitialAttributes { get; }

        public override FlowShape<TGraph, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => $"FlattenMerge({_breadth})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class PrefixAndTail<T> : GraphStage<FlowShape<T, Tuple<IImmutableList<T>, Source<T, NotUsed>>>>
    {
        #region internal classes
        
        private sealed class Logic : TimerGraphStageLogic
        {
            private const string SubscriptionTimer = "SubstreamSubscriptionTimer";

            private readonly PrefixAndTail<T> _stage;
            private readonly LambdaOutHandler _subHandler;
            private int _left;
            private ImmutableList<T>.Builder _builder;
            private SubSourceOutlet<T> _tailSource;

            public Logic(PrefixAndTail<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                _left = _stage._count < 0 ? 0 : _stage._count;
                _builder = ImmutableList<T>.Empty.ToBuilder();

                _subHandler = new LambdaOutHandler(onPull: () =>
                {
                    SetKeepGoing(false);
                    CancelTimer(SubscriptionTimer);
                    Pull(_stage._in);
                    _tailSource.SetHandler(new LambdaOutHandler(onPull: () => Pull(_stage._in)));
                });
                
                SetHandler(_stage._in,
                    onPush: OnPush,
                    onUpstreamFinish: OnUpstreamFinish,
                    onUpstreamFailure: OnUpstreamFailure);

                SetHandler(_stage._out,
                    onPull: OnPull,
                    onDownstreamFinish: OnDownstreamFinish);
            }
            
            protected internal override void OnTimer(object timerKey)
            {
                var materializer = ActorMaterializer.Downcast(Interpreter.Materializer);
                var timeoutSettings = materializer.Settings.SubscriptionTimeoutSettings;
                var timeout = timeoutSettings.Timeout;

                switch (timeoutSettings.Mode)
                {
                    case StreamSubscriptionTimeoutTerminationMode.NoopTermination:
                        //do nothing
                        break;
                    case StreamSubscriptionTimeoutTerminationMode.WarnTermination:
                        materializer.Logger.Warning(
                            $"Substream subscription timeout triggered after {timeout} in prefixAndTail({_stage._count}).");
                        break;
                    case StreamSubscriptionTimeoutTerminationMode.CancelTermination:
                        _tailSource.Timeout(timeout);
                        if(_tailSource.IsClosed)
                            CompleteStage();
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            private bool IsPrefixComplete => ReferenceEquals(_builder, null);

            private Source<T, NotUsed> OpenSubstream()
            {
                var timeout = ActorMaterializer.Downcast(Interpreter.Materializer).Settings.SubscriptionTimeoutSettings.Timeout;
                _tailSource = new SubSourceOutlet<T>(this, "TailSource");
                _tailSource.SetHandler(_subHandler);
                SetKeepGoing(true);
                ScheduleOnce(SubscriptionTimer, timeout);
                _builder = null;
                return Source.FromGraph(_tailSource.Source);
            }

            private void OnPush()
            {
                if (IsPrefixComplete)
                    _tailSource.Push(Grab(_stage._in));
                else
                {
                    _builder.Add(Grab(_stage._in));
                    _left--;
                    if (_left == 0)
                    {
                        Push(_stage._out, Tuple.Create((IImmutableList<T>) _builder.ToImmutable(), OpenSubstream()));
                        Complete(_stage._out);
                    }
                    else
                        Pull(_stage._in);
                }
            }

            private void OnPull()
            {
                if (_left == 0)
                {
                    Push(_stage._out, Tuple.Create((IImmutableList<T>) ImmutableList<T>.Empty, OpenSubstream()));
                    Complete(_stage._out);
                }
                else
                    Pull(_stage._in);
            }

            private void OnUpstreamFinish()
            {
                if (!IsPrefixComplete)
                {
                    // This handles the unpulled out case as well
                    Emit(_stage._out, Tuple.Create((IImmutableList<T>) _builder.ToImmutable(), Source.Empty<T>()), CompleteStage);
                }
                else
                {
                    if (!_tailSource.IsClosed)
                        _tailSource.Complete();
                    CompleteStage();
                }
            }

            private void OnUpstreamFailure(Exception ex)
            {
                if (IsPrefixComplete)
                {
                    if (!_tailSource.IsClosed)
                        _tailSource.Fail(ex);
                    CompleteStage();
                }
                else
                    FailStage(ex);
            }

            private void OnDownstreamFinish()
            {
                if (!IsPrefixComplete)
                    CompleteStage();
                // Otherwise substream is open, ignore
            }
        }

        #endregion

        private readonly int _count;
        private readonly Inlet<T> _in = new Inlet<T>("PrefixAndTail.in");
        private readonly Outlet<Tuple<IImmutableList<T>, Source<T, NotUsed>>> _out = new Outlet<Tuple<IImmutableList<T>, Source<T, NotUsed>>>("PrefixAndTail.out");

        public PrefixAndTail(int count)
        {
            _count = count;

            Shape = new FlowShape<T, Tuple<IImmutableList<T>, Source<T, NotUsed>>>(_in, _out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.PrefixAndTail;

        public override FlowShape<T, Tuple<IImmutableList<T>, Source<T, NotUsed>>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => $"PrefixAndTail({_count})";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class GroupBy<T, TKey> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
    {
        #region Loigc 

        private sealed class Logic : TimerGraphStageLogic
        {
            private readonly GroupBy<T, TKey> _stage;
            private readonly Dictionary<TKey, SubstreamSource> _activeSubstreams = new Dictionary<TKey, SubstreamSource>();
            private readonly HashSet<TKey> _closedSubstreams = new HashSet<TKey>();
            private readonly HashSet<SubstreamSource> _substreamsJustStarted = new HashSet<SubstreamSource>();
            private readonly Lazy<Decider> _decider;
            private TimeSpan _timeout;
            private SubstreamSource _substreamWaitingToBePushed;
            private Option<TKey> _nextElementKey = Option<TKey>.None;
            private Option<T> _nextElementValue = Option<T>.None;
            private long _nextId;
            private int _firstPushCounter;


            public Logic(GroupBy<T, TKey> stage, Attributes inheritedAttributes) : base(stage.Shape)
            {
                _stage = stage;
                
                _decider = new Lazy<Decider>(() =>
                {
                    var attribute = inheritedAttributes.GetAttribute<ActorAttributes.SupervisionStrategy>(null);
                    return attribute != null ? attribute.Decider : Deciders.StoppingDecider; ;
                }); 

                SetHandler(_stage.In, onPush: () =>
                {
                    try
                    {
                        var element = Grab(_stage.In);
                        var key = _stage._keyFor(element);
                        if (key == null)
                            throw new ArgumentNullException(nameof(key), "Key cannot be null");
                       
                        if (_activeSubstreams.ContainsKey(key))
                        {
                            var substreamSource = _activeSubstreams[key];
                            if (substreamSource.IsAvailable)
                                substreamSource.Push(element);
                            else
                            {
                                _nextElementKey = key;
                                _nextElementValue = element;
                            }
                        }
                        else
                        {
                            if (_activeSubstreams.Count == _stage._maxSubstreams)
                                Fail(new IllegalStateException($"Cannot open substream for key {key}: too many substreams open"));
                            else if (_closedSubstreams.Contains(key) && !HasBeenPulled(_stage.In))
                                Pull(_stage.In);
                            else
                                RunSubstream(key, element);
                        }
                    }
                    catch (Exception ex)
                    {
                        var directive = _decider.Value(ex);
                        if (directive == Directive.Stop)
                            Fail(ex);
                        else if (!HasBeenPulled(_stage.In))
                            Pull(_stage.In);
                    }
                }, onUpstreamFinish: () =>
                {
                    if (!TryCompleteAll())
                        SetKeepGoing(true);
                }, onUpstreamFailure: Fail);

                SetHandler(_stage.Out, onPull: () =>
                {
                    if (_substreamWaitingToBePushed != null)
                    {
                        Push(_stage.Out, Source.FromGraph(_substreamWaitingToBePushed.Source));
                        ScheduleOnce(_substreamWaitingToBePushed.Key.Value, _timeout);
                        _substreamWaitingToBePushed = null;
                    }
                    else
                    {
                        if (HasNextElement)
                        {
                            var subSubstreamSource = _activeSubstreams[_nextElementKey.Value];
                            if (subSubstreamSource.IsAvailable)
                            {
                                subSubstreamSource.Push(_nextElementValue.Value);
                                ClearNextElement();
                            }
                        }
                        else
                            TryPull(_stage.In);
                    }
                }, 
                onDownstreamFinish: () =>
                {
                    if (_activeSubstreams.Count == 0)
                        CompleteStage();
                    else
                        SetKeepGoing(true);
                });
            }
            
            private long NextId => ++_nextId;

            private bool HasNextElement => _nextElementKey.HasValue;

            private void ClearNextElement()
            {
                _nextElementKey = Option<TKey>.None;
                _nextElementValue = Option<T>.None;
            }

            private bool TryCompleteAll()
            {
                if (_activeSubstreams.Count == 0 || (!HasNextElement && _firstPushCounter == 0))
                {
                    foreach (var value in _activeSubstreams.Values)
                        value.Complete();
                    CompleteStage();
                    return true;
                }

                return false;
            }

            private void Fail(Exception ex)
            {
                foreach (var value in _activeSubstreams.Values)
                    value.Fail(ex);

                FailStage(ex);
            }

            private bool NeedToPull => !(HasBeenPulled(_stage.In) || IsClosed(_stage.In) || HasNextElement);

            public override void PreStart()
            {
                var settings = ActorMaterializer.Downcast(Interpreter.Materializer).Settings;
                _timeout = settings.SubscriptionTimeoutSettings.Timeout;
            }

            protected internal override void OnTimer(object timerKey)
            {
                var key = (TKey) timerKey;
                if (_activeSubstreams.ContainsKey(key))
                {
                    var substreamSource = _activeSubstreams[key];
                    substreamSource.Timeout(_timeout);
                    _closedSubstreams.Add(key);
                    _activeSubstreams.Remove(key);
                    if (IsClosed(_stage.In))
                        TryCompleteAll();
                }
            }

            private void RunSubstream(TKey key, T value)
            {
                var substreamSource = new SubstreamSource(this, "GroupBySource " + NextId, key, value);
                _activeSubstreams.Add(key, substreamSource);
                _firstPushCounter++;
                if (IsAvailable(_stage.Out))
                {
                    Push(_stage.Out, Source.FromGraph(substreamSource.Source));
                    ScheduleOnce(key, _timeout);
                    _substreamWaitingToBePushed = null;
                }
                else
                {
                    SetKeepGoing(true);
                    _substreamsJustStarted.Add(substreamSource);
                    _substreamWaitingToBePushed = substreamSource;
                }
            }

            private sealed class SubstreamSource : SubSourceOutlet<T>, IOutHandler
            {
                private readonly Logic _logic;
                private Option<T> _firstElement;

                public SubstreamSource(Logic logic, string name, Option<TKey> key, Option<T> firstElement) : base(logic, name)
                {
                    _logic = logic;
                    _firstElement = firstElement;
                    Key = key;

                    SetHandler(this);
                }

                private bool FirstPush => _firstElement.HasValue;

                private bool HasNextForSubSource => _logic.HasNextElement && _logic._nextElementKey.Equals(Key);

                public Option<TKey> Key { get; }

                private void CompleteSubStream()
                {
                    Complete();
                    _logic._activeSubstreams.Remove(Key.Value);
                    _logic._closedSubstreams.Add(Key.Value);
                }

                private void TryCompleteHandler()
                {
                    if (_logic.IsClosed(_logic._stage.In) && !HasNextForSubSource)
                    {
                        CompleteSubStream();
                        _logic.TryCompleteAll();
                    }
                }

                public void OnPull()
                {
                    _logic.CancelTimer(Key.Value);
                    if (FirstPush)
                    {
                        _logic._firstPushCounter--;
                        Push(_firstElement.Value);
                        _firstElement = Option<T>.None;
                        _logic._substreamsJustStarted.Remove(this);
                        if(_logic._substreamsJustStarted.Count == 0)
                            _logic.SetKeepGoing(false);
                    }
                    else if (HasNextForSubSource)
                    {
                        Push(_logic._nextElementValue.Value);
                        _logic.ClearNextElement();
                    }
                    else if (_logic.NeedToPull)
                        _logic.Pull(_logic._stage.In);

                    TryCompleteHandler();
                }

                public void OnDownstreamFinish()
                {
                    if(_logic.HasNextElement && _logic._nextElementKey.Equals(Key))
                        _logic.ClearNextElement();
                    if (FirstPush)
                        _logic._firstPushCounter--;
                    CompleteSubStream();
                    if (_logic.IsClosed(_logic._stage.In))
                        _logic.TryCompleteAll();
                    else if (_logic.NeedToPull)
                        _logic.Pull(_logic._stage.In);
                }
            }
        }

        #endregion

        private readonly int _maxSubstreams;
        private readonly Func<T, TKey> _keyFor;

        public GroupBy(int maxSubstreams, Func<T, TKey> keyFor)
        {
            _maxSubstreams = maxSubstreams;
            _keyFor = keyFor;
            
            Shape = new FlowShape<T, Source<T, NotUsed>>(In, Out);
        }

        private Inlet<T> In { get; } = new Inlet<T>("GroupBy.in");

        private Outlet<Source<T, NotUsed>> Out { get; } = new Outlet<Source<T, NotUsed>>("GroupBy.out");

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.GroupBy;

        public override FlowShape<T, Source<T, NotUsed>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            => new Logic(this, inheritedAttributes);

        public override string ToString() => "GroupBy";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class Split
    {
        internal enum SplitDecision
        {
            SplitBefore,
            SplitAfter
        }

        public static IGraph<FlowShape<T, Source<T, NotUsed>>, NotUsed> When<T>(Func<T, bool> p, SubstreamCancelStrategy substreamCancelStrategy) => new Split<T>(SplitDecision.SplitBefore, p, substreamCancelStrategy);


        public static IGraph<FlowShape<T, Source<T, NotUsed>>, NotUsed> After<T>(Func<T, bool> p, SubstreamCancelStrategy substreamCancelStrategy) => new Split<T>(SplitDecision.SplitAfter, p, substreamCancelStrategy);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class Split<T> : GraphStage<FlowShape<T, Source<T, NotUsed>>>
    {
        #region internal classes

        private sealed class Logic : TimerGraphStageLogic
        {
            #region internal classes 

            private sealed class SubstreamHandler : InAndOutHandler
            {
                private readonly Logic _logic;
                private bool _willCompleteAfterInitialElement;

                public SubstreamHandler(Logic logic)
                {
                    _logic = logic;
                }

                public bool HasInitialElement => FirstElement.HasValue;

                public Option<T> FirstElement { private get; set; }

                private void CloseThis(SubstreamHandler handler, T currentElem)
                {
                    var decision = _logic._stage._decision;
                    if (decision == Split.SplitDecision.SplitAfter)
                    {
                        if (!_logic._substreamCancelled)
                        {
                            _logic._substreamSource.Push(currentElem);
                            _logic._substreamSource.Complete();
                        }
                    }
                    else if (decision == Split.SplitDecision.SplitBefore)
                    {
                        handler.FirstElement = currentElem;
                        if (!_logic._substreamCancelled)
                            _logic._substreamSource.Complete();
                    }
                }

                public override void OnPull()
                {
                    if (HasInitialElement)
                    {
                        _logic._substreamSource.Push(FirstElement.Value);
                        FirstElement = Option<T>.None;
                        _logic.SetKeepGoing(false);

                        if (_willCompleteAfterInitialElement)
                        {
                            _logic._substreamSource.Complete();
                            _logic.CompleteStage();
                        }
                    }
                    else
                        _logic.Pull(_logic._stage._in);
                }

                public override void OnDownstreamFinish()
                {
                    _logic._substreamCancelled = true;
                    if (_logic.IsClosed(_logic._stage._in) || _logic._stage._propagateSubstreamCancel)
                        _logic.CompleteStage();
                    else
                    // Start draining
                        if (!_logic.HasBeenPulled(_logic._stage._in))
                            _logic.Pull(_logic._stage._in);
                }

                public override void OnPush()
                {
                    var elem = _logic.Grab(_logic._stage._in);
                    try
                    {
                        if (_logic._stage._predicate(elem))
                        {
                            var handler = new SubstreamHandler(_logic);
                            CloseThis(handler, elem);
                            _logic.HandOver(handler);
                        }
                        else
                        {
                            // Drain into the void
                            if (_logic._substreamCancelled)
                                _logic.Pull(_logic._stage._in);
                            else
                                _logic._substreamSource.Push(elem);
                        }
                    }
                    catch (Exception ex)
                    {
                        OnUpstreamFailure(ex);
                    }
                }

                public override void OnUpstreamFinish()
                {
                    if (HasInitialElement)
                        _willCompleteAfterInitialElement = true;
                    else
                    {
                        _logic._substreamSource.Complete();
                        _logic.CompleteStage();
                    }
                }

                public override void OnUpstreamFailure(Exception ex)
                {
                    _logic._substreamSource.Fail(ex);
                    _logic.FailStage(ex);
                }
            }

            #endregion

            private const string SubscriptionTimer = "SubstreamSubscriptionTimer";

            private TimeSpan _timeout;
            private SubSourceOutlet<T> _substreamSource;
            private bool _substreamWaitingToBePushed;
            private bool _substreamCancelled;
            private readonly Split<T> _stage;

            public Logic(Split<T> stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage._out, onPull: () =>
                {
                    if (_substreamSource == null)
                        Pull(stage._in);
                    else if (!_substreamWaitingToBePushed)
                    {
                        Push(stage._out, Source.FromGraph(_substreamSource.Source));
                        ScheduleOnce(SubscriptionTimer, _timeout);
                        _substreamWaitingToBePushed = true;
                    }
                }, onDownstreamFinish: () =>
                {
                    // If the substream is already cancelled or it has not been handed out, we can go away
                    if (!_substreamWaitingToBePushed || _substreamCancelled)
                        CompleteStage();
                });

                // initial input handler
                SetHandler(stage._in, onPush: () =>
                {
                    var handler = new SubstreamHandler(this);
                    var elem = Grab(_stage._in);

                    if (_stage._decision == Split.SplitDecision.SplitAfter && _stage._predicate(elem))
                        Push(_stage._out, Source.Single(elem));
                    // Next pull will come from the next substream that we will open
                    else
                        handler.FirstElement = elem;

                    HandOver(handler);
                }, onUpstreamFinish: CompleteStage);
            }

            public override void PreStart()
            {
                var settings = ActorMaterializer.Downcast(Interpreter.Materializer).Settings;
                _timeout = settings.SubscriptionTimeoutSettings.Timeout;
            }

            private void HandOver(SubstreamHandler handler)
            {
                if (IsClosed(_stage._out))
                    CompleteStage();
                else
                {
                    _substreamSource = new SubSourceOutlet<T>(this, "SplitSource");
                    _substreamSource.SetHandler(handler);
                    _substreamCancelled = false;
                    SetHandler(_stage._in, handler);
                    SetKeepGoing(handler.HasInitialElement);

                    if (IsAvailable(_stage._out))
                    {
                        Push(_stage._out, Source.FromGraph(_substreamSource.Source));
                        ScheduleOnce(SubscriptionTimer, _timeout);
                        _substreamWaitingToBePushed = true;
                    }
                    else
                        _substreamWaitingToBePushed = false;
                }
            }

            protected internal override void OnTimer(object timerKey) => _substreamSource.Timeout(_timeout);
        }

        #endregion

        private readonly Inlet<T> _in = new Inlet<T>("Split.in");
        private readonly Outlet<Source<T, NotUsed>> _out = new Outlet<Source<T, NotUsed>>("Split.out");

        private readonly Split.SplitDecision _decision;
        private readonly Func<T, bool> _predicate;
        private readonly bool _propagateSubstreamCancel;

        public Split(Split.SplitDecision decision, Func<T, bool> predicate, SubstreamCancelStrategy substreamCancelStrategy)
        {
            _decision = decision;
            _predicate = predicate;
            _propagateSubstreamCancel = substreamCancelStrategy == SubstreamCancelStrategy.Propagate;

            Shape = new FlowShape<T, Source<T, NotUsed>>(_in, _out);
        }

        public override FlowShape<T, Source<T, NotUsed>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => "Split";
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class SubSink
    {
        internal interface ICommand
        {
        }

        internal class RequestOne : ICommand
        {
            public static readonly RequestOne Instance = new RequestOne();

            private RequestOne()
            {
            }
        }

        internal class Cancel : ICommand
        {
            public static readonly Cancel Instance = new Cancel();

            private Cancel()
            {
            }
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SubSink<T> : GraphStage<SinkShape<T>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly SubSink<T> _stage;

            public Logic(SubSink<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._in, onPush: () => _stage._externalCallback(new OnNext(Grab(_stage._in))), onUpstreamFinish: () => _stage._externalCallback(OnComplete.Instance), onUpstreamFailure: ex => _stage._externalCallback(new OnError(ex)));
            }

            private void SetCallback(Action<SubSink.ICommand> cb)
            {
                var status = _stage._status.Value;
                if (status == null)
                {
                    if (!_stage._status.CompareAndSet(null, cb))
                        SetCallback(cb);
                }
                else if (status is SubSink.RequestOne)
                {
                    Pull(_stage._in);
                    if (!_stage._status.CompareAndSet(SubSink.RequestOne.Instance, cb))
                        SetCallback(cb);
                }
                else if (status is SubSink.Cancel)
                {
                    CompleteStage();
                    if (!_stage._status.CompareAndSet(SubSink.Cancel.Instance, cb))
                        SetCallback(cb);
                }
                else if (status is Action)
                    FailStage(new IllegalStateException("Substream Source cannot be materialized more than once"));
            }

            public override void PreStart()
            {
                var ourOwnCallback = GetAsyncCallback<SubSink.ICommand>(cmd =>
                {
                    if (cmd is SubSink.RequestOne)
                        TryPull(_stage._in);
                    else if (cmd is SubSink.Cancel)
                        CompleteStage();
                    else
                        throw new IllegalStateException("Bug");
                });
                SetCallback(ourOwnCallback);
            }
        }

        #endregion

        private readonly Inlet<T> _in = new Inlet<T>("SubSink.in");
        private readonly AtomicReference<object> _status = new AtomicReference<object>();
        private readonly string _name;
        private readonly Action<IActorSubscriberMessage> _externalCallback;

        public SubSink(string name, Action<IActorSubscriberMessage> externalCallback)
        {
            _name = name;
            _externalCallback = externalCallback;

            InitialAttributes = Attributes.CreateName($"SubSink({name})");
            Shape = new SinkShape<T>(_in);
        }

        protected override Attributes InitialAttributes { get; }

        public override SinkShape<T> Shape { get; }

        public void PullSubstream()
        {
            var s = _status.Value;
            var f = s as Action<SubSink.ICommand>;

            if (f != null)
                f(SubSink.RequestOne.Instance);
            else
            {
                if (!_status.CompareAndSet(null, SubSink.RequestOne.Instance))
                    ((Action<SubSink.ICommand>) _status.Value)(SubSink.RequestOne.Instance);
            }
        }

        public void CancelSubstream()
        {
            var s = _status.Value;
            var f = s as Action<SubSink.ICommand>;

            if (f != null)
                f(SubSink.Cancel.Instance);
            else if (!_status.CompareAndSet(s, SubSink.Cancel.Instance)) // a potential RequestOne is overwritten
                ((Action<SubSink.ICommand>) _status.Value)(SubSink.Cancel.Instance);
        }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => _name;
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class SubSource
    {
        /// <summary>
        /// INTERNAL API
        /// 
        /// HERE ACTUALLY ARE DRAGONS, YOU HAVE BEEN WARNED!
        /// 
        /// FIXME #19240 (jvm)
        /// </summary>
        public static void Kill<T, TMat>(Source<T, TMat> s)
        {
            var module = s.Module as GraphStageModule;
            if (module?.Stage is SubSource<T>)
            {
                ((SubSource<T>) module.Stage).ExternalCallback(SubSink.Cancel.Instance);
                return;
            }

            var pub = s.Module as PublisherSource<T>;
            if (pub != null)
            {
                NotUsed _;
                pub.Create(default(MaterializationContext), out _).Subscribe(CancelingSubscriber<T>.Instance);
                return;
            }

            var intp = GraphInterpreter.CurrentInterpreterOrNull;
            if (intp == null)
                throw new NotSupportedException($"cannot drop Source of type {s.Module.GetType().Name}");
            s.RunWith(Sink.Ignore<T>(), intp.SubFusingMaterializer);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class SubSource<T> : GraphStage<SourceShape<T>>
    {
        #region internal classes 

        private sealed class Logic : GraphStageLogic
        {
            private readonly SubSource<T> _stage;

            public Logic(SubSource<T> stage) : base(stage.Shape)
            {
                _stage = stage;

                SetHandler(stage._out, onPull: () => _stage.ExternalCallback(SubSink.RequestOne.Instance), onDownstreamFinish: () => _stage.ExternalCallback(SubSink.Cancel.Instance));
            }

            private void SetCallback(Action<IActorSubscriberMessage> callback)
            {
                var status = _stage._status.Value;

                if (status == null)
                {
                    if (!_stage._status.CompareAndSet(null, callback))
                        SetCallback(callback);
                }
                else if (status is OnComplete)
                    CompleteStage();
                else if (status is OnError)
                    FailStage(((OnError) status).Cause);
                else if (status is Action<IActorSubscriberMessage>)
                    throw new IllegalStateException("Substream Source cannot be materialized more than once");
            }

            public override void PreStart()
            {
                var ourOwnCallback = GetAsyncCallback<IActorSubscriberMessage>(msg =>
                {
                    if (msg is OnComplete)
                        CompleteStage();
                    else if (msg is OnError)
                        FailStage(((OnError) msg).Cause);
                    else if (msg is OnNext)
                        Push(_stage._out, (T) ((OnNext) msg).Element);
                });
                SetCallback(ourOwnCallback);
            }
        }

        #endregion

        private readonly string _name;
        private readonly Outlet<T> _out = new Outlet<T>("SubSource.out");
        private readonly AtomicReference<object> _status = new AtomicReference<object>();

        public SubSource(string name, Action<SubSink.ICommand> externalCallback)
        {
            _name = name;

            Shape = new SourceShape<T>(_out);
            InitialAttributes = Attributes.CreateName($"SubSource({name})");
            ExternalCallback = externalCallback;
        }

        public override SourceShape<T> Shape { get; }

        protected override Attributes InitialAttributes { get; }

        internal Action<SubSink.ICommand> ExternalCallback { get; }

        public void PushSubstream(T elem)
        {
            var s = _status.Value;
            var f = s as Action<IActorSubscriberMessage>;

            if (f == null)
                throw new IllegalStateException("cannot push to uninitialized substream");
            f(new OnNext(elem));
        }

        public void CompleteSubstream()
        {
            var s = _status.Value;
            var f = s as Action<IActorSubscriberMessage>;

            if (f != null)
                f(OnComplete.Instance);
            else if (!_status.CompareAndSet(null, OnComplete.Instance))
                ((Action<IActorSubscriberMessage>) _status.Value)(OnComplete.Instance);
        }

        public void FailSubstream(Exception ex)
        {
            var s = _status.Value;
            var f = s as Action<IActorSubscriberMessage>;
            var failure = new OnError(ex);

            if (f != null)
                f(failure);
            else if (!_status.CompareAndSet(null, failure))
                ((Action<IActorSubscriberMessage>) _status.Value)(failure);
        }

        public bool Timeout(TimeSpan d) => _status.CompareAndSet(null, new OnError(new SubscriptionTimeoutException($"Substream Source has not been materialized in {d}")));

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => _name;
    }
}