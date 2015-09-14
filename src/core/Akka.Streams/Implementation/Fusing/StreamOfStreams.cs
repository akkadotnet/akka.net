using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.Fusing
{
    internal sealed class FlattenMerge<T, TMat> : GraphStage<FlowShape<IGraph<SourceShape<T>, TMat>, T>>
    {
        #region internal classes
        private sealed class FlattenStageLogic : GraphStageLogic
        {
            private readonly FlattenMerge<T, TMat> _stage;
            private IImmutableSet<StreamOfStreams.LocalSource<T>> _sources = ImmutableHashSet<StreamOfStreams.LocalSource<T>>.Empty;
            private IQueue _queue = new FixedQueue();

            public FlattenStageLogic(Shape shape, FlattenMerge<T, TMat> stage) : base(shape)
            {
                _stage = stage;

                SetHandler(_stage.In, onPush: () =>
                {
                    var source = Grab(_stage.In);
                    AddSource(source);
                    if (ActiveSources < _stage.Breadth) TryPull(_stage.In);
                },
                onUpstreamFinish: () =>
                {
                    if (ActiveSources == 0) CompleteStage<T>();
                });
                SetHandler(_stage.Out, onPull: () =>
                {
                    Pull(_stage.In);
                    SetHandler(_stage.Out, onPull: () =>
                    {
                        // could be unavailable due to async input having been executed before this notification
                        if (_queue.HasData && IsAvailable(_stage.Out)) PushOut();
                    });
                });
            }

            public int ActiveSources { get { return _sources.Count; } }

            public void PushOut()
            {
                var source = _queue.Dequeue();
                Push(_stage.Out, (T)source.Element);
                source.Element = null;
                if (source.IsActive) source.Pull();
                else RemoveSource(source);
            }

            public void AddSource(IGraph<SourceShape<T>, TMat> source)
            {
                var localSource = new StreamOfStreams.LocalSource<T>();
                _sources = _sources.Add(localSource);
                var subFlow = Source.FromGraph<T, TMat>(source)
                    .RunWith<Task<Action<IActorPublisherMessage>>>(new StreamOfStreams.LocalSink<T>(GetAsyncCallback<IActorSubscriberMessage>(msg =>
                    {
                        msg.Match()
                            .With<OnNext>(next =>
                            {
                                var element = (T)next.Element;
                                if (IsAvailable(_stage.Out))
                                {
                                    Push(_stage.Out, element);
                                    localSource.Pull();
                                }
                                else
                                {
                                    localSource.Element = element;
                                    _queue.Enqueue(localSource);
                                }
                            })
                            .With<OnComplete>(_ =>
                            {
                                localSource.Deactivate();
                                if (localSource.Element == null) RemoveSource(localSource);
                            })
                            .With<OnError>(err => FailStage<T>(err.Cause));
                    })), Interpreter.SubFusingMaterializer);

                localSource.Activate(subFlow);
            }

            public void RemoveSource(StreamOfStreams.LocalSource<T> source)
            {
                var pullSuppressed = ActiveSources == _stage.Breadth;
                _sources = _sources.Remove(source);
                if (pullSuppressed) TryPull(_stage.In);
                if (ActiveSources == 0 && IsClosed(_stage.In)) CompleteStage<T>();
            }

            public override void PostStop()
            {
                foreach (var source in _sources)
                    source.Cancel();
            }
        }

        private interface IQueue
        {
            bool HasData { get; }
            IQueue Enqueue(StreamOfStreams.LocalSource<T> source);
            StreamOfStreams.LocalSource<T> Dequeue();
        }

        private sealed class FixedQueue : IQueue
        {
            private const int Size = 15;
            private const int Mask = 16;

            private readonly StreamOfStreams.LocalSource<T>[] _queue = new StreamOfStreams.LocalSource<T>[Size];
            private int _head = 0;
            private int _tail = 0;

            public bool HasData { get { return _head != _tail; } }

            public IQueue Enqueue(StreamOfStreams.LocalSource<T> source)
            {
                if (_tail - _head == Size)
                {
                    var queue = new DynamicQueue();
                    while (HasData)
                        queue.Enqueue(Dequeue());

                    queue.Enqueue(source);
                    return queue;
                }
                else
                {
                    _queue[_tail & Mask] = source;
                    _tail++;
                    return this;
                }
            }

            public StreamOfStreams.LocalSource<T> Dequeue()
            {
                var result = _queue[_head & Mask];
                _head++;
                return result;
            }
        }

        private sealed class DynamicQueue : LinkedList<StreamOfStreams.LocalSource<T>>, IQueue
        {
            public bool HasData { get { return Count != 0; } }
            public IQueue Enqueue(StreamOfStreams.LocalSource<T> source)
            {
                AddLast(source);
                return this;
            }

            public StreamOfStreams.LocalSource<T> Dequeue()
            {
                var e = this.FirstOrDefault();
                RemoveFirst();
                return e;
            }
        }
        #endregion

        public readonly int Breadth;
        public readonly Inlet<IGraph<SourceShape<T>, TMat>> In = new Inlet<IGraph<SourceShape<T>, TMat>>("flatten.in");
        public readonly Outlet<T> Out = new Outlet<T>("flatten.out");

        public FlattenMerge(int breadth)
        {
            Breadth = breadth;
            InitialAttributes = Attributes.CreateName("FlattenMerge");
            Shape = new FlowShape<IGraph<SourceShape<T>, TMat>, T>(In, Out);
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<IGraph<SourceShape<T>, TMat>, T> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new FlattenStageLogic(Shape, this);
        }
    }

    public static class StreamOfStreams
    {
        private static readonly Request RequestSingle = new Request(1);

        internal sealed class LocalSource<T>
        {
            private Task<Action<IActorPublisherMessage>> _subscriptionTask;
            private Action<IActorPublisherMessage> _subscription;

            public object Element { get; set; }
            public bool IsActive { get { return !ReferenceEquals(_subscription, null); } }

            public void Deactivate()
            {
                _subscription = null;
                _subscriptionTask = null;
            }

            public void Activate(Task<Action<IActorPublisherMessage>> future)
            {
                _subscriptionTask = future;
                /*
                 * The subscription is communicated to the FlattenMerge stage by way of completing
                 * the future. Encoding it like this means that the `sub` field will be written
                 * either by us (if the future has already been completed) or by the LocalSink (when
                 * it eventually completes the future in its `preStart`). The important part is that
                 * either way the `sub` field is populated before we get the first `OnNext` message
                 * and the value is safely published in either case as well (since AsyncCallback is
                 * based on an Actor message send).
                 */
                future.ContinueWith(t => _subscription = t.Result, TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.ExecuteSynchronously);
            }

            public void Pull()
            {
                if (!ReferenceEquals(_subscription, null)) _subscription(RequestSingle);
                else if (ReferenceEquals(_subscriptionTask, null)) throw new IllegalStateException("Not yet initialized, subscription task not set");
                else throw new IllegalStateException("Not yet initialized, subscription task has " + _subscriptionTask.Result);
            }

            public void Cancel()
            {
                if (!ReferenceEquals(_subscriptionTask, null))
                    _subscriptionTask.ContinueWith(t => Actors.Cancel.Instance, TaskContinuationOptions.AttachedToParent | TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        internal sealed class LocalSink<T> : GraphStageWithMaterializedValue<SinkShape<T>, Task<Action<IActorPublisherMessage>>>
        {
            #region internal classes
            private sealed class SinkStageLogic : GraphStageLogic
            {
                private readonly TaskCompletionSource<Action<IActorPublisherMessage>> _subscriptionPromise;
                private readonly LocalSink<T> _stage;

                public SinkStageLogic(Shape shape, TaskCompletionSource<Action<IActorPublisherMessage>> subscriptionPromise, LocalSink<T> stage) : base(shape)
                {
                    _subscriptionPromise = subscriptionPromise;
                    _stage = stage;

                    SetHandler(stage.In, onPush: () => _stage.Notifier(new OnNext(Grab(_stage.In))),
                    onUpstreamFinish: () => _stage.Notifier(OnComplete.Instance),
                    onUpstreamFailure: cause => _stage.Notifier(new OnError(cause)));
                }

                public override void PreStart()
                {
                    Pull(_stage.In);
                    _subscriptionPromise.SetResult(GetAsyncCallback<IActorPublisherMessage>(msg =>
                    {
                        if (msg == RequestSingle) TryPull(_stage.In);
                        else if (msg is Cancel) CompleteStage<T>();
                        else throw new IllegalStateException(string.Format("Invalid message {0} send throug the local sink task", msg.GetType()));
                    }));
                }
            }
            #endregion

            public readonly Action<IActorSubscriberMessage> Notifier;
            public readonly Inlet<T> In = new Inlet<T>("LocalSink.in");

            public LocalSink(Action<IActorSubscriberMessage> notifier)
            {
                Notifier = notifier;
                InitialAttributes = Attributes.CreateName("LocalSink");
                Shape = new SinkShape<T>(In);
            }

            protected override Attributes InitialAttributes { get; }
            public override SinkShape<T> Shape { get; }

            public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<Action<IActorPublisherMessage>> materialized)
            {
                var sub = new TaskCompletionSource<Action<IActorPublisherMessage>>();
                var logic = new SinkStageLogic(Shape, sub, this);
                materialized = sub.Task;
                return logic;
            }
        }
    }

    internal sealed class PrefixAndTail<T> : GraphStage<FlowShape<T, Tuple<IEnumerable<T>, Source<T, Unit>>>>
    {
        #region internal classes

        public const int NotMaterialized = 0;
        public const int AlreadyMaterialized = 1;
        public const int TimedOut = 2;
        public const int NormalCompletion = 3;
        public const int FailureCompletion = 4;

        public interface ITail
        {
            void PushSubstream(T element);
            void CompleteSubstream();
            void FailSubstream(Exception cause);
        }

        private sealed class TailSource : GraphStage<SourceShape<T>>
        {
            private sealed class TailSourceLogic : GraphStageLogic, ITail
            {
                private readonly TailSource _stage;
                private readonly Action<T> _onParentPush;
                private readonly Action<Unit> _onParentFinish;
                private readonly Action<Exception> _onParentFailure;

                public TailSourceLogic(Shape shape, TailSource stage) : base(shape)
                {
                    _stage = stage;
                    _onParentPush = GetAsyncCallback<T>(element => Push(stage.Out, element));
                    _onParentFinish = GetAsyncCallback<Unit>(_ => CompleteStage<T>());
                    _onParentFailure = GetAsyncCallback<Exception>(FailStage<T>);

                    SetHandler(stage.Out,
                        onPull: stage.PullParent,
                        onDownstreamFinish: stage.CancelParent);
                }

                public override void PreStart()
                {
                    var materializedStae = _stage.MaterializationState.GetAndSet(AlreadyMaterialized);
                    switch (materializedStae)
                    {
                        case AlreadyMaterialized:
                            FailStage<T>(new IllegalStateException("Tail source cannot be materialized more than once"));
                            break;
                        case TimedOut:  // already detached from the parent
                            FailStage<T>(new SubscriptionTimeoutException(string.Format("Tail source has not been materialized in {0}", _stage.Timeout)));
                            break;
                        case NormalCompletion:  // already detached from parent
                            CompleteStage<T>();
                            break;
                        case FailureCompletion: // already detached from parent
                            FailStage<T>(_stage.MaterializationFailure);
                            break;
                        case NotMaterialized:
                            _stage.Register(this);
                            break;
                    }
                }

                public void PushSubstream(T element)
                {
                    _onParentPush(element);
                }

                public void CompleteSubstream()
                {
                    _onParentFinish(Unit.Instance);
                }

                public void FailSubstream(Exception cause)
                {
                    _onParentFailure(cause);
                }
            }

            public readonly TimeSpan Timeout;
            public readonly Action<ITail> Register;
            public readonly Action PullParent;
            public readonly Action CancelParent;

            public readonly Outlet<T> Out = new Outlet<T>("Tail.out");
            public AtomicCounter MaterializationState = new AtomicCounter(NotMaterialized);
            public Exception MaterializationFailure = null;

            public TailSource(TimeSpan timeout, Action<ITail> register, Action pullParent, Action cancelParent)
            {
                Timeout = timeout;
                Register = register;
                PullParent = pullParent;
                CancelParent = cancelParent;
                Shape = new SourceShape<T>(Out);
            }

            public override SourceShape<T> Shape { get; }
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
            {
                return new TailSourceLogic(Shape, this);
            }
        }

        private sealed class PrefixAndTailLogic : TimerGraphStageLogic
        {
            private const string SubscriptionTimer = "SubstreamSubscriptionTimer";

            private readonly PrefixAndTail<T> _stage;
            private int _left;
            private List<T> _builder;
            private TailSource _tailSource = null;
            private ITail _tail = null;
            private int? _pendingCompletion = NotMaterialized;
            private Exception _completionFailure = null;

            private readonly Action _onSubstreamPull;
            private readonly Action _onSubstreamFinish;
            private readonly Action<ITail> _onSubstreamRegister;

            public PrefixAndTailLogic(Shape shape, PrefixAndTail<T> stage) : base(shape)
            {
                _stage = stage;
                _left = _stage.Count < 0 ? 0 : _stage.Count;
                _builder = new List<T>(_left);

                _onSubstreamPull = GetAsyncCallback(() => Pull(_stage.In));
                _onSubstreamFinish = GetAsyncCallback(CompleteStage<T>);
                _onSubstreamRegister = GetAsyncCallback<ITail>(tailIf =>
                {
                    _tail = tailIf;
                    CancelTimer(SubscriptionTimer);
                    if (_pendingCompletion == NormalCompletion)
                    {
                        _tail.CompleteSubstream();
                        CompleteStage<T>();
                    }
                    else if (_pendingCompletion == FailureCompletion)
                    {
                        _tail.FailSubstream(_completionFailure);
                        CompleteStage<T>();
                    }
                });

                SetHandler(_stage.In,
                    onPush: OnPush,
                    onUpstreamFinish: OnUpstreamFinish,
                    onUpstreamFailure: OnUpstreamFailure);

                SetHandler(_stage.Out,
                    onPull: OnPull,
                    onDownstreamFinish: OnDownstreamFinish);
            }
            
            // Needs to keep alive if upstream completes but substream has been not yet materialized
            public override bool KeepGoingAfterAllPortsClosed { get { return true; } }

            private bool IsPrefixComplete { get { return ReferenceEquals(_builder, null); } }
            private bool IsWaitingSubstreamRegistration { get { return ReferenceEquals(_tail, null); } }

            protected internal override void OnTimer(object timerKey)
            {
                if (_tailSource.MaterializationState.CompareAndSet(NotMaterialized, TimedOut)) CompleteStage<T>();
            }

            private Source<T, Unit> OpenSubstream()
            {
                var timeout = ActorMaterializer.Downcast(Interpreter.Materializer).Settings.SubscriptionTimeoutSettings.Timeout;
                _tailSource = new TailSource(timeout, _onSubstreamRegister, _onSubstreamPull, _onSubstreamFinish);
                ScheduleOnce(SubscriptionTimer, timeout);
                _builder = null;
                return Source.FromGraph(_tailSource);
            }

            private void OnPush()
            {
                if (IsPrefixComplete) _tail.PushSubstream(Grab(_stage.In));
                else
                {
                    _builder.Add(Grab(_stage.In));
                    _left--;
                    if (_left == 0)
                    {
                        Push(_stage.Out, Tuple.Create(_builder as IEnumerable<T>, OpenSubstream()));
                        Complete(_stage.Out);
                    }
                    else Pull(_stage.In);
                }
            }

            private void OnPull()
            {
                if (_left == 0)
                {
                    Push(_stage.Out, Tuple.Create(Enumerable.Empty<T>(), OpenSubstream()));
                    Complete(_stage.Out);
                }
                else Pull(_stage.In);
            }

            private void OnUpstreamFinish()
            {
                if (!IsPrefixComplete)
                {
                    // This handles the unpulled out case as well
                    Emit(_stage.Out, Tuple.Create(_builder as IEnumerable<T>, Source.Empty<T, Unit>()), CompleteStage<T>);
                }
                else
                {
                    if (IsWaitingSubstreamRegistration)
                    {
                        // Detach if possible.
                        // This allows this stage to complete without waiting for the substream to be materialized, since that
                        // is empty anyway. If it is already being registered (state was not NotMaterialized) then we will be
                        // able to signal completion normally soon.
                        if (_tailSource.MaterializationState.CompareAndSet(NotMaterialized, NormalCompletion))
                            CompleteStage<T>();
                        else _pendingCompletion = NormalCompletion;
                    }
                    else
                    {
                        _tail.CompleteSubstream();
                        CompleteStage<T>();
                    }
                }
            }

            private void OnUpstreamFailure(Exception cause)
            {
                if (IsPrefixComplete)
                {
                    if (IsWaitingSubstreamRegistration)
                    {
                        // Detach if possible.
                        // This allows this stage to complete without waiting for the substream to be materialized, since that
                        // is empty anyway. If it is already being registered (state was not NotMaterialized) then we will be
                        // able to signal completion normally soon.

                        //TODO: on JVM side setting materialization state and exception is one atomic operation, this may cause some races
                        if (_tailSource.MaterializationState.CompareAndSet(NotMaterialized, FailureCompletion))
                        {
                            _tailSource.MaterializationFailure = cause;
                            FailStage<T>(cause);
                        }
                        else
                        {
                            _pendingCompletion = FailureCompletion;
                            _completionFailure = cause;
                        }
                    }
                    else
                    {
                        _tail.FailSubstream(cause);
                        CompleteStage<T>();
                    }
                }
                else FailStage<T>(cause);
            }

            private void OnDownstreamFinish()
            {
                if(!IsPrefixComplete) CompleteStage<T>();
                // Otherwise substream is open, ignore
            }
        }

        #endregion

        public readonly int Count;
        public readonly Inlet<T> In = new Inlet<T>("PrefixAndTail.in");
        public readonly Outlet<Tuple<IEnumerable<T>, Source<T, Unit>>> Out = new Outlet<Tuple<IEnumerable<T>, Source<T, Unit>>>("PrefixAndTail.out");

        public PrefixAndTail(int count)
        {
            Count = count;
            InitialAttributes = Attributes.CreateName("PrefixAndTail");
            Shape = new FlowShape<T, Tuple<IEnumerable<T>, Source<T, Unit>>>(In, Out);
        }

        protected override Attributes InitialAttributes { get; }
        public override FlowShape<T, Tuple<IEnumerable<T>, Source<T, Unit>>> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new PrefixAndTailLogic(Shape, this);
        }
    }
}