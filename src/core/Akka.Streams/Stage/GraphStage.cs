//-----------------------------------------------------------------------
// <copyright file="GraphStage.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Actors;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Util;
using Akka.Util;
using Akka.Util.Internal;
using static Akka.Streams.Implementation.Fusing.GraphInterpreter;

namespace Akka.Streams.Stage
{
    public interface ILogicAndMaterializedValue<out TMaterialized>
    {
        GraphStageLogic Logic { get; }
        TMaterialized MaterializedValue { get; }
    }

    public struct LogicAndMaterializedValue<TMaterialized> : ILogicAndMaterializedValue<TMaterialized>
    {
        public LogicAndMaterializedValue(GraphStageLogic logic, TMaterialized materializedValue)
        {
            Logic = logic;
            MaterializedValue = materializedValue;
        }

        public GraphStageLogic Logic { get; }
        public TMaterialized MaterializedValue { get; }
    }

    public interface IGraphStageWithMaterializedValue<out TShape, out TMaterialized> : IGraph<TShape, TMaterialized> where TShape : Shape
    {
        ILogicAndMaterializedValue<TMaterialized> CreateLogicAndMaterializedValue(Attributes attributes);
    }

    public abstract class GraphStageWithMaterializedValue<TShape, TMaterialized> : IGraphStageWithMaterializedValue<TShape, TMaterialized> where TShape : Shape
    {
        #region anonymous graph class

        private sealed class Graph : IGraph<TShape, TMaterialized>
        {
            public Graph(TShape shape, IModule module, Attributes attributes)
            {
                Shape = shape;
                Module = module.WithAttributes(attributes);
            }

            public TShape Shape { get; }

            public IModule Module { get; }

            public IGraph<TShape, TMaterialized> WithAttributes(Attributes attributes) => new Graph(Shape, Module, attributes);

            public IGraph<TShape, TMaterialized> AddAttributes(Attributes attributes) => WithAttributes(Module.Attributes.And(attributes));

            public IGraph<TShape, TMaterialized> Named(string name) => AddAttributes(Attributes.CreateName(name));

            public IGraph<TShape, TMaterialized> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }

        #endregion

        private readonly Lazy<IModule> _module;

        protected GraphStageWithMaterializedValue()
        {
            _module =
                new Lazy<IModule>(
                    () =>
                        new GraphStageModule(Shape, InitialAttributes,
                            (IGraphStageWithMaterializedValue<Shape, object>) this));
        }

        protected virtual Attributes InitialAttributes => Attributes.None;

        public abstract TShape Shape { get; }

        public IGraph<TShape, TMaterialized> WithAttributes(Attributes attributes) => new Graph(Shape, Module, attributes);

        public abstract ILogicAndMaterializedValue<TMaterialized> CreateLogicAndMaterializedValue(Attributes inheritedAttributes);

        public IModule Module => _module.Value;

        public IGraph<TShape, TMaterialized> AddAttributes(Attributes attributes) => WithAttributes(Module.Attributes.And(attributes));

        public IGraph<TShape, TMaterialized> Named(string name) => AddAttributes(Attributes.CreateName(name));

        public IGraph<TShape, TMaterialized> Async() => AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
    }

    /// <summary>
    /// A GraphStage represents a reusable graph stream processing stage. A GraphStage consists of a <see cref="Shape"/> which describes
    /// its input and output ports and a factory function that creates a <see cref="GraphStageLogic"/> which implements the processing
    /// logic that ties the ports together.
    /// </summary>
    public abstract class GraphStage<TShape> : GraphStageWithMaterializedValue<TShape, NotUsed> where TShape : Shape
    {
        public sealed override ILogicAndMaterializedValue<NotUsed> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
            => new LogicAndMaterializedValue<NotUsed>(CreateLogic(inheritedAttributes), NotUsed.Instance);

        protected abstract GraphStageLogic CreateLogic(Attributes inheritedAttributes);
    }

    public abstract class TimerGraphStageLogic : GraphStageLogic
    {
        private readonly IDictionary<object, TimerMessages.Timer> _keyToTimers = new Dictionary<object, TimerMessages.Timer>();
        private readonly AtomicCounter _timerIdGen = new AtomicCounter(0);
        private Action<TimerMessages.Scheduled> _timerAsyncCallback;

        protected TimerGraphStageLogic(Shape shape) : base(shape)
        {
        }

        private Action<TimerMessages.Scheduled> TimerAsyncCallback
        {
            get
            {
                if (ReferenceEquals(_timerAsyncCallback, null))
                    _timerAsyncCallback = GetAsyncCallback<TimerMessages.Scheduled>(OnInternalTimer);

                return _timerAsyncCallback;
            }
        }

        private void OnInternalTimer(TimerMessages.Scheduled scheduled)
        {
            var id = scheduled.TimerId;
            TimerMessages.Timer timer;
            if (_keyToTimers.TryGetValue(scheduled.TimerKey, out timer) && timer.Id == id)
            {
                if (!scheduled.IsRepeating)
                    _keyToTimers.Remove(scheduled.TimerKey);
                OnTimer(scheduled.TimerKey);
            }
        }

        /// <summary>
        /// Will be called when the scheduled timer is triggered.
        /// </summary>
        protected internal abstract void OnTimer(object timerKey);

        /// <summary>
        /// Schedule timer to call <see cref="OnTimer"/> periodically with the given interval after the specified
        /// initial delay.
        /// Any existing timer with the same key will automatically be canceled before
        /// adding the new timer.
        /// </summary>
        protected internal void ScheduleRepeatedly(object timerKey, TimeSpan initialDelay, TimeSpan interval)
        {
            CancelTimer(timerKey);
            var id = _timerIdGen.IncrementAndGet();
            var task = Interpreter.Materializer.ScheduleRepeatedly(initialDelay, interval, () =>
            {
                TimerAsyncCallback(new TimerMessages.Scheduled(timerKey, id, isRepeating: true));
            });
            _keyToTimers[timerKey] = new TimerMessages.Timer(id, task);
        }

        /// <summary>
        /// Schedule timer to call <see cref="OnTimer"/> periodically with the given interval after the specified
        /// initial delay.
        /// Any existing timer with the same key will automatically be canceled before
        /// adding the new timer.
        /// </summary>
        protected internal void ScheduleRepeatedly(object timerKey, TimeSpan interval)
            => ScheduleRepeatedly(timerKey, interval, interval);

        /// <summary>
        /// Schedule timer to call <see cref="OnTimer"/> after given delay.
        /// Any existing timer with the same key will automatically be canceled before
        /// adding the new timer.
        /// </summary>
        protected internal void ScheduleOnce(object timerKey, TimeSpan delay)
        {
            CancelTimer(timerKey);
            var id = _timerIdGen.IncrementAndGet();
            var task = Interpreter.Materializer.ScheduleOnce(delay, () =>
            {
                TimerAsyncCallback(new TimerMessages.Scheduled(timerKey, id, isRepeating: false));
            });
            _keyToTimers[timerKey] = new TimerMessages.Timer(id, task);
        }

        /// <summary>
        /// Cancel timer, ensuring that the <see cref="OnTimer"/> is not subsequently called.
        /// </summary>
        /// <param name="timerKey">key of the timer to cancel</param>
        protected internal void CancelTimer(object timerKey)
        {
            TimerMessages.Timer timer;
            if (_keyToTimers.TryGetValue(timerKey, out timer))
            {
                timer.Task.Cancel();
                _keyToTimers.Remove(timerKey);
            }
        }

        /// <summary>
        /// Inquire whether the timer is still active. Returns true unless the
        /// timer does not exist, has previously been canceled or if it was a
        /// single-shot timer that was already triggered.
        /// </summary>
        protected internal bool IsTimerActive(object timerKey) => _keyToTimers.ContainsKey(timerKey);

        // Internal hooks to avoid reliance on user calling super in postStop
        protected internal override void AfterPostStop()
        {
            base.AfterPostStop();
            if (_keyToTimers.Count != 0)
            {
                foreach (var entry in _keyToTimers)
                    entry.Value.Task.Cancel();
                _keyToTimers.Clear();
            }
        }
    }

