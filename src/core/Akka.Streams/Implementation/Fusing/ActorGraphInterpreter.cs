using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Stage;
using Akka.Util;

namespace Akka.Streams.Implementation.Fusing
{
    internal class GraphModule : Module
    {
        public readonly IModule[] MaterializedValueIds;
        public readonly GraphAssembly Assembly;
        private readonly Shape _shape;
        private readonly Attributes _attributes;

        public GraphModule(GraphAssembly assembly, Shape shape, Attributes attributes, IModule[] materializedValueIds)
        {
            Assembly = assembly;
            _shape = shape;
            _attributes = attributes;
            MaterializedValueIds = materializedValueIds;
        }

        public override Shape Shape { get { return _shape; } }
        public override IModule ReplaceShape(Shape shape)
        {
            return new CopiedModule(shape, _attributes, this);
        }

        public override IImmutableSet<IModule> SubModules { get { return ImmutableHashSet<IModule>.Empty; } }
        public override IModule CarbonCopy()
        {
            return ReplaceShape(Shape.DeepCopy());
        }

        public override Attributes Attributes { get { return _attributes; } }
        public override IModule WithAttributes(Attributes attributes)
        {
            return new GraphModule(Assembly, _shape, attributes, MaterializedValueIds);
        }
    }

    internal sealed class GraphInterpreterShell
    {
        private readonly GraphAssembly _assembly;
        private readonly InHandler[] _inHandlers;
        private readonly OutHandler[] _outHandlers;
        private readonly GraphStageLogic[] _logics;
        private readonly Shape _shape;
        private readonly ActorMaterializerSettings _settings;
        private readonly ActorMaterializerImpl _materializer;

        /// <summary>
        /// Limits the number of events processed by the interpreter before scheduling
        /// a self-message for fairness with other actors. The basic assumption here is
        /// to give each input buffer slot a chance to run through the whole pipeline
        /// and back (for the demand).
        /// </summary>
        private readonly int _eventLimit;

        // Limits the number of events processed by the interpreter on an abort event.
        private readonly int _abortLimit;
        private readonly ActorGraphInterpreter.BatchingActorInputBoundary<object>[] _inputs;
        private readonly ActorGraphInterpreter.ActorOutputBoundary<object>[] _outputs;

        private ILoggingAdapter _log;
        private GraphInterpreter _interpreter;
        private int _subscribersPending;
        private int _publishersPending;
        private bool _resumeScheduled = false;
        private bool _waitingForShutdown = false;

        private readonly ActorGraphInterpreter.Resume _resume;

        public GraphInterpreterShell(GraphAssembly assembly, InHandler[] inHandlers, OutHandler[] outHandlers, GraphStageLogic[] logics, Shape shape, ActorMaterializerSettings settings, ActorMaterializerImpl materializer)
        {
            _assembly = assembly;
            _inHandlers = inHandlers;
            _outHandlers = outHandlers;
            _logics = logics;
            _shape = shape;
            _settings = settings;
            _materializer = materializer;

            _inputs = new ActorGraphInterpreter.BatchingActorInputBoundary<object>[shape.Inlets.Count()];
            _outputs = new ActorGraphInterpreter.ActorOutputBoundary<object>[shape.Outlets.Count()];
            _subscribersPending = _inputs.Length;
            _publishersPending = _outputs.Length;
            _eventLimit = settings.MaxInputBufferSize * (assembly.Inlets.Length + assembly.Outlets.Length);
            _abortLimit = _eventLimit * 2;

            _resume = new ActorGraphInterpreter.Resume(this);

            IsTerminated = false;
        }

        public bool IsTerminated { get; private set; }
        public bool CanShutdown { get { return _subscribersPending + _publishersPending == 0; } }
        public IActorRef Self { get; private set; }
        public ILoggingAdapter Log { get { return _log ?? (_log = GetLogger()); } }
        public GraphInterpreter Interpreter { get { return _interpreter ?? (_interpreter = GetInterpreter()); } }

