using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reactive.Streams;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Stages;
using Akka.Streams.Stage;
using Akka.Streams.Util;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FlattenMerge<T, TMat> : GraphStage<FlowShape<IGraph<SourceShape<T>, TMat>, T>>
    {
        #region internal classes

        private sealed class Logic : GraphStageLogic
        {
            private readonly FlattenMerge<T, TMat> _stage;
            private readonly HashSet<SubSinkInlet<T>> _sources = new HashSet<SubSinkInlet<T>>();
            private IBuffer<SubSinkInlet<T>> _q;
            private readonly Action _outHandler;

            public Logic(FlattenMerge<T, TMat> stage) : base(stage.Shape)
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
            {
                _q = Buffer.Create<SubSinkInlet<T>>(_stage._breadth, Interpreter.Materializer);
            }

            public override void PostStop()
            {
                foreach (var source in _sources)
                {
                    source.Cancel();
                }
            }

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

        private readonly Inlet<IGraph<SourceShape<T>, TMat>> _in = new Inlet<IGraph<SourceShape<T>, TMat>>("flatten.in");
        private readonly Outlet<T> _out = new Outlet<T>("flatten.out");

        private readonly int _breadth;

        public FlattenMerge(int breadth)
        {
            _breadth = breadth;

            InitialAttributes = DefaultAttributes.FlattenMerge;
            Shape = new FlowShape<IGraph<SourceShape<T>, TMat>, T>(_in, _out);
        }

        protected override Attributes InitialAttributes { get; }

        public override FlowShape<IGraph<SourceShape<T>, TMat>, T> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString()
        {
            return $"FlattenMerge({_breadth})";
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class PrefixAndTail<T> : GraphStage<FlowShape<T, Tuple<IImmutableList<T>, Source<T, Unit>>>>
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

                _tailSource.Timeout(timeout);
                if (_tailSource.IsClosed)
                    CompleteStage();
            }

            private bool IsPrefixComplete => ReferenceEquals(_builder, null);

            private Source<T, Unit> OpenSubstream()
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
                    if(!_tailSource.IsClosed)
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
        private readonly Outlet<Tuple<IImmutableList<T>, Source<T, Unit>>> _out = new Outlet<Tuple<IImmutableList<T>, Source<T, Unit>>>("PrefixAndTail.out");

        public PrefixAndTail(int count)
        {
            _count = count;

            Shape = new FlowShape<T, Tuple<IImmutableList<T>, Source<T, Unit>>>(_in, _out);
        }

        protected override Attributes InitialAttributes { get; } = DefaultAttributes.PrefixAndTail;

        public override FlowShape<T, Tuple<IImmutableList<T>, Source<T, Unit>>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        public override string ToString() => $"PrefixAndTail({_count})";
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

        public static IGraph<FlowShape<T, Source<T, Unit>>, Unit> When<T>(Func<T, bool> p, SubstreamCancelStrategy substreamCancelStrategy) => new Split<T>(SplitDecision.SplitBefore, p, substreamCancelStrategy);


        public static IGraph<FlowShape<T, Source<T, Unit>>, Unit> After<T>(Func<T, bool> p, SubstreamCancelStrategy substreamCancelStrategy) => new Split<T>(SplitDecision.SplitAfter, p, substreamCancelStrategy);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal sealed class Split<T> : GraphStage<FlowShape<T, Source<T, Unit>>>
    {
        #region internal classes

        private sealed class SplitLogic : TimerGraphStageLogic
        {
            #region internal classes 

            private sealed class SubstreamHandler : InAndOutHandler
            {
                private readonly SplitLogic _logic;
                private bool _willCompleteAfterInitialElement;

                public SubstreamHandler(SplitLogic logic)
                {
                    _logic = logic;
                }

                public bool HasInitialElement => FirstElement != null;

                public Option<T> FirstElement { get; } = new Option<T>();

                private void CloseThis(SubstreamHandler handler, T currentElem)
                {
                    var decision = _logic._split._decision;
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
                        handler.FirstElement.Value = currentElem;
                        if (!_logic._substreamCancelled)
                            _logic._substreamSource.Complete();
                    }
                }

                public override void OnPull()
                {
                    if (HasInitialElement)
                    {
                        _logic._substreamSource.Push(FirstElement.Value);
                        FirstElement.Reset();
                        _logic.SetKeepGoing(false);

                        if (_willCompleteAfterInitialElement)
                        {
                            _logic._substreamSource.Complete();
                            _logic.CompleteStage();
                        }
                    }
                    else
                        _logic.Pull(_logic._split._in);
                }

                public override void OnDownstreamFinish()
                {
                    _logic._substreamCancelled = true;
                    if (_logic.IsClosed(_logic._split._in) || _logic._split._propagateSubstreamCancel)
                        _logic.CompleteStage();
                    else
                    // Start draining
                        if (!_logic.HasBeenPulled(_logic._split._in))
                            _logic.Pull(_logic._split._in);
                }

                public override void OnPush()
                {
                    var elem = _logic.Grab(_logic._split._in);
                    try
                    {
                        if (_logic._split._p(elem))
                        {
                            var handler = new SubstreamHandler(_logic);
                            CloseThis(handler, elem);
                            _logic.HandOver(handler);
                        }
                        else
                        {
                            // Drain into the void
                            if (_logic._substreamCancelled)
                                _logic.Pull(_logic._split._in);
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
            private bool _substreamPushed;
            private bool _substreamCancelled;
            private readonly Split<T> _split;

            public SplitLogic(Split<T> split) : base(split.Shape)
            {
                _split = split;
                SetHandler(split._out, onPull: () =>
                {
                    if (_substreamSource == null)
                        Pull(split._in);
                    else if (!_substreamPushed)
                    {
                        Push(split._out, Source.FromGraph(_substreamSource.Source));
                        ScheduleOnce(SubscriptionTimer, _timeout);
                        _substreamPushed = true;
                    }
                }, onDownstreamFinish: () =>
                {
                    // If the substream is already cancelled or it has not been handed out, we can go away
                    if (!_substreamPushed || _substreamCancelled)
                        CompleteStage();
                });

                // initial input handler
                SetHandler(split._in, onPush: () =>
                {
                    var handler = new SubstreamHandler(this);
                    var elem = Grab(_split._in);

                    if (_split._decision == Split.SplitDecision.SplitAfter)
                    {
                        if (_split._p(elem))
                            Push(_split._out, Source.Single(elem));
                    }
                    // Next pull will come from the next substream that we will open
                    else
                        handler.FirstElement.Value = elem;

                    HandOver(handler);
                }, onUpstreamFinish: CompleteStage);
            }

            public override void PreStart() => _timeout = ActorMaterializer.Downcast(Interpreter.Materializer).Settings.SubscriptionTimeoutSettings.Timeout;

            private void HandOver(SubstreamHandler handler)
            {
                if (IsClosed(_split._out))
                    CompleteStage();
                else
                {
                    _substreamSource = new SubSourceOutlet<T>(this, "SplitSource");
                    _substreamSource.SetHandler(handler);
                    _substreamCancelled = false;
                    SetHandler(_split._in, handler);
                    SetKeepGoing(handler.HasInitialElement);

                    if (IsAvailable(_split._out))
                    {
                        Push(_split._out, Source.FromGraph(_substreamSource.Source));
                        ScheduleOnce(SubscriptionTimer, _timeout);
                        _substreamPushed = true;
                    }
                    else
                        _substreamPushed = false;
                }
            }

            protected internal override void OnTimer(object timerKey) => _substreamSource.Timeout(_timeout);
        }

        #endregion

        private readonly Inlet<T> _in = new Inlet<T>("Split.in");
        private readonly Outlet<Source<T, Unit>> _out = new Outlet<Source<T, Unit>>("Split.out");

        private readonly Split.SplitDecision _decision;
        private readonly Func<T, bool> _p;
        private readonly bool _propagateSubstreamCancel;

        public Split(Split.SplitDecision decision, Func<T, bool> p, SubstreamCancelStrategy substreamCancelStrategy)
        {
            _decision = decision;
            _p = p;
            _propagateSubstreamCancel = substreamCancelStrategy == SubstreamCancelStrategy.Propagate;

            Shape = new FlowShape<T, Source<T, Unit>>(_in, _out);
        }

        public override FlowShape<T, Source<T, Unit>> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new SplitLogic(this);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal static class SubSink
    {
        internal interface ICommand { }

        internal class RequestOne : ICommand
        {
            public static readonly RequestOne Instance = new RequestOne();
            private RequestOne() { }
        }

        internal class Cancel : ICommand
        {
            public static readonly Cancel Instance = new Cancel();
            private Cancel() { }
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

                SetHandler(stage._in,
                    onPush: () => _stage._externalCallback(new OnNext(Grab(_stage._in))),
                    onUpstreamFinish: () => _stage._externalCallback(OnComplete.Instance),
                    onUpstreamFailure: ex => _stage._externalCallback(new OnError(ex)));
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
                {
                    FailStage(new IllegalStateException("Substream Source cannot be materialized more than once"));
                }
            }

            public override void PreStart()
            {
                var ourOwnCallback = GetAsyncCallback<SubSink.ICommand>(cmd =>
                {
                    if (cmd is SubSink.RequestOne)
                    {
                            TryPull(_stage._in);
                    }
                    else if (cmd is SubSink.Cancel)
                    {
                        CompleteStage();
                    }
                    else
                    {
                        throw new IllegalStateException("Bug");
                    }
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
            {
                f(SubSink.RequestOne.Instance);
            }
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
                Unit _;
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

                SetHandler(stage._out,
                    onPull: () => _stage.ExternalCallback(SubSink.RequestOne.Instance),
                    onDownstreamFinish: () => _stage.ExternalCallback(SubSink.Cancel.Instance));
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