    internal static class TimerMessages
    {
        [Serializable]
        public sealed class Scheduled : IDeadLetterSuppression
        {
            public readonly object TimerKey;
            public readonly int TimerId;
            public readonly bool IsRepeating;

            public Scheduled(object timerKey, int timerId, bool isRepeating)
            {
                if (timerKey == null)
                    throw new ArgumentNullException(nameof(timerKey), "Timer key cannot be null");

                TimerKey = timerKey;
                TimerId = timerId;
                IsRepeating = isRepeating;
            }
        }

        public sealed class Timer
        {
            public readonly int Id;
            public readonly ICancelable Task;

            public Timer(int id, ICancelable task)
            {
                Id = id;
                Task = task;
            }
        }
    }

    /// <summary>
    /// Represents the processing logic behind a <see cref="GraphStage{TShape}"/>. Roughly speaking, a subclass of <see cref="GraphStageLogic"/> is a
    /// collection of the following parts:
    ///  <para>* A set of <see cref="InHandler"/> and <see cref="OutHandler"/> instances and their assignments to the <see cref="Inlet"/>s and <see cref="Outlet"/>s
    ///    of the enclosing <see cref="GraphStage{TShape}"/></para>
    ///  <para>* Possible mutable state, accessible from the <see cref="InHandler"/> and <see cref="OutHandler"/> callbacks, but not from anywhere
    ///    else (as such access would not be thread-safe)</para>
    ///  <para>* The lifecycle hooks <see cref="PreStart"/> and <see cref="PostStop"/></para>
    ///  <para>* Methods for performing stream processing actions, like pulling or pushing elements</para> 
    ///  The stage logic is always stopped once all its input and output ports have been closed, i.e. it is not possible to
    ///  keep the stage alive for further processing once it does not have any open ports.
    /// </summary>
    public abstract class GraphStageLogic
    {
        #region internal classes

        private sealed class Reading<T> : InHandler
        {
            private readonly Inlet<T> _inlet;
            private int _n;
            public readonly IInHandler Previous;
            private readonly Action<T> _andThen;
            private readonly Action _onComplete;
            private readonly GraphStageLogic _logic;

            public Reading(Inlet<T> inlet, int n, IInHandler previous, Action<T> andThen, Action onComplete, GraphStageLogic logic)
            {
                _inlet = inlet;
                _n = n;
                Previous = previous;
                _andThen = andThen;
                _onComplete = onComplete;
                _logic = logic;
            }

            public override void OnPush()
            {
                var element = _logic.Grab(_inlet);
                _n--;

                if(_n > 0)
                    _logic.Pull(_inlet);
                else
                    _logic.SetHandler(_inlet, Previous);

                _andThen(element);
            }

            public override void OnUpstreamFinish()
            {
                _logic.SetHandler(_inlet, Previous);
                _onComplete();
                Previous.OnUpstreamFinish();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                _logic.SetHandler(_inlet, Previous);
                Previous.OnUpstreamFailure(e);
            }
        }

        private abstract class Emitting : OutHandler
        {
            public readonly Outlet Out;
            public readonly IOutHandler Previous;

            protected readonly Action AndThen;
            protected readonly GraphStageLogic Logic;
            protected Emitting FollowUps;
            protected Emitting FollowUpsTail;

            protected Emitting(Outlet @out, IOutHandler previous, Action andThen, GraphStageLogic logic)
            {
                Out = @out;
                Previous = previous;
                AndThen = andThen;
                Logic = logic;
            }

            protected void FollowUp()
            {
                Logic.SetHandler(Out, Previous);
                AndThen();
                if (FollowUps != null)
                {
                    Emitting e;
                    if ((e = Logic.GetHandler(Out) as Emitting) != null)
                        e.AddFollowUps(this);
                    else
                    {
                        var next = Dequeue();
                        if (next is EmittingCompletion)
                            Logic.Complete(Out);
                        else
                            Logic.SetHandler(Out, next);
                    }
                }
            }

            public void AddFollowUp(Emitting e)
            {
                if (FollowUps == null)
                {
                    FollowUps = e;
                    FollowUpsTail = e;
                }
                else
                {
                    FollowUpsTail.FollowUps = e;
                    FollowUpsTail = e;
                }
            }

            private void AddFollowUps(Emitting e)
            {
                if (FollowUps == null)
                {
                    FollowUps = e.FollowUps;
                    FollowUpsTail = e.FollowUpsTail;
                }
                else
                {
                    FollowUpsTail.FollowUps = e.FollowUps;
                    FollowUpsTail = e.FollowUpsTail;
                }
            }

            /// <summary>
            /// Dequeue this from the head of the queue, meaning that this object will
            /// not be retained (setHandler will install the followUp). For this reason
            /// the followUpsTail knowledge needs to be passed on to the next runner.
            /// </summary>
            private OutHandler Dequeue()
            {
                var result = FollowUps;
                result.FollowUpsTail = FollowUpsTail;
                return result;
            }

            public override void OnDownstreamFinish() => Previous.OnDownstreamFinish();
        }

        private sealed class EmittingSingle<T> : Emitting
        {
            private readonly T _element;
            public EmittingSingle(Outlet<T> @out, T element, IOutHandler previous, Action andThen, GraphStageLogic logic) : base(@out, previous, andThen, logic)
            {
                _element = element;
            }

            public override void OnPull()
            {
                Logic.Push((Outlet<T>)Out, _element);
                FollowUp();
            }
        }

        private sealed class EmittingIterator<T> : Emitting
        {
            private readonly IEnumerator<T> _enumerator;

            public EmittingIterator(Outlet<T> @out, IEnumerator<T> enumerator, IOutHandler previous, Action andThen, GraphStageLogic logic) : base(@out, previous, andThen, logic)
            {
                _enumerator = enumerator;
            }

            public override void OnPull()
            {
                Logic.Push((Outlet<T>)Out, _enumerator.Current);
                if (!_enumerator.MoveNext())
                    FollowUp();
            }
        }

        private sealed class EmittingCompletion : Emitting
        {
            public EmittingCompletion(Outlet @out, IOutHandler previous, GraphStageLogic logic) : base(@out, previous, DoNothing, logic) { }

            public override void OnPull() => Logic.Complete(Out);
        }

        private sealed class PassAlongHandler<TOut, TIn> : InHandler where TIn : TOut
        {
            public readonly Inlet<TIn> From;
            public readonly Outlet<TOut> To;

            private readonly GraphStageLogic _logic;
            private readonly bool _doFinish;
            private readonly bool _doFail;

            public PassAlongHandler(Inlet<TIn> from, Outlet<TOut> to, GraphStageLogic logic, bool doFinish, bool doFail)
            {
                From = from;
                To = to;
                _logic = logic;
                _doFinish = doFinish;
                _doFail = doFail;
            }

            public void Apply() => _logic.TryPull(From);

            public override void OnPush() => _logic.Emit(To, _logic.Grab<TOut>(From), Apply);

            public override void OnUpstreamFinish()
            {
                if (_doFinish)
                    _logic.CompleteStage();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                if (_doFail)
                    _logic.FailStage(e);
            }
        }

        protected sealed class LambdaInHandler : InHandler
        {
            private readonly Action _onPush;
            private readonly Action _onUpstreamFinish;
            private readonly Action<Exception> _onUpstreamFailure;

            public LambdaInHandler(Action onPush, Action onUpstreamFinish = null, Action<Exception> onUpstreamFailure = null)
            {
                _onPush = onPush;
                _onUpstreamFinish = onUpstreamFinish;
                _onUpstreamFailure = onUpstreamFailure;
            }

            public override void OnPush() => _onPush();

            public override void OnUpstreamFinish()
            {
                if (_onUpstreamFinish != null)
                    _onUpstreamFinish();
                else
                    base.OnUpstreamFinish();
            }

            public override void OnUpstreamFailure(Exception e)
            {
                if (_onUpstreamFailure != null)
                    _onUpstreamFailure(e);
                else
                    base.OnUpstreamFailure(e);
            }
        }

        protected sealed class LambdaOutHandler : OutHandler
        {
            private readonly Action _onPull;
            private readonly Action _onDownstreamFinish;