        public void Init(IActorRef self, Func<GraphInterpreterShell, IActorRef> registerShell)
        {
            this.Self = self;
            for (int i = 0; i < _inputs.Length; i++)
            {
                var input = new ActorGraphInterpreter.BatchingActorInputBoundary<object>(_settings.MaxInputBufferSize, i);
                _inputs[i] = input;
                _interpreter.AttachUpstreamBoundary(i, input);
            }

            var offset = _assembly.ConnectionCount - _outputs.Length;
            for (int i = 0; i < _outputs.Length; i++)
            {
                var output = new ActorGraphInterpreter.ActorOutputBoundary<object>(Self, this, i);
                _outputs[i] = output;
                _interpreter.AttachDownstreamBoundary<object>(i + offset, output);
            }

            _interpreter.Init(new SubFusingActorMaterializerImpl(_materializer, registerShell));
            RunBatch();
        }

        public void Receive(ActorGraphInterpreter.IBoundaryEvent e)
        {
            if (_waitingForShutdown)
            {
                e.Match()
                    .With<ActorGraphInterpreter.ExposedPublisher<object>>(exposed =>
                    {
                        _outputs[exposed.Id].ExposedPublisher(exposed.Publisher);
                        _publishersPending--;
                        if (CanShutdown) IsTerminated = true;
                    })
                    .With<ActorGraphInterpreter.OnSubscribe>(onSubscribe =>
                    {
                        ReactiveStreamsCompliance.TryCancel(onSubscribe.Subscription);
                        _subscribersPending--;
                        if (CanShutdown) IsTerminated = true;
                    })
                    .With<ActorGraphInterpreter.Abort>(abort =>
                    {
                        TryAbort(new TimeoutException(string.Format(
                            "Streaming actor has been already stopped processing (normally), but not all of its inputs or outputs have been subscribed in [{0}]. Aborting actor now.",
                            _settings.SubscriptionTimeoutSettings.Timeout)));
                    });
            }
            else
            {
                e.Match()
                    .With<ActorGraphInterpreter.OnNext>(next =>
                    {
                        // Cases that are most likely on the hot path, in decreasing order of frequency
                        _inputs[next.Id].OnNext(next.Event);
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.RequestMore>(request =>
                    {
                        _outputs[request.Id].RequestMore(request.Demand);
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.Resume>(resume =>
                    {
                        _resumeScheduled = false;
                        if (_interpreter.IsSuspended) RunBatch();
                    })
                    .With<ActorGraphInterpreter.AsyncInput>(asyncInput =>
                    {
                        if (!_interpreter.IsStageCompleted(asyncInput.Logic))
                        {
                            try
                            {
                                asyncInput.Handler(asyncInput.Event);
                            }
                            catch (Exception cause)
                            {
                                asyncInput.Logic.FailStage(cause);
                            }
                            _interpreter.AfterStageHasRun(asyncInput.Logic);
                        }
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.OnError>(error =>
                    {
                        _inputs[error.Id].OnError(error.Cause);
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.OnComplete>(complete =>
                    {
                        _inputs[complete.Id].OnComplete();
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.OnSubscribe>(subscribe =>
                    {
                        _subscribersPending--;
                        _inputs[subscribe.Id].OnSubscribe(subscribe.Subscription);
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.Cancel>(cancel =>
                    {
                        _outputs[cancel.Id].Cancel();
                        RunBatch();
                    })
                    .With<ActorGraphInterpreter.SubscribePending>(pending =>
                    {
                        _outputs[pending.Id].SubscribePending();
                    })
                    .With<ActorGraphInterpreter.ExposedPublisher<object>>(exposed =>
                    {
                        _publishersPending--;
                        _outputs[exposed.Id].ExposedPublisher(exposed.Publisher);
                    });
            }
        }

        /**
         * Attempts to abort execution, by first propagating the reason given until either
         *  - the interpreter successfully finishes
         *  - the event limit is reached
         *  - a new error is encountered
         */
        public void TryAbort(Exception reason)
        {
            // This should handle termination while interpreter is running. If the upstream have been closed already this
            // call has no effect and therefore do the right thing: nothing.
            try
            {
                foreach (var input in _inputs)
                    input.OnInternalError(reason);

                _interpreter.Execute(_abortLimit);
                _interpreter.Finish();
            }
            catch (Exception) { /* swallow? */ }
            finally
            {
                IsTerminated = true;
                // Will only have an effect if the above call to the interpreter failed to emit a proper failure to the downstream
                // otherwise this will have no effect
                foreach (var output in _outputs) output.Fail(reason);
                foreach (var input in _inputs) input.Cancel();
            }
        }

        private void RunBatch()
        {
            try
            {
                var effectiveLimit = !_settings.IsFuzzingMode
                    ? _eventLimit
                    : ((ThreadLocalRandom.Current.Next() % 2 == 0)
                        ? (Thread.Yield() ? 1 : 0)
                        : ThreadLocalRandom.Current.Next(2));  // 1 or 0 events to be processed

                _interpreter.Execute(effectiveLimit);
                if (_interpreter.IsCompleted)
                {
                    // Cannot stop right away if not completely subscribed
                    if (CanShutdown) IsTerminated = true;
                    else
                    {
                        _waitingForShutdown = true;
                        _materializer.ScheduleOnce(_settings.SubscriptionTimeoutSettings.Timeout, () => Self.Tell(new ActorGraphInterpreter.Abort(this)));
                    }
                }
                else if (_interpreter.IsSuspended && !_resumeScheduled)
                {
                    _resumeScheduled = true;
                    Self.Tell(_resume);
                }
            }
            catch (Exception reason)
            {
                TryAbort(reason);
            }
        }

        private GraphInterpreter GetInterpreter()
        {
            return new GraphInterpreter(_assembly, _materializer, Log, _inHandlers, _outHandlers, _logics, (logic, @event, handler) =>
                Self.Tell(new ActorGraphInterpreter.AsyncInput(this, logic, @event, handler)), _settings.IsFuzzingMode);
        }

        private BusLogging GetLogger()
        {
            return new BusLogging(_materializer.System.EventStream, Self.ToString(), typeof(GraphInterpreterShell), new DefaultLogMessageFormatter());
        }
    }

    internal class ActorGraphInterpreter : ActorBase
    {
        #region messages

        public interface IBoundaryEvent : INoSerializationVerificationNeeded
        {
            GraphInterpreterShell Shell { get; }
        }

        public struct OnError : IBoundaryEvent
        {
            public readonly int Id;
            public readonly Exception Cause;
            public OnError(GraphInterpreterShell shell, int id, Exception cause)
            {
                Shell = shell;
                Id = id;
                Cause = cause;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct OnComplete : IBoundaryEvent
        {
            public readonly int Id;
            public OnComplete(GraphInterpreterShell shell, int id)
            {
                Shell = shell;
                Id = id;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct OnNext : IBoundaryEvent
        {
            public readonly int Id;
            public readonly object Event;
            public OnNext(GraphInterpreterShell shell, int id, object @event)
            {
                Shell = shell;
                Id = id;
                Event = @event;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct OnSubscribe : IBoundaryEvent
        {
            public readonly int Id;
            public readonly ISubscription Subscription;
            public OnSubscribe(GraphInterpreterShell shell, int id, ISubscription subscription)
            {
                Shell = shell;
                Id = id;
                Subscription = subscription;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct RequestMore : IBoundaryEvent
        {
            public readonly int Id;
            public readonly long Demand;
            public RequestMore(GraphInterpreterShell shell, int id, long demand)
            {
                Shell = shell;
                Id = id;
                Demand = demand;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct Cancel : IBoundaryEvent
        {
            public readonly int Id;
            public Cancel(GraphInterpreterShell shell, int id)
            {
                Shell = shell;
                Id = id;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct SubscribePending : IBoundaryEvent
        {
            public readonly int Id;
            public SubscribePending(GraphInterpreterShell shell, int id)
            {
                Shell = shell;
                Id = id;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct ExposedPublisher<T> : IBoundaryEvent
        {
            public readonly int Id;
            public readonly ActorPublisher<T> Publisher;
            public ExposedPublisher(GraphInterpreterShell shell, int id, ActorPublisher<T> publisher)
            {
                Shell = shell;
                Id = id;
                Publisher = publisher;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct AsyncInput : IBoundaryEvent
        {
            public readonly GraphStageLogic Logic;
            public readonly object Event;
            public readonly Action<object> Handler;
            public AsyncInput(GraphInterpreterShell shell, GraphStageLogic logic, object @event, Action<object> handler)
            {
                Shell = shell;
                Logic = logic;
                Event = @event;
                Handler = handler;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct Resume : IBoundaryEvent
        {
            public Resume(GraphInterpreterShell shell)
            {
                Shell = shell;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }

        public struct Abort : IBoundaryEvent
        {
            public Abort(GraphInterpreterShell shell)
            {
                Shell = shell;
            }

            public GraphInterpreterShell Shell { get; private set; }
        }
        #endregion

        #region internal classes

        public sealed class BoundarySubscriber<T> : ISubscriber<T>
        {
            private readonly IActorRef _parent;
            private readonly GraphInterpreterShell _shell;
            private readonly int _id;

            public BoundarySubscriber(IActorRef parent, GraphInterpreterShell shell, int id)
            {
                _parent = parent;
                _shell = shell;
                _id = id;
            }

            public void OnSubscribe(ISubscription subscription)
            {
                ReactiveStreamsCompliance.RequireNonNullSubscription(subscription);
                _parent.Tell(new OnSubscribe(_shell, _id, subscription));
            }

            public void OnError(Exception cause)
            {
                ReactiveStreamsCompliance.RequireNonNullException(cause);
                _parent.Tell(new OnError(_shell, _id, cause));
            }

            public void OnComplete()
            {
                _parent.Tell(new OnComplete(_shell, _id));
            }

            void ISubscriber.OnNext(object element)
            {
                OnNext((T)element);
            }

            public void OnNext(T element)
            {
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                _parent.Tell(new OnNext(_shell, _id, element));
            }
        }

        public sealed class BoundaryPublisher<T> : ActorPublisher<T>
        {
            public BoundaryPublisher(IActorRef impl, GraphInterpreterShell shell, int id) : base(impl)
            {
                _wakeUpMessage = new SubscribePending(shell, id);
            }

            private SubscribePending _wakeUpMessage;
            protected override object WakeUpMessage { get { return _wakeUpMessage; } }
        }

        public sealed class BoundarySubscription : ISubscription
        {
            private readonly IActorRef _parent;
            private readonly GraphInterpreterShell _shell;
            private readonly int _id;

            public BoundarySubscription(IActorRef parent, GraphInterpreterShell shell, int id)
            {
                _parent = parent;
                _shell = shell;
                _id = id;
            }

            public void Request(long n)
            {
                _parent.Tell(new RequestMore(_shell, _id, n));
            }

            public void Cancel()
            {
                _parent.Tell(new Cancel(_shell, _id));
            }
        }

        public class BatchingActorInputBoundary<T> : GraphInterpreter.UpstreamBoundaryStageLogic<T>
        {
            #region OutHandler
            private sealed class AnonymousOutHandler : OutHandler
            {
                private readonly BatchingActorInputBoundary<T> _that;

                public AnonymousOutHandler(BatchingActorInputBoundary<T> that)
                {
                    _that = that;
                }

                public override void OnPull()
                {
                    var elementsCount = _that._inputBufferElements;
                    var upstreamCompleted = _that._upstreamCompleted;
                    var outlet = _that.Out;
                    if (elementsCount > 1) _that.Push(outlet, _that.Dequeue());
                    else if (elementsCount == 1)
                    {
                        if (upstreamCompleted)
                        {
                            _that.Push(outlet, _that.Dequeue());
                            _that.Complete(outlet);
                        }
                        else _that.Push(outlet, _that.Dequeue());
                    }
                    else if (upstreamCompleted) _that.Complete(outlet);
                }

                public override void OnDownstreamFinish()
                {
                    _that.Cancel();
                }
            }
            #endregion

            private readonly int _size;
            private readonly int _id;

            private readonly object[] _inputBuffer;
            private readonly int _indexMask;

            private ISubscription _upstream;
            private int _inputBufferElements = 0;
            private int _nextInputElementCursor = 0;
            private bool _upstreamCompleted = false;
            private bool _downstreamCanceled = false;
            private int _batchRemaining;

            public BatchingActorInputBoundary(int size, int id)
            {
                if (size <= 0) throw new ArgumentException("Buffer size cannot be zero", "size");
                if ((size & (size - 1)) != 0) throw new ArgumentException("Buffer size must be power of two", "size");

                _size = size;
                _id = id;
                _inputBuffer = new object[size];
                _indexMask = size - 1;
                _batchRemaining = RequestBatchSize;
                _outlet = new Outlet<T>("UpstreamBoundary" + id) { Id = 0 };

                SetHandler(_outlet, new AnonymousOutHandler(this));
            }

            protected int RequestBatchSize { get { return Math.Max(1, _inputBuffer.Length / 2); } }

            private readonly Outlet<T> _outlet;
            public override Outlet<T> Out { get { return _outlet; } }

            // Call this when an error happens that does not come from the usual onError channel
            // (exceptions while calling RS interfaces, abrupt termination etc)
            public void OnInternalError(Exception reason)
            {
                if (!(_upstreamCompleted || _downstreamCanceled) && !ReferenceEquals(_upstream, null))
                {
                    _upstream.Cancel();
                }

                if (!IsClosed(Out)) OnError(reason);
            }

            public void OnError(Exception reason)
            {
                if (!_upstreamCompleted || !_downstreamCanceled)
                {
                    _upstreamCompleted = true;
                    Clear();
                    Fail(Out, reason);
                }
            }

            public void OnComplete()
            {
                if (!_upstreamCompleted)
                {
                    _upstreamCompleted = true;
                    if (_inputBufferElements == 0) Complete(Out);
                }
            }

            public void OnSubscribe(ISubscription subscription)
            {
                if (subscription == null) throw new ArgumentException("Subscription cannot be null");
                if (_upstreamCompleted) ReactiveStreamsCompliance.TryCancel(subscription);
                else if (_downstreamCanceled)
                {
                    _upstreamCompleted = true;
                    ReactiveStreamsCompliance.TryCancel(subscription);
                }
                else
                {
                    _upstream = subscription;
                    // prefetch
                    ReactiveStreamsCompliance.TryRequest(_upstream, _inputBuffer.Length);
                }
            }

            public void OnNext(T element)
            {
                if (!_upstreamCompleted)
                {
                    if (_inputBufferElements == _size) throw new IllegalStateException("Input buffer overrun");
                    _inputBuffer[(_nextInputElementCursor + _inputBufferElements) & _indexMask] = element;
                    _inputBufferElements++;
                    if (IsAvailable(Out)) Push(Out, Dequeue());
                }
            }

            public void Cancel()
            {
                _downstreamCanceled = true;
                if (!_upstreamCompleted)
                {
                    _upstreamCompleted = true;
                    if (!ReferenceEquals(_upstream, null)) ReactiveStreamsCompliance.TryCancel(_upstream);
                    Clear();
                }
            }

            private T Dequeue()
            {
                var element = (T)_inputBuffer[_nextInputElementCursor];
                if (element == null) throw new IllegalStateException("Internal queue must never contain a null");
                _inputBuffer[_nextInputElementCursor] = null;

                _batchRemaining--;
                if (_batchRemaining == 0 && !_upstreamCompleted)
                {
                    ReactiveStreamsCompliance.TryRequest(_upstream, RequestBatchSize);
                    _batchRemaining = RequestBatchSize;
                }

                _inputBufferElements--;
                _nextInputElementCursor = (_nextInputElementCursor + 1) & _indexMask;
                return element;
            }

            private void Clear()
            {
                _inputBuffer.Initialize();
                _inputBufferElements = 0;
            }
        }

        public class ActorOutputBoundary<T> : GraphInterpreter.DownstreamBoundaryStageLogic<T>
        {
            #region InHandler
            private sealed class AnonymousInHandler : InHandler
            {
                private readonly ActorOutputBoundary<T> _that;

                public AnonymousInHandler(ActorOutputBoundary<T> that)
                {
                    _that = that;
                }

                public override void OnPush()
                {
                    var inlet = _that.In;
                    _that.OnNext(_that.Grab(inlet));
                    if (_that._downstreamCompleted) _that.Cancel(inlet);
                    else if (_that._downstreamDemand > 0) _that.Pull(inlet);
                }

                public override void OnUpstreamFinish()
                {
                    _that.Complete();
                }

                public override void OnUpstreamFailure(Exception e)
                {
                    _that.Fail(e);
                }
            }
            #endregion

            private readonly IActorRef _aref;
            private readonly GraphInterpreterShell _shell;
            private readonly int _id;

            private ActorPublisher<T> _exposedPublisher;
            private ISubscriber<T> _subscriber;
            private long _downstreamDemand = 0L;

            // This flag is only used if complete/fail is called externally since this op turns into a Finished one inside the
            // interpreter (i.e. inside this op this flag has no effects since if it is completed the op will not be invoked)
            private bool _downstreamCompleted = false;
            // when upstream failed before we got the exposed publisher
            private Exception _upstreamFailed = null;
            private bool _upstreamCompleted = false;

            public ActorOutputBoundary(IActorRef aref, GraphInterpreterShell shell, int id)
            {
                _aref = aref;
                _shell = shell;
                _id = id;

                _inlet = new Inlet<T>("UpstreamBoundary" + id) { Id = 0 };
                SetHandler(_inlet, new AnonymousInHandler(this));
            }

            private readonly Inlet<T> _inlet;
            public override Inlet<T> In { get { return _inlet; } }

            public void RequestMore(long demand)
            {
                if (demand < 1)
                {
                    Cancel(In);
                    Fail(ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException);
                }
                else
                {
                    _downstreamDemand += demand;
                    if (_downstreamDemand < 0)
                        _downstreamDemand = long.MaxValue; // Long overflow, Reactive Streams Spec 3:17: effectively unbounded
                    if (!HasBeenPulled(In) && !IsClosed(In)) Pull(In);
                }
            }

            public void SubscribePending()
            {
                foreach (var subscriber in _exposedPublisher.TakePendingSubscribers())
                {
                    if (ReferenceEquals(_subscriber, null))
                    {
                        _subscriber = subscriber;
                        ReactiveStreamsCompliance.TryOnSubscribe(_subscriber, new BoundarySubscription(_aref, _shell, _id));
                    }
                    else ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, GetType().FullName);
                }
            }

            public void ExposedPublisher(ActorPublisher<T> publisher)
            {
                if (_upstreamFailed != null) publisher.Shutdown(_upstreamFailed);
                else
                {
                    if (_upstreamCompleted) publisher.Shutdown(null);
                    else _exposedPublisher = publisher;
                }
            }

            public void Cancel()
            {
                _downstreamCompleted = true;
                _subscriber = null;
                _exposedPublisher.Shutdown(new NormalShutdownException("UpstreamBoundary"));
                Cancel(In);
            }

            public void Fail(Exception reason)
            {
                // No need to fail if had already been cancelled, or we closed earlier
                if (!(_downstreamCompleted || _upstreamCompleted))
                {
                    _upstreamCompleted = true;
                    _upstreamFailed = reason;

                    if (!ReferenceEquals(_exposedPublisher, null)) _exposedPublisher.Shutdown(null);
                    if (!(ReferenceEquals(_subscriber, null)) && !(reason is ISpecViolation)) ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                }
            }

            private void OnNext(T element)
            {
                _downstreamDemand--;
                ReactiveStreamsCompliance.TryOnNext(_subscriber, element);
            }

            private void Complete()
            {
                // No need to complete if had already been cancelled, or we closed earlier
                if (!(_upstreamCompleted | _downstreamCompleted))
                {
                    _upstreamCompleted = true;
                    if (!ReferenceEquals(_exposedPublisher, null)) _exposedPublisher.Shutdown(null);
                    if (!(ReferenceEquals(_subscriber, null))) ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                }
            }
        }

        #endregion

        public static Props Props(GraphInterpreterShell shell)
        {
            return Actor.Props.Create(() => new ActorGraphInterpreter(shell));
        }

        private readonly ISet<GraphInterpreterShell> _activeInterpreters = new HashSet<GraphInterpreterShell>();

        private ActorGraphInterpreter(GraphInterpreterShell shell)
        {
            _activeInterpreters.Add(shell);
        }

        public IActorRef RegisterShell(GraphInterpreterShell shell)
        {
            shell.Init(Self, RegisterShell);
            _activeInterpreters.Add(shell);
            return Self;
        }

        protected override bool Receive(object message)
        {
            if (message is IBoundaryEvent)
            {
                var b = message as IBoundaryEvent;
                var shell = b.Shell;
                shell.Receive(b);
                if (shell.IsTerminated)
                {
                    _activeInterpreters.Remove(shell);
                    if (_activeInterpreters.Count == 0) Context.Stop(Self);
                }

                return true;
            }

            return false;
        }

        protected override void PreStart()
        {
            foreach (var shell in _activeInterpreters)
            {
                shell.Init(Self, RegisterShell);
            }
        }

        protected override void PostStop()
        {
            foreach (var shell in _activeInterpreters)
            {
                shell.TryAbort(new AbruptTerminationException(Self));
            }

            base.PostStop();
        }
    }
}