            public LambdaOutHandler(Action onPull, Action onDownstreamFinish = null)
            {
                _onPull = onPull;
                _onDownstreamFinish = onDownstreamFinish;
            }

            public override void OnPull() => _onPull();

            public override void OnDownstreamFinish()
            {
                if (_onDownstreamFinish != null)
                    _onDownstreamFinish();
                else
                    base.OnDownstreamFinish();
            }
        }

        #endregion


        /// <summary>
        /// Input handler that terminates the stage upon receiving completion. The stage fails upon receiving a failure.
        /// </summary>
        public static readonly InHandler EagerTerminateInput = Stage.EagerTerminateInput.Instance;

        /// <summary>
        /// Input handler that does not terminate the stage upon receiving completion.
        /// The stage fails upon receiving a failure.
        /// </summary>
        public static readonly InHandler IgnoreTerminateInput = Stage.IgnoreTerminateInput.Instance;

        /// <summary>
        /// Input handler that terminates the state upon receiving completion if the
        /// given condition holds at that time. The stage fails upon receiving a failure.
        /// </summary>
        public static InHandler ConditionalTerminateInput(Func<bool> predicate) => new ConditionalTerminateInput(predicate);

        /// <summary>
        /// Input handler that does not terminate the stage upon receiving completion
        /// nor failure.
        /// </summary>
        public static readonly InHandler TotallyIgnorantInput = Stage.TotallyIgnorantInput.Instance;

        /// <summary>
        /// Output handler that terminates the stage upon cancellation.
        /// </summary>
        public static readonly OutHandler EagerTerminateOutput = Stage.EagerTerminateOutput.Instance;

        /// <summary>
        /// Output handler that does not terminate the stage upon cancellation.
        /// </summary>
        public static readonly OutHandler IgnoreTerminateOutput = Stage.IgnoreTerminateOutput.Instance;

        public static Action DoNothing = () => { };

        /// <summary>
        /// Output handler that terminates the state upon receiving completion if the
        /// given condition holds at that time. The stage fails upon receiving a failure.
        /// </summary>
        public static OutHandler ConditionalTerminateOutput(Func<bool> predicate) => new ConditionalTerminateOutput(predicate);

        public readonly int InCount;
        public readonly int OutCount;

        internal readonly object[] Handlers;
        internal readonly Connection[] PortToConn;
        internal int StageId = int.MinValue;
        private GraphInterpreter _interpreter;

        internal GraphInterpreter Interpreter
        {
            get
            {
                if (_interpreter == null)
                    throw new IllegalStateException("Not yet initialized: only Sethandler is allowed in GraphStageLogic constructor");
                return _interpreter;
            }
            set { _interpreter = value; }
        }

        protected GraphStageLogic(int inCount, int outCount)
        {
            InCount = inCount;
            OutCount = outCount;
            Handlers = new object[InCount + OutCount];
            PortToConn = new Connection[Handlers.Length];
        }

        protected GraphStageLogic(Shape shape) : this(shape.Inlets.Count(), shape.Outlets.Count())
        {
        }

        /// <summary>
        /// The <see cref="IMaterializer"/> that has set this GraphStage in motion.
        /// </summary>
        protected IMaterializer Materializer => Interpreter.Materializer;

        /// <summary>
        /// An <see cref="IMaterializer"/> that may run fusable parts of the graphs that it materializes 
        /// within the same actor as the current GraphStage(if fusing is available). This materializer 
        /// must not be shared outside of the GraphStage.
        /// </summary>
        protected IMaterializer SubFusingMaterializer => Interpreter.SubFusingMaterializer;

        /// <summary>
        /// If this method returns true when all ports had been closed then the stage is not stopped 
        /// until <see cref="CompleteStage"/> or <see cref="FailStage"/> are explicitly called
        /// </summary>
        public virtual bool KeepGoingAfterAllPortsClosed => false;

        private StageActorRef _stageActorRef;
        public StageActorRef StageActorRef
        {
            get
            {
                if (_stageActorRef == null)
                    throw StageActorRefNotInitializedException.Instance;
                return _stageActorRef;
            }
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Inlet{T}"/>.
        /// </summary>
        protected internal void SetHandler(Inlet inlet, IInHandler handler)
        {
            Handlers[inlet.Id] = handler;
            _interpreter?.SetHandler(GetConnection(inlet), handler);
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        protected internal void SetHandler(Inlet inlet, Action onPush, Action onUpstreamFinish = null, Action<Exception> onUpstreamFailure = null)
        {
            if (onPush == null)
                throw new ArgumentNullException(nameof(onPush), "GraphStageLogic onPush handler must be provided");

            SetHandler(inlet, new LambdaInHandler(onPush, onUpstreamFinish, onUpstreamFailure));
        }

        /// <summary>
        /// Retrieves the current callback for the events on the given <see cref="Inlet{T}"/>
        /// </summary>
        protected IInHandler GetHandler(Inlet inlet) => (IInHandler)Handlers[inlet.Id];

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        protected internal void SetHandler(Outlet outlet, IOutHandler handler)
        {
            Handlers[outlet.Id + InCount] = handler;
            _interpreter?.SetHandler(GetConnection(outlet), handler);
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        protected internal void SetHandler(Outlet outlet, Action onPull, Action onDownstreamFinish = null)
        {
            if (onPull == null)
                throw new ArgumentNullException(nameof(onPull), "GraphStageLogic onPull handler must be provided");
            SetHandler(outlet, new LambdaOutHandler(onPull, onDownstreamFinish));
        }

        /// <summary>
        /// Retrieves the current callback for the events on the given <see cref="Outlet{T}"/>
        /// </summary>
        protected IOutHandler GetHandler(Outlet outlet) => (IOutHandler)Handlers[outlet.Id + InCount];

        private Connection GetConnection(Inlet inlet) => PortToConn[inlet.Id];

        private Connection GetConnection(Outlet outlet) => PortToConn[outlet.Id + InCount];

        private IOutHandler GetNonEmittingHandler(Outlet outlet)
        {
            var h = GetHandler(outlet);
            Emitting e;
            return (e = h as Emitting) != null ? e.Previous : h;
        }

        /// <summary>
        /// Requests an element on the given port. Calling this method twice before an element arrived will fail.
        /// There can only be one outstanding request at any given time.The method <see cref="HasBeenPulled"/> can be used
        /// query whether pull is allowed to be called or not.This method will also fail if the port is already closed.
        /// </summary>
        protected internal void Pull(Inlet inlet)
        {
            var connection = GetConnection(inlet);
            var portState = connection.PortState;

            if ((portState & (InReady | InClosed | OutClosed)) == InReady)
            {
                connection.PortState = portState ^ PullStartFlip;
                Interpreter.ChasePull(connection);
            }
            else
            {
                // Detailed error information should not add overhead to the hot path
                if (IsClosed(inlet))
                    throw new ArgumentException("Cannot pull a closed port");
                if (HasBeenPulled(inlet))
                    throw new ArgumentException("Cannot pull port twice");
            }

            // There were no errors, the pull was simply ignored as the target stage already closed its port. We
            // still need to track proper state though.
            connection.PortState = portState ^ PullStartFlip;
        }

        /// <summary>
        /// Requests an element on the given port. Calling this method twice before an element arrived will fail.
        /// There can only be one outstanding request at any given time.The method <see cref="HasBeenPulled"/> can be used
        /// query whether pull is allowed to be called or not.This method will also fail if the port is already closed.
        /// </summary>
        protected internal void Pull<T>(Inlet<T> inlet) => Pull((Inlet)inlet);

        /// <summary>
        /// Requests an element on the given port unless the port is already closed.
        /// Calling this method twice before an element arrived will fail.
        /// There can only be one outstanding request at any given time.The method <see cref="HasBeenPulled"/> can be used
        /// query whether pull is allowed to be called or not.
        /// </summary>
        protected internal void TryPull(Inlet inlet)
        {
            if (!IsClosed(inlet))
                Pull(inlet);
        }

        /// <summary>
        /// Requests an element on the given port unless the port is already closed.
        /// Calling this method twice before an element arrived will fail.
        /// There can only be one outstanding request at any given time.The method <see cref="HasBeenPulled"/> can be used
        /// query whether pull is allowed to be called or not.
        /// </summary>
        protected internal void TryPull<T>(Inlet<T> inlet) => TryPull((Inlet)inlet);

        /// <summary>
        /// Requests to stop receiving events from a given input port. Cancelling clears any ungrabbed elements from the port.
        /// </summary>
        protected void Cancel(Inlet inlet) => Interpreter.Cancel(GetConnection(inlet));

        /// <summary>
        /// Once the callback <see cref="InHandler.OnPush"/> for an input port has been invoked, the element that has been pushed
        /// can be retrieved via this method. After <see cref="Grab{T}(Inlet)"/> has been called the port is considered to be empty, and further
        /// calls to <see cref="Grab{T}(Inlet)"/> will fail until the port is pulled again and a new element is pushed as a response.
        /// 
        /// The method <see cref="IsAvailable(Inlet)"/> can be used to query if the port has an element that can be grabbed or not.
        /// </summary>
        protected internal T Grab<T>(Inlet inlet)
        {
            var connection = GetConnection(inlet);
            var element = connection.Slot;

            if ((connection.PortState & (InReady | InFailed)) ==
                InReady && !ReferenceEquals(element, Empty.Instance))
            {
                // fast path
                connection.Slot = Empty.Instance;
            }
            else
            {
                // slow path
                if (!IsAvailable(inlet))
                    throw new ArgumentException("Cannot get element from already empty input port");
                var failed = (GraphInterpreter.Failed)element;
                element = failed.PreviousElement;
                connection.Slot = new GraphInterpreter.Failed(failed.Reason, Empty.Instance);
            }

            return (T)element;
        }

        /// <summary>
        /// Once the callback <see cref="InHandler.OnPush"/> for an input port has been invoked, the element that has been pushed
        /// can be retrieved via this method. After <see cref="Grab{T}(Inlet{T})"/> has been called the port is considered to be empty, and further
        /// calls to <see cref="Grab{T}(Inlet{T})"/> will fail until the port is pulled again and a new element is pushed as a response.
        /// 
        /// The method <see cref="IsAvailable(Inlet)"/> can be used to query if the port has an element that can be grabbed or not.
        /// </summary>
        protected internal T Grab<T>(Inlet<T> inlet) => Grab<T>((Inlet)inlet);

        /// <summary>
        /// Indicates whether there is already a pending pull for the given input port. If this method returns true 
        /// then <see cref="IsAvailable(Inlet)"/> must return false for that same port.
        /// </summary>
        protected bool HasBeenPulled(Inlet inlet) 
            => (GetConnection(inlet).PortState & (InReady | InClosed)) == 0;

        /// <summary>
        /// Indicates whether there is an element waiting at the given input port. <see cref="Grab{T}(Inlet{T})"/> can be used to retrieve the
        /// element. After calling <see cref="Grab{T}(Inlet{T})"/> this method will return false.
        /// 
        /// If this method returns true then <see cref="HasBeenPulled"/> will return false for that same port.
        /// </summary>
        protected internal bool IsAvailable(Inlet inlet)
        {
            var connection = GetConnection(inlet);
            var normalArrived = (connection.PortState & (InReady | InFailed)) == InReady;

            if (normalArrived)
            {
                // fast path
                return !ReferenceEquals(connection.Slot, Empty.Instance);
            }
            
            // slow path on failure
            if ((connection.PortState & (InReady | InFailed)) ==
                (InReady | InFailed))
            {
                var failed = connection.Slot as GraphInterpreter.Failed;
                // This can only be Empty actually (if a cancel was concurrent with a failure)
                return failed != null && !ReferenceEquals(failed.PreviousElement, Empty.Instance);
            }

            return false;
        }

        /// <summary>
        /// Indicates whether the port has been closed. A closed port cannot be pulled.
        /// </summary>
        protected bool IsClosed(Inlet inlet) => (GetConnection(inlet).PortState & InClosed) != 0;

        /// <summary>
        /// Emits an element through the given output port. Calling this method twice before a <see cref="Pull{T}(Inlet{T})"/> has been arrived
        /// will fail. There can be only one outstanding push request at any given time. The method <see cref="IsAvailable(Inlet)"/> can be
        /// used to check if the port is ready to be pushed or not.
        /// </summary>
        protected internal void Push<T>(Outlet outlet, T element)
        {
            var connection = GetConnection(outlet);
            var portState = connection.PortState;

            connection.PortState = portState ^ PushStartFlip;
            if ((portState & (OutReady | OutClosed | InClosed)) == OutReady && (element != null))
            {
                connection.Slot = element;
                Interpreter.ChasePush(connection);
            }
            else
            {
                // Restore state for the error case
                connection.PortState = portState;

                // Detailed error information should not add overhead to the hot path
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                if (IsClosed(outlet)) throw new ArgumentException($"Cannot pull closed port {outlet}");
                if (!IsAvailable(outlet)) throw new ArgumentException($"Cannot push port twice {outlet}");

                // No error, just InClosed caused the actual pull to be ignored, but the status flag still needs to be flipped
                connection.PortState = portState ^ PushStartFlip;
            }
        }

        /// <summary>
        /// Controls whether this stage shall shut down when all its ports are closed, which
        /// is the default. In order to have it keep going past that point this method needs
        /// to be called with a true argument before all ports are closed, and afterwards
        /// it will not be closed until this method is called with a false argument or the
        /// stage is terminated via <see cref="CompleteStage"/> or <see cref="FailStage"/>.
        /// </summary>
        protected void SetKeepGoing(bool enabled) => Interpreter.SetKeepGoing(this, enabled);

        /// <summary>
        /// Signals that there will be no more elements emitted on the given port.
        /// </summary>
        protected void Complete(Outlet outlet)
        {
            var h = GetHandler(outlet);
            Emitting e;
            if ((e = h as Emitting) != null)
                e.AddFollowUp(new EmittingCompletion(e.Out, e.Previous, this));
            else
                Interpreter.Complete(GetConnection(outlet));
        }

        /// <summary>
        /// Signals failure through the given port.
        /// </summary>
        protected void Fail(Outlet outlet, Exception reason) => Interpreter.Fail(GetConnection(outlet), reason);

        /// <summary>
        /// Automatically invokes <see cref="Cancel"/> or <see cref="Complete"/> on all the input or output ports that have been called,
        /// then stops the stage, then <see cref="PostStop"/> is called.
        /// </summary>
        public void CompleteStage()
        {
            for (var i = 0; i < PortToConn.Length; i++)
            {
                if (i < InCount)
                    Interpreter.Cancel(PortToConn[i]);
                else
                {
                    var handler = Handlers[i];
                    Emitting e;
                    if ((e = handler as Emitting) != null)
                        e.AddFollowUp(new EmittingCompletion(e.Out, e.Previous, this));
                    else
                        Interpreter.Complete(PortToConn[i]);
                }
            }

            SetKeepGoing(false);
        }

        /// <summary>
        /// Automatically invokes <see cref="Cancel"/> or <see cref="Fail"/> on all the input or output ports that have been called,
        /// then stops the stage, then <see cref="PostStop"/> is called.
        /// </summary>
        public void FailStage(Exception reason)
        {
            for (var i = 0; i < PortToConn.Length; i++)
            {
                if (i < InCount)
                    Interpreter.Cancel(PortToConn[i]);
                else
                    Interpreter.Fail(PortToConn[i], reason);
            }

            SetKeepGoing(false);
        }

        /// <summary>
        /// Return true if the given output port is ready to be pushed.
        /// </summary>
        protected internal bool IsAvailable(Outlet outlet) 
            => (GetConnection(outlet).PortState & (OutReady | OutClosed)) == OutReady;

        /// <summary>
        /// Indicates whether the port has been closed. A closed port cannot be pushed.
        /// </summary>
        protected bool IsClosed(Outlet outlet) 
            => (GetConnection(outlet).PortState & OutClosed) != 0;

        /// <summary>
        /// Read a number of elements from the given inlet and continue with the given function,
        /// suspending execution if necessary. This action replaces the <see cref="InHandler"/>
        /// for the given inlet if suspension is needed and reinstalls the current
        /// handler upon receiving the last <see cref="InHandler.OnPush"/> signal.
        /// 
        /// If upstream closes before N elements have been read,
        /// the <paramref name="onComplete"/> function is invoked with the elements which were read.
        /// </summary>
        protected void ReadMany<T>(Inlet<T> inlet, int n, Action<IEnumerable<T>> andThen, Action<IEnumerable<T>> onComplete)
        {
            if (n < 0)
                throw new ArgumentException("Cannot read negative number of elements");
            if (n == 0)
                andThen(null);
            else
            {
                var result = new T[n];
                var pos = 0;

                if (IsAvailable(inlet))
                {
                    //If we already have data available, then shortcircuit and read the first
                    result[pos] = Grab(inlet);
                    pos++;
                }

                if (n != pos)
                {
                    // If we aren't already done
                    RequireNotReading(inlet);
                    if (!HasBeenPulled(inlet))
                        Pull(inlet);
                    SetHandler(inlet, new Reading<T>(inlet, n - pos, GetHandler(inlet), element =>
                    {
                        result[pos] = element;
                        pos++;
                        if (pos == n)
                            andThen(result);
                    }, () => onComplete(result.Take(pos)), this));
                }
                else
                    andThen(result);
            }
        }


        /// <summary>
        /// Read an element from the given inlet and continue with the given function,
        /// suspending execution if necessary. This action replaces the <see cref="InHandler"/>
        /// for the given inlet if suspension is needed and reinstalls the current
        /// handler upon receiving the <see cref="InHandler.OnPush"/> signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        protected void Read<T>(Inlet<T> inlet, Action<T> andThen, Action onClose)
        {
            if (IsAvailable(inlet))
                andThen(Grab(inlet));
            else if (IsClosed(inlet))
                onClose();
            else
            {
                RequireNotReading(inlet);
                if (!HasBeenPulled(inlet))
                    Pull(inlet);
                SetHandler(inlet, new Reading<T>(inlet, 1, GetHandler(inlet), andThen, onClose, this));
            }
        }

        /// <summary>
        /// Abort outstanding (suspended) reading for the given inlet, if there is any.
        /// This will reinstall the replaced handler that was in effect before the read
        /// call.
        /// </summary>
        protected void AbortReading<T>(Inlet<T> inlet)
        {
            Reading<T> reading;
            if ((reading = GetHandler(inlet) as Reading<T>) != null)
                SetHandler(inlet, reading.Previous);
        }

        private void RequireNotReading<T>(Inlet<T> inlet)
        {
            if (GetHandler(inlet) is Reading<T>)
                throw new IllegalStateException("Already reading on inlet " + inlet);
        }

        /// <summary>
        /// Emit a sequence of elements through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerable<T> elements, Action andThen)
            => EmitMultiple(outlet, elements.GetEnumerator(), andThen);

        /// <summary>
        /// Emit a sequence of elements through the given outlet, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal.
        /// </summary>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerable<T> elements)
            => EmitMultiple(outlet, elements, DoNothing);

        /// <summary>
        /// Emit a sequence of elements through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerator<T> enumerator, Action andThen)
        {
            if (enumerator.MoveNext())
            {
                if (IsAvailable(outlet))
                {
                    Push(outlet, enumerator.Current);

                    if (enumerator.MoveNext())
                        SetOrAddEmitting(outlet,
                            new EmittingIterator<T>(outlet, enumerator, GetNonEmittingHandler(outlet), andThen, this));
                    else
                        andThen();
                }
                else
                    SetOrAddEmitting(outlet,
                        new EmittingIterator<T>(outlet, enumerator, GetNonEmittingHandler(outlet), andThen, this));
            }
            else
                andThen();
        }

        /// <summary>
        /// Emit a sequence of elements through the given outlet, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal.
        /// </summary>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerator<T> enumerator)
        {
            EmitMultiple(outlet, enumerator, DoNothing);
        }

        /// <summary>
        /// Emit an element through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        protected internal void Emit<T>(Outlet<T> outlet, T element, Action andThen)
        {
            if (IsAvailable(outlet))
            {
                Push(outlet, element);
                andThen();
            }
            else
                SetOrAddEmitting(outlet, new EmittingSingle<T>(outlet, element, GetNonEmittingHandler(outlet), andThen, this));
        }

        /// <summary>
        /// Emit an element through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>.
        /// </summary>
        protected internal void Emit<T>(Outlet<T> outlet, T element) => Emit(outlet, element, DoNothing);

        /// <summary>
        /// Abort outstanding (suspended) emissions for the given outlet, if there are any.
        /// This will reinstall the replaced handler that was in effect before the <see cref="Emit{T}(Outlet{T},T,Action)"/>
        /// call.
        /// </summary>
        protected internal void AbortEmitting<T>(Outlet<T> outlet)
        {
            Emitting e;
            if ((e = GetHandler(outlet) as Emitting) != null)
                SetHandler(outlet, e.Previous);
        }

        private void SetOrAddEmitting<T>(Outlet<T> outlet, Emitting next)
        {
            Emitting e;
            if ((e = GetHandler(outlet) as Emitting) != null)
                e.AddFollowUp(next);
            else
                SetHandler(outlet, next);
        }

        /// <summary>
        /// Install a handler on the given inlet that emits received elements on the
        /// given outlet before pulling for more data. <paramref name="doFinish"/> and <paramref name="doFail"/> control whether
        /// completion or failure of the given inlet shall lead to stage termination or not.
        /// <paramref name="doPull"/> instructs to perform one initial pull on the <paramref name="from"/> port.
        /// </summary>
        protected void PassAlong<TOut, TIn>(Inlet<TIn> from, Outlet<TOut> to, bool doFinish = true, bool doFail = true, bool doPull = false)
            where TIn : TOut
        {
            var passHandler = new PassAlongHandler<TOut, TIn>(from, to, this, doFinish, doFail);
            if (_interpreter != null)
            {
                if (IsAvailable(from))
                    Emit(to, Grab(from), passHandler.Apply);
                if (doFinish && IsClosed(from))
                    CompleteStage();
            }

            SetHandler(from, passHandler);
            if (doPull)
                TryPull(from);
        }

        /// <summary>
        /// Obtain a callback object that can be used asynchronously to re-enter the
        /// current <see cref="GraphStage{TShape}"/> with an asynchronous notification. The delegate returned 
        /// is safe to be called from other threads and it will in the background thread-safely
        /// delegate to the passed callback function. I.e. it will be called by the external world and
        /// the passed handler will be invoked eventually in a thread-safe way by the execution environment.
        /// 
        /// This object can be cached and reused within the same <see cref="GraphStageLogic"/>.
        /// </summary>
        protected Action<T> GetAsyncCallback<T>(Action<T> handler)
            => @event => Interpreter.OnAsyncInput(this, @event, x => handler((T) x));

        /// <summary>
        /// Obtain a callback object that can be used asynchronously to re-enter the
        /// current <see cref="GraphStage{TShape}"/> with an asynchronous notification. The delegate returned 
        /// is safe to be called from other threads and it will in the background thread-safely
        /// delegate to the passed callback function. I.e. it will be called by the external world and
        /// the passed handler will be invoked eventually in a thread-safe way by the execution environment.
        /// 
        /// This object can be cached and reused within the same <see cref="GraphStageLogic"/>.
        /// </summary>
        protected Action GetAsyncCallback(Action handler)
            => () => Interpreter.OnAsyncInput(this, NotUsed.Instance, _ => handler());

        /// <summary>
        /// Initialize a <see cref="StageActorRef"/> which can be used to interact with from the outside world "as-if" an actor.
        /// The messages are looped through the <see cref="GetAsyncCallback{T}"/> mechanism of <see cref="GraphStage{TShape}"/> so they are safe to modify
        /// internal state of this stage.
        /// 
        /// This method must (the earliest) be called after the <see cref="GraphStageLogic"/> constructor has finished running,
        /// for example from the <see cref="PreStart"/> callback the graph stage logic provides.
        /// 
        /// Created <see cref="StageActorRef"/> to get messages and watch other actors in synchronous way.
        /// 
        /// The <see cref="StageActorRef"/>'s lifecycle is bound to the Stage, in other words when the Stage is finished,
        /// the Actor will be terminated as well. The entity backing the <see cref="StageActorRef"/> is not a real Actor,
        /// but the <see cref="GraphStageLogic"/> itself, therefore it does not react to <see cref="PoisonPill"/>.
        /// </summary>
        /// <param name="receive">Callback that will be called upon receiving of a message by this special Actor</param>
        /// <returns>Minimal actor with watch method</returns>
        protected StageActorRef GetStageActorRef(StageActorRef.Receive receive)
        {
            if (_stageActorRef == null)
            {
                var actorMaterializer = ActorMaterializerHelper.Downcast(Interpreter.Materializer);
                var provider = ((IInternalActorRef)actorMaterializer.Supervisor).Provider;
                var path = actorMaterializer.Supervisor.Path / StageActorRef.Name.Next();
                _stageActorRef = new StageActorRef(provider, actorMaterializer.Logger, r => GetAsyncCallback<Tuple<IActorRef, object>>(tuple => r(tuple)), receive, path);
            }
            else
                _stageActorRef.Become(receive);

            return _stageActorRef;
        }

        protected internal virtual void BeforePreStart() { }

        protected internal virtual void AfterPostStop()
        {
            if (_stageActorRef != null)
            {
                _stageActorRef.Stop();
                _stageActorRef = null;
            }
        }

        /// <summary>
        /// Invoked before any external events are processed, at the startup of the stage.
        /// </summary>
        public virtual void PreStart() { }

        /// <summary>
        /// Invoked after processing of external events stopped because the stage is about to stop or fail.
        /// </summary>
        public virtual void PostStop() { }

        /// <summary>
        /// INTERNAL API
        /// 
        /// This allows the dynamic creation of an Inlet for a GraphStage which is
        /// connected to a Sink that is available for materialization (e.g. using
        /// the <see cref="GraphStageLogic.SubFusingMaterializer"/>). Care needs to be taken to cancel this Inlet
        /// when the stage shuts down lest the corresponding Sink be left hanging.
        /// </summary>
        protected class SubSinkInlet<T>
        {
            private readonly string _name;
            private InHandler _handler;
            private Option<T> _elem;
            private bool _closed;
            private bool _pulled;
            private readonly SubSink<T> _sink;

            public SubSinkInlet(GraphStageLogic logic, string name)
            {
                _name = name;
                _sink = new SubSink<T>(name, logic.GetAsyncCallback<IActorSubscriberMessage>(
                    msg =>
                    {
                        if (_closed)
                            return;

                        if (msg is OnNext)
                        {
                            _elem = (T) ((OnNext) msg).Element;
                            _pulled = false;
                            _handler.OnPush();
                        }
                        else if (msg is OnComplete)
                        {
                            _closed = true;
                            _handler.OnUpstreamFinish();
                        }
                        else if (msg is OnError)
                        {
                            _closed = true;
                            _handler.OnUpstreamFailure(((OnError) msg).Cause);
                        }
                    }));
            }

            public IGraph<SinkShape<T>, NotUsed> Sink => _sink;

            public void SetHandler(InHandler handler) => _handler = handler;

            public bool IsAvailable => _elem.HasValue;

            public bool IsClosed => _closed;

            public bool HasBeenPulled => _pulled && !IsClosed;

            public T Grab()
            {
                if (!_elem.HasValue)
                    throw new IllegalStateException($"cannot grab element from port {this} when data have not yet arrived");

                var ret = _elem.Value;
                _elem = Option<T>.None;
                return ret;
            }

            public void Pull()
            {
                if (_pulled)
                    throw new IllegalStateException($"cannot pull port {this} twice");
                if (_closed)
                    throw new IllegalStateException($"cannot pull closed port {this}");

                _pulled = true;
                _sink.PullSubstream();
            }

            public void Cancel()
            {
                _closed = true;
                _sink.CancelSubstream();
            }

            public override string ToString() => $"SubSinkInlet{_name}";
        }

        protected SubSinkInlet<T> CreateSubSinkInlet<T>(string name) => new SubSinkInlet<T>(this, name);

        /// <summary>
        /// INTERNAL API
        /// 
        /// This allows the dynamic creation of an Outlet for a GraphStage which is
        /// connected to a Source that is available for materialization (e.g. using
        /// the <see cref="GraphStageLogic.SubFusingMaterializer"/>). Care needs to be taken to complete this
        /// Outlet when the stage shuts down lest the corresponding Sink be left
        /// hanging. It is good practice to use the <see cref="Timeout"/> method to cancel this
        /// Outlet in case the corresponding Source is not materialized within a
        /// given time limit, see e.g. ActorMaterializerSettings.
        /// </summary>
        protected class SubSourceOutlet<T>
        {
            private readonly string _name;
            private readonly SubSource<T> _source;
            private IOutHandler _handler;
            private bool _available;
            private bool _closed;

            public SubSourceOutlet(GraphStageLogic logic, string name)
            {
                _name = name;

                _source = new SubSource<T>(name, logic.GetAsyncCallback<SubSink.ICommand>(command =>
                {
                    if (command is SubSink.RequestOne)
                    {
                        if (!_closed)
                        {
                            _available = true;
                            _handler.OnPull();
                        }
                    }
                    else if (command is SubSink.Cancel)
                    {
                        if (!_closed)
                        {
                            _available = false;
                            _closed = true;
                            _handler.OnDownstreamFinish();
                        }
                    }
                }));
            }

            /// <summary>
            /// Get the Source for this dynamic output port.
            /// </summary>
            public IGraph<SourceShape<T>, NotUsed> Source => _source;

            /// <summary>
            /// Returns true if this output port can be pushed.
            /// </summary>
            public bool IsAvailable => _available;

            /// <summary>
            /// Returns true if this output port is closed, but caution
            /// THIS WORKS DIFFERENTLY THAN THE NORMAL isClosed(out).
            /// Due to possibly asynchronous shutdown it may not return
            /// true immediately after <see cref="Complete"/> or <see cref="Fail"/> have returned.
            /// </summary>
            public bool IsClosed => _closed;

            /// <summary>
            /// Set the source into timed-out mode if it has not yet been materialized.
            /// </summary>
            public void Timeout(TimeSpan d)
            {
                if (_source.Timeout(d))
                    _closed = true;
            }

            /// <summary>
            /// Set OutHandler for this dynamic output port; this needs to be done before
            /// the first substream callback can arrive.
            /// </summary>
            public void SetHandler(IOutHandler handler) => _handler = handler;

            /// <summary>
            /// Push to this output port.
            /// </summary>
            public void Push(T elem)
            {
                _available = false;
                _source.PushSubstream(elem);
            }

            /// <summary>
            /// Complete this output port. 
            /// </summary>
            public void Complete()
            {
                _available = false;
                _closed = true;
                _source.CompleteSubstream();
            }

            /// <summary>
            /// Fail this output port.
            /// </summary>
            public void Fail(Exception ex)
            {
                _available = false;
                _closed = true;
                _source.FailSubstream(ex);
            }

            public override string ToString() => $"SubSourceOutlet({_name})";
        }
    }

    /// <summary>
    /// Collection of callbacks for an input port of a <see cref="GraphStage{TShape}"/>
    /// </summary>
    public interface IInHandler
    {
        /// <summary>
        /// Called when the input port has a new element available. The actual element can be retrieved via the <see cref="GraphStageLogic.Grab{T}(Inlet{T})"/> method.
        /// </summary>
        void OnPush();

        /// <summary>
        /// Called when the input port is finished. After this callback no other callbacks will be called for this port.
        /// </summary>
        void OnUpstreamFinish();

        /// <summary>
        /// Called when the input port has failed. After this callback no other callbacks will be called for this port.
        /// </summary>
        void OnUpstreamFailure(Exception e);
    }

    /// <summary>
    /// Collection of callbacks for an input port of a <see cref="GraphStage{TShape}"/>
    /// </summary>
    public abstract class InHandler : IInHandler
    {
        /// <summary>
        /// Called when the input port has a new element available. The actual element can be retrieved via the <see cref="GraphStageLogic.Grab{T}(Inlet{T})"/> method.
        /// </summary>
        public abstract void OnPush();

        /// <summary>
        /// Called when the input port is finished. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFinish() => Current.ActiveStage.CompleteStage();

        /// <summary>
        /// Called when the input port has failed. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFailure(Exception e) => Current.ActiveStage.FailStage(e);
    }

    /// <summary>
    /// Collection of callbacks for an output port of a <see cref="GraphStage{TShape}"/>
    /// </summary>
    public interface IOutHandler
    {
        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        void OnDownstreamFinish();
    }

    /// <summary>
    /// Collection of callbacks for an output port of a <see cref="GraphStage{TShape}"/>
    /// </summary>
    public abstract class OutHandler : IOutHandler
    {
        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        public abstract void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnDownstreamFinish() => Current.ActiveStage.CompleteStage();
    }

    /// <summary>
    /// Collection of callbacks for an output port of a <see cref="GraphStage{TShape}"/> and
    /// for an input port of a <see cref="GraphStage{TShape}"/>
    /// </summary>
    public abstract class InAndOutHandler : IInHandler, IOutHandler
    {

        /// <summary>
        /// Called when the input port has a new element available. The actual element can be retrieved via the <see cref="GraphStageLogic.Grab{T}(Inlet{T})"/> method.
        /// </summary>
        public abstract void OnPush();

        /// <summary>
        /// Called when the input port is finished. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFinish() => Current.ActiveStage.CompleteStage();

        /// <summary>
        /// Called when the input port has failed. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFailure(Exception e) => Current.ActiveStage.FailStage(e);

        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        public abstract void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnDownstreamFinish() => Current.ActiveStage.CompleteStage();
    }

    /// <summary>
    /// A <see cref="GraphStageLogic"/> that implements <see cref="IInHandler"/>.
    /// <para/>
    /// <see cref="OnUpstreamFinish"/> calls <see cref="GraphStageLogic.CompleteStage"/>
    /// <para/>
    /// <see cref="OnUpstreamFailure"/> calls <see cref="GraphStageLogic.FailStage"/>
    /// </summary>
    public abstract class InGraphStageLogic : GraphStageLogic, IInHandler
    {
        protected InGraphStageLogic(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        protected InGraphStageLogic(Shape shape) : base(shape)
        {
        }

        /// <summary>
        /// Called when the input port has a new element available. The actual element can be retrieved via the <see cref="GraphStageLogic.Grab{T}(Inlet{T})"/> method.
        /// </summary>
        public abstract void OnPush();

        /// <summary>
        /// Called when the input port is finished. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFinish() => CompleteStage();
        
        /// <summary>
        /// Called when the input port has failed. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFailure(Exception e) => FailStage(e);
    }

    /// <summary>
    /// A <see cref="GraphStageLogic"/> that implements <see cref="IOutHandler"/>.
    /// <para/>
    /// <see cref="OnDownstreamFinish"/> calls <see cref="GraphStageLogic.CompleteStage"/>
    /// </summary>
    public abstract class OutGraphStageLogic : GraphStageLogic, IOutHandler
    {
        protected OutGraphStageLogic(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        protected OutGraphStageLogic(Shape shape) : base(shape)
        {
        }

        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        public abstract void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnDownstreamFinish() => CompleteStage();
    }

    /// <summary>
    /// A <see cref="GraphStageLogic"/> that implements <see cref="IInHandler"/> and <see cref="IOutHandler"/>.
    /// <para/>
    /// <see cref="OnUpstreamFinish"/> calls <see cref="GraphStageLogic.CompleteStage"/>
    /// <para/>
    /// <see cref="OnUpstreamFailure"/> calls <see cref="GraphStageLogic.FailStage"/>
    /// <para/>
    /// <see cref="OnDownstreamFinish"/> calls <see cref="GraphStageLogic.CompleteStage"/>
    /// </summary>
    public abstract class InAndOutGraphStageLogic : GraphStageLogic, IInHandler, IOutHandler
    {
        protected InAndOutGraphStageLogic(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        protected InAndOutGraphStageLogic(Shape shape) : base(shape)
        {
        }

        /// <summary>
        /// Called when the input port has a new element available. The actual element can be retrieved via the <see cref="GraphStageLogic.Grab{T}(Inlet{T})"/> method.
        /// </summary>
        public abstract void OnPush();
        
        /// <summary>
        /// Called when the input port is finished. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFinish() => CompleteStage();

        /// <summary>
        /// Called when the input port has failed. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnUpstreamFailure(Exception e) => FailStage(e);

        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        public abstract void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnDownstreamFinish() => CompleteStage();
    }

    [Serializable]
    public class StageActorRefNotInitializedException : Exception
    {
        public static readonly StageActorRefNotInitializedException Instance = new StageActorRefNotInitializedException();
        private StageActorRefNotInitializedException() : base("You must first call getStageActorRef, to initialize the Actors behaviour") { }
        protected StageActorRefNotInitializedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Input handler that terminates the stage upon receiving completion. The stage fails upon receiving a failure.
    /// </summary>
    public sealed class EagerTerminateInput : InHandler
    {
        public static readonly EagerTerminateInput Instance = new EagerTerminateInput();
        private EagerTerminateInput() { }
        public override void OnPush() { }
    }

    /// <summary>
    /// Input handler that does not terminate the stage upon receiving completion. The stage fails upon receiving a failure.
    /// </summary>
    public sealed class IgnoreTerminateInput : InHandler
    {
        public static readonly IgnoreTerminateInput Instance = new IgnoreTerminateInput();
        private IgnoreTerminateInput() { }
        public override void OnPush() { }
        public override void OnUpstreamFinish() { }
    }

    /// <summary>
    /// Input handler that terminates the state upon receiving completion 
    /// if the given condition holds at that time.The stage fails upon receiving a failure.
    /// </summary>
    public class ConditionalTerminateInput : InHandler
    {
        private readonly Func<bool> _predicate;

        public ConditionalTerminateInput(Func<bool> predicate)
        {
            _predicate = predicate;
        }

        public override void OnPush() { }
        public override void OnUpstreamFinish()
        {
            if (_predicate())
                Current.ActiveStage.CompleteStage();
        }
    }

    /// <summary>
    /// Input handler that does not terminate the stage upon receiving completion nor failure.
    /// </summary>
    public sealed class TotallyIgnorantInput : InHandler
    {
        public static readonly TotallyIgnorantInput Instance = new TotallyIgnorantInput();
        private TotallyIgnorantInput() { }
        public override void OnPush() { }
        public override void OnUpstreamFinish() { }
        public override void OnUpstreamFailure(Exception e) { }
    }

    /// <summary>
    /// Output handler that terminates the stage upon cancellation.
    /// </summary>
    public sealed class EagerTerminateOutput : OutHandler
    {
        public static readonly EagerTerminateOutput Instance = new EagerTerminateOutput();
        private EagerTerminateOutput() { }
        public override void OnPull() { }
    }

    /// <summary>
    /// Output handler that does not terminate the stage upon cancellation.
    /// </summary>
    public sealed class IgnoreTerminateOutput : OutHandler
    {
        public static readonly IgnoreTerminateOutput Instance = new IgnoreTerminateOutput();
        private IgnoreTerminateOutput() { }
        public override void OnPull() { }
        public override void OnDownstreamFinish() { }
    }

    /// <summary>
    /// Output handler that terminates the state upon receiving completion if the
    /// given condition holds at that time.The stage fails upon receiving a failure.
    /// </summary>
    public class ConditionalTerminateOutput : OutHandler
    {
        private readonly Func<bool> _predicate;

        public ConditionalTerminateOutput(Func<bool> predicate)
        {
            _predicate = predicate;
        }

        public override void OnPull() { }
        public override void OnDownstreamFinish()
        {
            if (_predicate())
                Current.ActiveStage.CompleteStage();
        }
    }

    /// <summary>
    /// Minimal actor to work with other actors and watch them in a synchronous ways.
    /// </summary>
    public sealed class StageActorRef : MinimalActorRef
    {
        public delegate void Receive(Tuple<IActorRef, object> args);
        public readonly IImmutableSet<IActorRef> StageTerminatedTombstone = null;

        /// <summary>
        /// Globally sequential, one should not depend on these names in any case.
        /// </summary>
        public static readonly EnumerableActorName Name = new EnumerableActorNameImpl("StageActorRef", new AtomicCounterLong(0L));

        public readonly ILoggingAdapter Log;
        private readonly Action<Tuple<IActorRef, object>> _callback;
        private readonly AtomicReference<IImmutableSet<IActorRef>> _watchedBy = new AtomicReference<IImmutableSet<IActorRef>>(ImmutableHashSet<IActorRef>.Empty);

        private volatile Receive _behavior;
        private IImmutableSet<IActorRef> _watching = ImmutableHashSet<IActorRef>.Empty;

        public StageActorRef(IActorRefProvider provider, ILoggingAdapter log, Func<Receive, Action<Tuple<IActorRef, object>>> getAsyncCallback, Receive initialReceive, ActorPath path)
        {
            Log = log;
            Provider = provider;
            _behavior = initialReceive;
            Path = path;

            _callback = getAsyncCallback(args => _behavior(args));
        }

        public override ActorPath Path { get; }

        public override IActorRefProvider Provider { get; }

        public override bool IsTerminated => _watchedBy.Value == StageTerminatedTombstone;

        private void LogIgnored(object message) => Log.Warning($"{message} message sent to StageActorRef({Path}) will be ignored, since it is not a real Actor. Use a custom message type to communicate with it instead.");
        protected override void TellInternal(object message, IActorRef sender)
        {
            var handled = true;
            if(message is PoisonPill) { LogIgnored(message);}
            else if(message is Kill) { LogIgnored(message);}
            else { handled = false;}

            if (handled)
                return;

            var t = message as Terminated;
            if (t != null)
            {
                if (_watching.Contains(t.ActorRef))
                    _watching.Remove(t.ActorRef);
                else
                    return;
            }

            _callback(Tuple.Create(sender, message));
        }

        public override void SendSystemMessage(ISystemMessage message)
        {
            var death = message as DeathWatchNotification;
            if (death != null)
                Tell(new Terminated(death.Actor, true, false), ActorRefs.NoSender);
            else if (message is Watch)
            {
                var w = (Watch) message;
                AddWatcher(w.Watchee, w.Watcher);
            }
            else if (message is Unwatch)
            {
                var u = (Unwatch) message;
                RemoveWatcher(u.Watchee, u.Watcher);
            }
        }

        public void Become(Receive behavior) => _behavior = behavior;

        private void SendTerminated()
        {
            var watchedBy = _watchedBy.GetAndSet(StageTerminatedTombstone);
            if (watchedBy != StageTerminatedTombstone)
            {
                foreach (var actorRef in watchedBy.Cast<IInternalActorRef>())
                    SendTerminated(actorRef);

                foreach (var actorRef in _watching.Cast<IInternalActorRef>())
                    UnwatchWatched(actorRef);

                _watching = ImmutableHashSet<IActorRef>.Empty;
            }
        }

        public void Watch(IActorRef actorRef)
        {
            var iw = (IInternalActorRef) actorRef;
            _watching = _watching.Add(actorRef);
            iw.SendSystemMessage(new Watch(iw, this));
        }

        public void Unwatch(IActorRef actorRef)
        {
            var iw = (IInternalActorRef)actorRef;
            _watching = _watching.Remove(actorRef);
            iw.SendSystemMessage(new Unwatch(iw, this));
        }

        public override void Stop() => SendTerminated();

        private void SendTerminated(IInternalActorRef actorRef)
            => actorRef.SendSystemMessage(new DeathWatchNotification(this, true, false));

        private void UnwatchWatched(IInternalActorRef actorRef) => actorRef.SendSystemMessage(new Unwatch(actorRef, this));

        private void AddWatcher(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            while (true)
            {
                var watchedBy = _watchedBy.Value;
                if (watchedBy == StageTerminatedTombstone)
                    SendTerminated(watcher);
                else
                {
                    var isWatcheeSelf = Equals(watchee, this);
                    var isWatcherSelf = Equals(watcher, this);

                    if (isWatcheeSelf && !isWatcherSelf)
                    {
                        if (!watchedBy.Contains(watcher))
                            if (!_watchedBy.CompareAndSet(watchedBy, watchedBy.Add(watcher)))
                                continue; // try again
                    }
                    else if (!isWatcheeSelf && isWatcherSelf)
                        Log.Warning("externally triggered watch from {0} to {1} is illegal on StageActorRef",
                            watcher, watchee);
                    else
                        Log.Error("BUG: illegal Watch({0}, {1}) for {2}", watchee, watcher, this);
                }

                break;
            }
        }

        private void RemoveWatcher(IInternalActorRef watchee, IInternalActorRef watcher)
        {
            while (true)
            {
                var watchedBy = _watchedBy.Value;
                if (watchedBy == null)
                    SendTerminated(watcher);
                else
                {
                    var isWatcheeSelf = Equals(watchee, this);
                    var isWatcherSelf = Equals(watcher, this);

                    if (isWatcheeSelf && !isWatcherSelf)
                    {
                        if (!watchedBy.Contains(watcher))
                            if (!_watchedBy.CompareAndSet(watchedBy, watchedBy.Remove(watcher)))
                                continue; // try again
                    }
                    else if (!isWatcheeSelf && isWatcherSelf)
                        Log.Warning("externally triggered unwatch from {0} to {1} is illegal on StageActorRef",
                            watcher, watchee);
                    else
                        Log.Error("BUG: illegal Unatch({0}, {1}) for {2}", watchee, watcher, this);
                }

                break;
            }
        }
    }

    /// <summary>
    /// <para>
    /// This class wraps callback for <see cref="GraphStage{TShape}"/> instances and gracefully handles
    /// cases when stage is not yet initialized or already finished.
    /// </para>
    /// <para>
    /// While <see cref="GraphStage{TShape}"/> is not initialized it adds all requests to list.
    /// As soon as <see cref="GraphStage{TShape}"/> is started it stops collecting requests (pointing
    /// to real callback function) and runs all callbacks from the list.
    /// </para>
    /// <para>
    /// Intended to be used by <see cref="GraphStage{TShape}"/> that share callback with outer world.
    /// </para>
    /// </summary>
    /// <typeparam name="T"></typeparam>
    internal class GraphStageLogicWithCallbackWrapper<T> : GraphStageLogic
    {
        private interface ICallbackState { }

        private sealed class NotInitialized : ICallbackState
        {
            public IList<T> Args { get; }

            public NotInitialized(IList<T> args)
            {
                Args = args;
            }
        }

        private sealed class Initialized : ICallbackState
        {
            public Action<T> Callback { get; }

            public Initialized(Action<T> callback)
            {
                Callback = callback;
            }
        }

        private sealed class Stopped : ICallbackState
        {
            public Action<T> Callback { get; }

            public Stopped(Action<T> callback)
            {
                Callback = callback;
            }
        }

        private readonly AtomicReference<ICallbackState> _callbackState =
            new AtomicReference<ICallbackState>(new NotInitialized(new List<T>()));

        public GraphStageLogicWithCallbackWrapper(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        public GraphStageLogicWithCallbackWrapper(Shape shape) : base(shape)
        {
        }

        protected void StopCallback(Action<T> callback) => Locked(() => _callbackState.Value = new Stopped(callback));

        protected void InitCallback(Action<T> callback) => Locked(() =>
        {
            var state = _callbackState.GetAndSet(new Initialized(callback));
            (state as NotInitialized)?.Args.ForEach(callback);
        });

        protected void InvokeCallbacks(T arg) => Locked(() =>
        {
            var state = _callbackState.Value;
            if (state is Initialized)
                ((Initialized) state).Callback(arg);
            else if (state is NotInitialized)
                ((NotInitialized) state).Args.Add(arg);
            else if (state is Stopped)
                ((Stopped) state).Callback(arg);
        });

        private void Locked(Action body)
        {
            Monitor.Enter(this);
            try
            {
                body();
            }
            finally
            {
                Monitor.Exit(this);
            }
        }
    }
}