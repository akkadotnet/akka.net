//-----------------------------------------------------------------------
// <copyright file="GraphStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
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
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TMaterialized">TBD</typeparam>
    public interface ILogicAndMaterializedValue<out TMaterialized>
    {
        /// <summary>
        /// TBD
        /// </summary>
        GraphStageLogic Logic { get; }

        /// <summary>
        /// TBD
        /// </summary>
        TMaterialized MaterializedValue { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TMaterialized">TBD</typeparam>
    public readonly struct LogicAndMaterializedValue<TMaterialized> : ILogicAndMaterializedValue<TMaterialized>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="logic">TBD</param>
        /// <param name="materializedValue">TBD</param>
        public LogicAndMaterializedValue(GraphStageLogic logic, TMaterialized materializedValue)
        {
            Logic = logic;
            MaterializedValue = materializedValue;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public GraphStageLogic Logic { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TMaterialized MaterializedValue { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TShape">TBD</typeparam>
    /// <typeparam name="TMaterialized">TBD</typeparam>
    public interface IGraphStageWithMaterializedValue<out TShape, out TMaterialized> : IGraph<TShape, TMaterialized>
        where TShape : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        ILogicAndMaterializedValue<TMaterialized> CreateLogicAndMaterializedValue(Attributes attributes);
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TShape">TBD</typeparam>
    /// <typeparam name="TMaterialized">TBD</typeparam>
    public abstract class
        GraphStageWithMaterializedValue<TShape, TMaterialized> : IGraphStageWithMaterializedValue<TShape, TMaterialized>
        where TShape : Shape
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

            public IGraph<TShape, TMaterialized> WithAttributes(Attributes attributes) =>
                new Graph(Shape, Module, attributes);

            public IGraph<TShape, TMaterialized> AddAttributes(Attributes attributes) =>
                WithAttributes(Module.Attributes.And(attributes));

            public IGraph<TShape, TMaterialized> Named(string name) => AddAttributes(Attributes.CreateName(name));

            public IGraph<TShape, TMaterialized> Async() =>
                AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
        }

        #endregion

        private readonly Lazy<IModule> _module;

        /// <summary>
        /// TBD
        /// </summary>
        protected GraphStageWithMaterializedValue()
        {
            _module =
                new Lazy<IModule>(
                    () =>
                        new GraphStageModule(Shape, InitialAttributes,
                            (IGraphStageWithMaterializedValue<Shape, object>)this));
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual Attributes InitialAttributes => Attributes.None;

        /// <summary>
        /// TBD
        /// </summary>
        public abstract TShape Shape { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public IGraph<TShape, TMaterialized> WithAttributes(Attributes attributes) =>
            new Graph(Shape, Module, attributes);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public abstract ILogicAndMaterializedValue<TMaterialized> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes);

        /// <summary>
        /// TBD
        /// </summary>
        public IModule Module => _module.Value;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="attributes">TBD</param>
        /// <returns>TBD</returns>
        public IGraph<TShape, TMaterialized> AddAttributes(Attributes attributes) =>
            WithAttributes(Module.Attributes.And(attributes));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public IGraph<TShape, TMaterialized> Named(string name) => AddAttributes(Attributes.CreateName(name));

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public IGraph<TShape, TMaterialized> Async() =>
            AddAttributes(new Attributes(Attributes.AsyncBoundary.Instance));
    }

    /// <summary>
    /// A GraphStage represents a reusable graph stream processing stage. A GraphStage consists of a <see cref="Shape"/> which describes
    /// its input and output ports and a factory function that creates a <see cref="GraphStageLogic"/> which implements the processing
    /// logic that ties the ports together.
    /// </summary>
    /// <typeparam name="TShape">TBD</typeparam>
    public abstract class GraphStage<TShape> : GraphStageWithMaterializedValue<TShape, NotUsed> where TShape : Shape
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        public sealed override ILogicAndMaterializedValue<NotUsed> CreateLogicAndMaterializedValue(
            Attributes inheritedAttributes)
            => new LogicAndMaterializedValue<NotUsed>(CreateLogic(inheritedAttributes), NotUsed.Instance);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inheritedAttributes">TBD</param>
        /// <returns>TBD</returns>
        protected abstract GraphStageLogic CreateLogic(Attributes inheritedAttributes);
    }

    /// <summary>
    /// Asynchronous callback holder that is attached to a <see cref="GraphStageLogic"/>.
    ///
    /// Initializing <see cref="Invoke"/> will eventually lead to the registered callback function being called.
    ///
    /// This holder has the same lifecycle as a stream and cannot be used before materialization is done.
    /// </summary>
    /// <remarks>
    /// Typical use cases for this are exchanging messages between stream and substreams or sending external events
    /// to a stream.
    /// </remarks>
    /// <typeparam name="T">The input type accepted by the stage.</typeparam>
    public interface IAsyncCallback<in T>
    {
        /// <summary>
        /// Dispatch an asynchronous notification. This method is thread-safe and may
        /// be invoked from external execution contexts.
        /// </summary>
        /// <remarks>
        /// For cases where it's important to know if the notification was ever processed
        /// or not, please see <see cref="InvokeWithFeedback"/>
        /// </remarks>
        void Invoke(T input);

        /// <summary>
        /// Dispatch an asynchronous notification. This method is thread-safe and may
        /// be invoked from external execution contexts.
        ///
        /// This method returns directly and the returned <see cref="Task"/> is then completed
        /// once the event has been handled by the operator. If the event triggers an exception
        /// from the handler the returned <see cref="Task"/> will be completed with that exception.
        ///
        /// If the operator was stopped before the event was handled then the Task will fail with a
        /// <see cref="StreamDetachedException"/>.
        /// </summary>
        /// <remarks>
        /// The handling of the returned <see cref="Task"/> incurs slight overhead, so in cases where you don't
        /// need an explicit reply please use <see cref="Invoke"/> instead.
        /// </remarks>
        Task<Done> InvokeWithFeedback(T input);
    }

    /// <summary>
    /// Timer-driven graph stage logic.
    /// </summary>
    /// <remarks>
    ///  To be thread safe the methods of this class must only be called from either the constructor of the graph operator during
    /// materialization or one of the methods invoked by the graph operator machinery, such as <see cref="IInHandler.OnPush"/>
    /// </remarks>
    public abstract class TimerGraphStageLogic : GraphStageLogic
    {
        private readonly IDictionary<object, TimerMessages.Timer> _keyToTimers =
            new Dictionary<object, TimerMessages.Timer>();

        private readonly AtomicCounter _timerIdGen = new(0);
        private Action<TimerMessages.Scheduled> _timerAsyncCallback;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
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
            if (_keyToTimers.TryGetValue(scheduled.TimerKey, out var timer) && timer.Id == id)
            {
                if (!scheduled.IsRepeating)
                    _keyToTimers.Remove(scheduled.TimerKey);
                OnTimer(scheduled.TimerKey);
            }
        }

        /// <summary>
        /// Will be called when the scheduled timer is triggered.
        /// </summary>
        /// <param name="timerKey">TBD</param>
        protected internal abstract void OnTimer(object timerKey);

        /// <summary>
        /// Schedule timer to call <see cref="OnTimer"/> periodically with the given interval after the specified
        /// initial delay.
        /// Any existing timer with the same key will automatically be canceled before
        /// adding the new timer.
        /// </summary>
        /// <param name="timerKey">TBD</param>
        /// <param name="initialDelay">TBD</param>
        /// <param name="interval">TBD</param>
        protected internal void ScheduleRepeatedly(object timerKey, TimeSpan initialDelay, TimeSpan interval)
        {
            CancelTimer(timerKey);
            var id = _timerIdGen.IncrementAndGet();
            var task = Interpreter.Materializer.ScheduleRepeatedly(initialDelay, interval,
                () => { TimerAsyncCallback(new TimerMessages.Scheduled(timerKey, id, isRepeating: true)); });
            _keyToTimers[timerKey] = new TimerMessages.Timer(id, task);
        }

        /// <summary>
        /// Schedule timer to call <see cref="OnTimer"/> periodically with the given interval after the specified
        /// initial delay.
        /// Any existing timer with the same key will automatically be canceled before
        /// adding the new timer.
        /// </summary>
        /// <param name="timerKey">TBD</param>
        /// <param name="interval">TBD</param>
        protected internal void ScheduleRepeatedly(object timerKey, TimeSpan interval)
            => ScheduleRepeatedly(timerKey, interval, interval);

        /// <summary>
        /// Schedule timer to call <see cref="OnTimer"/> after given delay.
        /// Any existing timer with the same key will automatically be canceled before
        /// adding the new timer.
        /// </summary>
        /// <param name="timerKey">TBD</param>
        /// <param name="delay">TBD</param>
        protected internal void ScheduleOnce(object timerKey, TimeSpan delay)
        {
            CancelTimer(timerKey);
            var id = _timerIdGen.IncrementAndGet();
            var task = Interpreter.Materializer.ScheduleOnce(delay,
                () => { TimerAsyncCallback(new TimerMessages.Scheduled(timerKey, id, isRepeating: false)); });
            _keyToTimers[timerKey] = new TimerMessages.Timer(id, task);
        }

        /// <summary>
        /// Cancel timer, ensuring that the <see cref="OnTimer"/> is not subsequently called.
        /// </summary>
        /// <param name="timerKey">key of the timer to cancel</param>
        protected internal void CancelTimer(object timerKey)
        {
            if (_keyToTimers.TryGetValue(timerKey, out var timer))
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
        /// <param name="timerKey">TBD</param>
        /// <returns>TBD</returns>
        protected internal bool IsTimerActive(object timerKey) => _keyToTimers.ContainsKey(timerKey);

        // Internal hooks to avoid reliance on user calling super in postStop
        /// <summary>
        /// TBD
        /// </summary>
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

    /// <summary>
    /// TBD
    /// </summary>
    internal static class TimerMessages
    {
        /// <summary>
        /// TBD
        /// </summary>
        [Serializable]
        public sealed class Scheduled : IDeadLetterSuppression
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly object TimerKey;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly int TimerId;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly bool IsRepeating;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="timerKey">TBD</param>
            /// <param name="timerId">TBD</param>
            /// <param name="isRepeating">TBD</param>
            /// <exception cref="ArgumentNullException">
            /// This exception is thrown when the specified <paramref name="timerKey"/> is undefined.
            /// </exception>
            public Scheduled(object timerKey, int timerId, bool isRepeating)
            {
                TimerKey = timerKey ?? throw new ArgumentNullException(nameof(timerKey), "Timer key cannot be null");
                TimerId = timerId;
                IsRepeating = isRepeating;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Timer
        {
            /// <summary>
            /// TBD
            /// </summary>
            public readonly int Id;

            /// <summary>
            /// TBD
            /// </summary>
            public readonly ICancelable Task;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="id">TBD</param>
            /// <param name="task">TBD</param>
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
    /// The stage logic is completed once all its input and output ports have been closed. This can be changed by
    /// setting <see cref="SetKeepGoing"/> to true.
    /// <para />
    /// The <see cref="PostStop"/> lifecycle hook on the logic itself is called once all ports are closed. This is the only tear down
    /// callback that is guaranteed to happen, if the actor system or the materializer is terminated the handlers may never
    /// see any callbacks to <see cref="InHandler.OnUpstreamFailure"/>, <see cref="InHandler.OnUpstreamFinish"/> or <see cref="OutHandler.OnDownstreamFinish"/>. 
    /// Therefore stage resource cleanup should always be done in <see cref="PostStop"/>.
    /// </summary>
    public abstract class GraphStageLogic : IStageLogging
    {
        private ImmutableList<IConcurrentAsyncCallback> _callbacksWaitingForInterpreter = ImmutableList<IConcurrentAsyncCallback>.Empty;

        // needs to be accessible via the ConcurrentAsyncCallback
        private AtomicReference<ImmutableList<TaskCompletionSource<Done>>?> _asyncCallbacksInProgress =
            new(ImmutableList<TaskCompletionSource<Done>>.Empty);

        public static readonly TaskCompletionSource<Done> NoPromise;

        static GraphStageLogic()
        {
            NoPromise = TaskEx.NonBlockingTaskCompletionSource<Done>();
            NoPromise.SetResult(Done.Instance);
        }
        
        private StreamDetachedException StreamDetachedException =>
            new StreamDetachedException($"Stage with GraphStageLogic [{this}] stopped before async invocation was processed");

        #region internal classes

        private sealed class Reading<T> : InHandler
        {
            private readonly Inlet<T> _inlet;
            private int _n;
            public readonly IInHandler Previous;
            private readonly Action<T> _andThen;
            private readonly Action _onComplete;
            private readonly GraphStageLogic _logic;

            public Reading(Inlet<T> inlet, int n, IInHandler previous, Action<T> andThen, Action onComplete,
                GraphStageLogic logic)
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

                if (_n > 0)
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
                    // If (while executing andThen() callback) handler was changed to new emitting,
                    // we should add it to the end of emission queue
                    var currentHandler = Logic.GetHandler(Out);
                    if (currentHandler is Emitting e)
                        AddFollowUp(e);

                    var next = Dequeue();
                    if (next is EmittingCompletion completion)
                    {
                        // If next element is emitting completion and there are some elements after it,
                        // we to need pass them before completion
                        if (completion.FollowUps != null)
                            Logic.SetHandler(Out, DequeueHeadAndAddToTail(completion));
                        else
                            Logic.Complete(Out);
                    }
                    else
                        Logic.SetHandler(Out, next);
                }
            }

            private IOutHandler DequeueHeadAndAddToTail(Emitting head)
            {
                var next = head.Dequeue();
                next.AddFollowUp(head);
                head.FollowUps = null;
                head.FollowUpsTail = null;
                return next;
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
            private Emitting Dequeue()
            {
                var result = FollowUps;
                result.FollowUpsTail = FollowUpsTail;
                return result;
            }

            public override void OnDownstreamFinish(Exception cause) => Previous.OnDownstreamFinish(cause);
        }

        private sealed class EmittingSingle<T> : Emitting
        {
            private readonly T _element;

            public EmittingSingle(Outlet<T> @out, T element, IOutHandler previous, Action andThen,
                GraphStageLogic logic) : base(@out, previous, andThen, logic)
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

            public EmittingIterator(Outlet<T> @out, IEnumerator<T> enumerator, IOutHandler previous, Action andThen,
                GraphStageLogic logic) : base(@out, previous, andThen, logic)
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
            public EmittingCompletion(Outlet @out, IOutHandler previous, GraphStageLogic logic) : base(@out, previous,
                DoNothing, logic)
            {
            }

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

        /// <summary>
        /// TBD
        /// </summary>
        protected sealed class LambdaInHandler : InHandler
        {
            private readonly Action _onPush;
            private readonly Action _onUpstreamFinish;
            private readonly Action<Exception> _onUpstreamFailure;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="onPush">TBD</param>
            /// <param name="onUpstreamFinish">TBD</param>
            /// <param name="onUpstreamFailure">TBD</param>
            public LambdaInHandler(Action onPush, Action onUpstreamFinish = null,
                Action<Exception> onUpstreamFailure = null)
            {
                _onPush = onPush;
                _onUpstreamFinish = onUpstreamFinish;
                _onUpstreamFailure = onUpstreamFailure;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void OnPush() => _onPush();

            /// <summary>
            /// TBD
            /// </summary>
            public override void OnUpstreamFinish()
            {
                if (_onUpstreamFinish != null)
                    _onUpstreamFinish();
                else
                    base.OnUpstreamFinish();
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="e">TBD</param>
            public override void OnUpstreamFailure(Exception e)
            {
                if (_onUpstreamFailure != null)
                    _onUpstreamFailure(e);
                else
                    base.OnUpstreamFailure(e);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected sealed class LambdaOutHandler : OutHandler
        {
            private readonly Action _onPull;
            private readonly Action<Exception> _onDownstreamFinish;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="onPull">TBD</param>
            /// <param name="onDownstreamFinish">TBD</param>
            public LambdaOutHandler(Action onPull, Action<Exception> onDownstreamFinish = null)
            {
                _onPull = onPull;
                _onDownstreamFinish = onDownstreamFinish;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public override void OnPull() => _onPull();

            /// <summary>
            /// TBD
            /// </summary>
            public override void OnDownstreamFinish(Exception cause)
            {
                if (_onDownstreamFinish != null)
                    _onDownstreamFinish(cause);
                else
                    base.OnDownstreamFinish(cause);
            }
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal static class ConcurrentAsyncCallbackState
        {
            public interface IState<TE>;

            /// <summary>
            /// Event with feedback promise and optional producer-thread trace context.
            /// <paramref name="IngressContext"/> is captured from <see cref="Activity.Current"/>
            /// at <see cref="ConcurrentAsyncCallback{T}.InvokeWithPromise"/> time on whatever
            /// external thread is dispatching into the interpreter. When non-null, the interpreter
            /// wraps the handler invocation in an "akka.stream.ingress" Activity so downstream
            /// stages can parent to the producer's trace.
            /// </summary>
            public sealed record Event<TE>(TE Evt, TaskCompletionSource<Done> HandlingPromise, ActivityContext? IngressContext = null);

            /// <summary>
            /// Waiting for materialization completion or during dispatching of internally queued events
            /// </summary>
            public sealed record Pending<TE>(ImmutableList<Event<TE>> PendingEvents) : IState<TE>
            {
                public static readonly Pending<TE> Empty = new(ImmutableList<Event<TE>>.Empty);
            }

            /// <summary>
            /// Stream is initialized and so now threads can send events without any sync overhead
            /// </summary>
            public sealed class Initialized<TE> : IState<TE>
            {
                public static readonly Initialized<TE> Instance = new();

                private Initialized()
                {
                }
            }
        }

        /// <summary>
        /// Used by the <see cref="GraphStageLogic"/> to invoke all queue events
        /// once the interpreter is started.
        /// </summary>
        private interface IConcurrentAsyncCallback
        {
            void OnStart();
        }


        /// <summary>
        /// ConcurrentAsyncCallback allows to send events to a stream from multiple threads regardless of
        /// the initialization / materialization state of the stream in a thread-safe manner.
        ///
        /// The state of this object can be changed both "internally" by the owning <see cref="GraphStage{TShape}"/> or
        /// by the external world, i.e. external threads and callers.
        ///
        /// Specifically, calls to this class can be made:
        ///  - From the owning <see cref="GraphStage{TShape}"/>, TO 
        /// </summary>
        /// <typeparam name="T">The type of input argument accepted</typeparam>
        internal sealed class ConcurrentAsyncCallback<T> : IAsyncCallback<T>, IConcurrentAsyncCallback
        {
            // need this to make the GraphInterpreter happy
            private readonly Action<object> _wrappedHandler;
            private readonly GraphStageLogic _ownedStage;

            private readonly AtomicReference<ConcurrentAsyncCallbackState.IState<T>> _state =
                new(ConcurrentAsyncCallbackState.Pending<T>.Empty);

            public ConcurrentAsyncCallback(Action<T> handler, GraphStageLogic ownedStage)
            {
                var handler1 = handler;
                _ownedStage = ownedStage;
                _wrappedHandler = obj =>
                {
                    switch (obj)
                    {
                        // Always assume that T can be null and the handler will handle null values
                        case null:
                            handler1(default);
                            break;
                        case T e:
                            handler1(e);
                            break;
                        default:
                            throw new ArgumentException($"Expected {nameof(obj)} to be of type {typeof(T)}, but was {obj.GetType()}");
                    }
                };
            }

            /// <summary>
            /// Called from the owning <see cref="GraphStage{TShape}"/>.
            /// </summary>
            /// <exception cref="IllegalStateException"></exception>
            public void OnStart()
            {
                while (true)
                {
                    // dispatch callbacks that have been queued from before the interpreter was started
                    var current = _state.GetAndSet(ConcurrentAsyncCallbackState.Pending<T>.Empty);
                    switch (current)
                    {
                        case ConcurrentAsyncCallbackState.Pending<T> pending:
                            if (pending.PendingEvents.Count > 0)
                            {
                                foreach (var ev in pending.PendingEvents)
                                {
                                    OnAsyncInput(ev.Evt, ev.HandlingPromise, ev.IngressContext);
                                }
                            }

                            break;
                        default:
                            throw new IllegalStateException($"Unexpected callback state: {current}");
                    }

                    // in the meantime, more callbacks might have been queued (we keep queuing them to ensure order)
                    if (!_state.CompareAndSet(ConcurrentAsyncCallbackState.Pending<T>.Empty,
                            ConcurrentAsyncCallbackState.Initialized<T>.Instance))
                        // state guaranteed to still be pending
                        continue;
                    break;
                }
            }

            private void OnAsyncInput(T e, TaskCompletionSource<Done> promise, ActivityContext? ingressContext)
            {
                if (!ingressContext.HasValue)
                {
                    // Fast path: no producer trace context captured — dispatch without wrapping.
                    _ownedStage.Interpreter.OnAsyncInput(_ownedStage, e, promise, _wrappedHandler);
                    return;
                }

                // Start an ingress Activity on the interpreter thread so Push captures
                // the producer's trace into Connection.SlotContext.
                var capturedContext = ingressContext.Value;
                var owner = _ownedStage;
                var realHandler = _wrappedHandler;
                var ingressOpName = StreamsDiagnostics.GetIngressOperationName(owner);
                Action<object> contextAwareHandler = obj =>
                {
                    using var ingress = StreamsDiagnostics.ActivitySource.StartActivity(
                        ingressOpName,
                        ActivityKind.Internal,
                        capturedContext);
                    realHandler(obj);
                };
                _ownedStage.Interpreter.OnAsyncInput(_ownedStage, e, promise, contextAwareHandler);
            }

            // External call
            public void Invoke(T input)
            {
                InvokeWithPromise(input, NoPromise);
            }

            // External call
            public Task<Done> InvokeWithFeedback(T input)
            {
                var promise = TaskEx.NonBlockingTaskCompletionSource<Done>();

                if (AddToWaiting())
                {
                    InvokeWithPromise(input, promise);
                    return promise.Task;
                }

                return Task.FromException<Done>(_ownedStage.StreamDetachedException);

                /*
                 * Add this task completion source to the owning logic, so it can be completed `AfterPostStop`
                 * if it was never handled otherwise. Returns `true` if the logic is still running, `false` otherwise.
                 */
                bool AddToWaiting()
                {
                    while (true)
                    {
                        var previous = _ownedStage._asyncCallbacksInProgress.Value;
                        if (previous != null) // not stopped
                        {
                            // prepend to front of list
                            var updated = previous.Add(promise);
                            if (!_ownedStage._asyncCallbacksInProgress.CompareAndSet(previous, updated))
                                continue;
                            return true;
                        }

                        // logic was already stopped
                        return false;
                    }
                }
            }

            private void InvokeWithPromise(T evt, TaskCompletionSource<Done> promise)
            {
                // Capture the producer thread's trace context BEFORE the async handoff so we can
                // restore it on the interpreter thread. AsyncLocal does not flow across the stream
                // actor boundary, so this is the one place we can observe Activity.Current from the
                // external caller's perspective. Skip entirely when no one is listening to the
                // "Akka.Streams" ActivitySource — avoids a per-invoke Activity.Current property
                // access on the non-traced fast path.
                ActivityContext? ingressContext = StreamsDiagnostics.ActivitySource.HasListeners()
                    ? Activity.Current?.Context
                    : null;

                while (true)
                {
                    var state = _state.Value;
                    switch (state)
                    {
                        case ConcurrentAsyncCallbackState.Initialized<T>:
                            // started - can just dispatch async message to interpreter
                            OnAsyncInput(evt, promise, ingressContext);
                            break;

                        case ConcurrentAsyncCallbackState.Pending<T> list:
                        {
                            // not started yet - queue the event with its captured ingress context
                            var e = new ConcurrentAsyncCallbackState.Event<T>(evt, promise, ingressContext);
                            var newList = list.PendingEvents.Add(e);
                            if (!_state.CompareAndSet(list, new ConcurrentAsyncCallbackState.Pending<T>(newList))) continue;
                            break;
                        }
                    }

                    break;
                }
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
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static InHandler ConditionalTerminateInput(Func<bool> predicate) =>
            new ConditionalTerminateInput(predicate);

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

        /// <summary>
        /// Empty cached delegate
        /// </summary>
        public static readonly Action DoNothing = () => { };

        /// <summary>
        /// Output handler that terminates the state upon receiving completion if the
        /// given condition holds at that time. The stage fails upon receiving a failure.
        /// </summary>
        /// <param name="predicate">TBD</param>
        /// <returns>TBD</returns>
        public static OutHandler ConditionalTerminateOutput(Func<bool> predicate) =>
            new ConditionalTerminateOutput(predicate);

        /// <summary>
        /// TBD
        /// </summary>
        public readonly int InCount;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly int OutCount;

        /// <summary>
        /// TBD
        /// </summary>
        internal readonly object[] Handlers;

        /// <summary>
        /// TBD
        /// </summary>
        internal readonly Connection[] PortToConn;

        /// <summary>
        /// TBD
        /// </summary>
        internal int StageId = int.MinValue;

        private GraphInterpreter _interpreter;

        /// <summary>
        /// INTERNAL API: The <see cref="GraphInterpreter"/> that is running this stage.
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the class is not initialized.
        /// </exception>
        internal GraphInterpreter Interpreter
        {
            get
            {
                if (_interpreter == null)
                    throw new IllegalStateException(
                        "Not yet initialized: only SetHandler is allowed in GraphStageLogic constructor. " +
                        "To access materializer use Source/Flow/Sink.Setup factory");
                return _interpreter;
            }
            set
            {
                _interpreter = value;
            }
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inCount">TBD</param>
        /// <param name="outCount">TBD</param>
        protected GraphStageLogic(int inCount, int outCount)
        {
            InCount = inCount;
            OutCount = outCount;
            Handlers = new object[InCount + OutCount];
            PortToConn = new Connection[Handlers.Length];
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        protected GraphStageLogic(Shape shape) : this(shape.Inlets.Count(), shape.Outlets.Count())
        {
            LogSource = Akka.Event.LogSource.Create(shape.ToString());
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

        private StageActor _stageActor;

        /// <summary>
        /// TBD
        /// </summary>
        public StageActor StageActor
        {
            get
            {
                if (_stageActor == null)
                    throw StageActorRefNotInitializedException.Instance;
                return _stageActor;
            }
        }

        private ILoggingAdapter _log;

        /// <summary>
        /// Override to customise reported log source 
        /// </summary>
        protected virtual object LogSource { get; }

        public ILoggingAdapter Log
        {
            get
            {
                // only used in StageLogic, i.e. thread safe
                if (_log == null)
                {
                    if (Materializer is IMaterializerLoggingProvider provider)
                        _log = provider.MakeLogger(LogSource);
                    else
                        _log = NoLogger.Instance;
                }

                return _log;
            }
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Inlet{T}"/>.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="handler">TBD</param>
        protected internal void SetHandler<T>(Inlet<T> inlet, IInHandler handler)
        {
            Handlers[inlet.Id] = handler;
            _interpreter?.SetHandler(GetConnection(inlet), handler);
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="onPush">TBD</param>
        /// <param name="onUpstreamFinish">TBD</param>
        /// <param name="onUpstreamFailure">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="onPush"/> is undefined.
        /// </exception>
        protected internal void SetHandler<T>(Inlet<T> inlet, Action onPush, Action onUpstreamFinish = null,
            Action<Exception> onUpstreamFailure = null)
        {
            if (onPush == null)
                throw new ArgumentNullException(nameof(onPush), "GraphStageLogic onPush handler must be provided");

            SetHandler(inlet, new LambdaInHandler(onPush, onUpstreamFinish, onUpstreamFailure));
        }

        /// <summary>
        /// Retrieves the current callback for the events on the given <see cref="Inlet{T}"/>
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        protected IInHandler GetHandler<T>(Inlet<T> inlet) => (IInHandler)Handlers[inlet.Id];

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="handler">TBD</param>
        private void SetHandler(Outlet outlet, IOutHandler handler)
        {
            Handlers[outlet.Id + InCount] = handler;
            _interpreter?.SetHandler(GetConnection(outlet), handler);
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="handler">TBD</param>
        protected internal void SetHandler<T>(Outlet<T> outlet, IOutHandler handler) =>
            SetHandler((Outlet)outlet, handler);

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Outlet{T}"/>.
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="onPull">TBD</param>
        /// <param name="onDownstreamFinish">TBD</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="onPull"/> is undefined.
        /// </exception>
        protected internal void SetHandler<T>(Outlet<T> outlet, Action onPull,
            Action<Exception> onDownstreamFinish = null)
        {
            if (onPull == null)
                throw new ArgumentNullException(nameof(onPull), "GraphStageLogic onPull handler must be provided");
            SetHandler(outlet, new LambdaOutHandler(onPull, onDownstreamFinish));
        }

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Inlet{T}"/> and <see cref="Outlet{T}"/>.
        /// </summary>
        [Obsolete("Use method `SetHandlers` instead. Will be removed in v1.5")]
        protected internal void SetHandler<TIn, TOut>(Inlet<TIn> inlet, Outlet<TOut> outlet,
            InAndOutGraphStageLogic handler) =>
            SetHandlers(inlet, outlet, handler);

        /// <summary>
        /// Assigns callbacks for the events for an <see cref="Inlet{T}"/> and <see cref="Outlet{T}"/>.
        /// </summary>
        protected internal void SetHandlers<TIn, TOut>(Inlet<TIn> inlet, Outlet<TOut> outlet,
            InAndOutGraphStageLogic handler)
        {
            SetHandler(inlet, handler);
            SetHandler(outlet, handler);
        }

        /// <summary>
        /// Retrieves the current callback for the events on the given <see cref="Outlet{T}"/>
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <returns>TBD</returns>
        private IOutHandler GetHandler(Outlet outlet) => (IOutHandler)Handlers[outlet.Id + InCount];

        /// <summary>
        /// Retrieves the current callback for the events on the given <see cref="Outlet{T}"/>
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <returns>TBD</returns>
        protected IOutHandler GetHandler<T>(Outlet<T> outlet) => GetHandler((Outlet)outlet);

        private Connection GetConnection(Inlet inlet) => PortToConn[inlet.Id];

        private Connection GetConnection(Outlet outlet) => PortToConn[outlet.Id + InCount];

        private IOutHandler GetNonEmittingHandler(Outlet outlet)
        {
            var h = GetHandler(outlet);
            return h is Emitting e ? e.Previous : h;
        }

        /// <summary>
        /// Requests an element on the given port. Calling this method twice before an element arrived will fail.
        /// There can only be one outstanding request at any given time.The method <see cref="HasBeenPulled"/> can be used
        /// query whether pull is allowed to be called or not.This method will also fail if the port is already closed.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="inlet"/> is closed or already pulled.
        /// </exception>
        private void Pull(Inlet inlet)
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        protected internal void Pull<T>(Inlet<T> inlet) => Pull((Inlet)inlet);

        /// <summary>
        /// Requests an element on the given port unless the port is already closed.
        /// Calling this method twice before an element arrived will fail.
        /// There can only be one outstanding request at any given time.The method <see cref="HasBeenPulled"/> can be used
        /// query whether pull is allowed to be called or not.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        protected internal void TryPull<T>(Inlet<T> inlet)
        {
            if (!IsClosed(inlet))
                Pull(inlet);
        }

        /// <summary>
        /// Requests to stop receiving events from a given input port. Cancelling clears any ungrabbed elements from the port.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <param name="cause"></param>
        protected void Cancel<T>(Inlet<T> inlet, Exception cause) => Interpreter.Cancel(GetConnection(inlet), cause);

        protected void Cancel<T>(Inlet<T> inlet) => Interpreter.Cancel(GetConnection(inlet),
            SubscriptionWithCancelException.NoMoreElementsNeeded.Instance);

        /// <summary>
        /// Once the callback <see cref="InHandler.OnPush"/> for an input port has been invoked, the element that has been pushed
        /// can be retrieved via this method. After <see cref="Grab{T}(Inlet)"/> has been called the port is considered to be empty, and further
        /// calls to <see cref="Grab{T}(Inlet)"/> will fail until the port is pulled again and a new element is pushed as a response.
        /// 
        /// The method <see cref="IsAvailable(Inlet)"/> can be used to query if the port has an element that can be grabbed or not.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="inlet"/> is empty.
        /// </exception>
        /// <returns>TBD</returns>
        private T Grab<T>(Inlet inlet)
        {
            var connection = GetConnection(inlet);
            var element = connection.Slot;

            if ((connection.PortState & (InReady | InFailed | InClosed)) == InReady &&
                !ReferenceEquals(element, Empty.Instance))
            {
                // fast path
                connection.Slot = Empty.Instance;
                if (connection.SlotContext.HasValue)
                {
                    connection.SlotContext = null;
                    connection.SlotLinks = null;
                }
                return (T)element;
            }

            // Slow path for grabbing element from already failed or completed connections
            if (!IsAvailable(inlet))
                throw new ArgumentException($"Cannot get element from already empty input port ({inlet})");

            if ((connection.PortState & (InReady | InFailed)) == (InReady | InFailed))
            {
                // failed
                var failed = (GraphInterpreter.Failed)element;
                connection.Slot = new GraphInterpreter.Failed(failed.Reason, Empty.Instance);
                // keep SlotContext / SlotLinks — caller still needs them for the retry path
                return (T)failed.PreviousElement;
            }

            // Completed
            var elem = (T)connection.Slot;
            connection.Slot = Empty.Instance;
            if (connection.SlotContext.HasValue)
            {
                connection.SlotContext = null;
                connection.SlotLinks = null;
            }
            return elem;
        }

        /// <summary>
        /// Once the callback <see cref="InHandler.OnPush"/> for an input port has been invoked, the element that has been pushed
        /// can be retrieved via this method. After <see cref="Grab{T}(Inlet{T})"/> has been called the port is considered to be empty, and further
        /// calls to <see cref="Grab{T}(Inlet{T})"/> will fail until the port is pulled again and a new element is pushed as a response.
        /// 
        /// The method <see cref="IsAvailable(Inlet)"/> can be used to query if the port has an element that can be grabbed or not.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        protected internal T Grab<T>(Inlet<T> inlet) => Grab<T>((Inlet)inlet);

        /// <summary>
        /// Indicates whether there is already a pending pull for the given input port. If this method returns true 
        /// then <see cref="IsAvailable(Inlet)"/> must return false for that same port.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        private bool HasBeenPulled(Inlet inlet)
            => (GetConnection(inlet).PortState & (InReady | InClosed)) == 0;

        /// <summary>
        /// Indicates whether there is already a pending pull for the given input port. If this method returns true 
        /// then <see cref="IsAvailable(Inlet)"/> must return false for that same port.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        protected bool HasBeenPulled<T>(Inlet<T> inlet) => HasBeenPulled((Inlet)inlet);

        /// <summary>
        /// Indicates whether there is an element waiting at the given input port. <see cref="Grab{T}(Inlet{T})"/> can be used to retrieve the
        /// element. After calling <see cref="Grab{T}(Inlet{T})"/> this method will return false.
        /// 
        /// If this method returns true then <see cref="HasBeenPulled"/> will return false for that same port.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        private bool IsAvailable(Inlet inlet)
        {
            var connection = GetConnection(inlet);
            var normalArrived = (connection.PortState & (InReady | InFailed | InClosed)) == InReady;

            if (normalArrived)
            {
                // fast path
                return !ReferenceEquals(connection.Slot, Empty.Instance);
            }

            // slow path on failure
            if ((connection.PortState & (InReady | InClosed | InFailed)) == (InReady | InClosed))
            {
                return connection.Slot switch
                {
                    Empty _ => false, // cancelled (element is discarded when cancelled)
                    Cancelled _ => false, // cancelled (element is discarded when cancelled)
                    _ => true // completed but element still there to grab
                };
            }

            if ((connection.PortState & (InReady | InFailed)) == (InReady | InFailed))
            {
                return connection.Slot switch
                {
                    GraphInterpreter.Failed failed => !ReferenceEquals(failed.PreviousElement,
                        Empty.Instance), // failed but element still there to grab
                    _ => false
                };
            }

            return false;
        }

        /// <summary>
        /// Indicates whether there is an element waiting at the given input port. <see cref="Grab{T}(Inlet{T})"/> can be used to retrieve the
        /// element. After calling <see cref="Grab{T}(Inlet{T})"/> this method will return false.
        /// 
        /// If this method returns true then <see cref="HasBeenPulled"/> will return false for that same port.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        protected internal bool IsAvailable<T>(Inlet<T> inlet) => IsAvailable((Inlet)inlet);

        /// <summary>
        /// Indicates whether the port has been closed. A closed port cannot be pulled.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        private bool IsClosed(Inlet inlet) => (GetConnection(inlet).PortState & InClosed) != 0;

        /// <summary>
        /// Indicates whether the port has been closed. A closed port cannot be pulled.
        /// </summary>
        /// <param name="inlet">TBD</param>
        /// <returns>TBD</returns>
        protected bool IsClosed<T>(Inlet<T> inlet) => IsClosed((Inlet)inlet);

        /// <summary>
        /// Emits an element through the given output port. Calling this method twice before a <see cref="Pull{T}(Inlet{T})"/> has been arrived
        /// will fail. There can be only one outstanding push request at any given time. The method <see cref="IsAvailable(Inlet)"/> can be
        /// used to check if the port is ready to be pushed or not.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="element">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the specified <paramref name="outlet"/> is closed or already pulled.
        /// </exception>
        protected internal void Push<T>(Outlet<T> outlet, T element)
        {
            var connection = GetConnection(outlet);
            var portState = connection.PortState;

            connection.PortState = portState ^ PushStartFlip;
            if ((portState & (OutReady | OutClosed | InClosed)) == OutReady && element != null)
            {
                connection.Slot = element;
                if (StreamsDiagnostics.ActivitySource.HasListeners())
                {
                    if (connection.PendingPushPrimaryContext.HasValue)
                    {
                        connection.SlotContext = connection.PendingPushPrimaryContext;
                        connection.SlotLinks = connection.PendingPushLinks;
                        connection.PendingPushPrimaryContext = null;
                        connection.PendingPushLinks = null;
                    }
                    else
                    {
                        connection.SlotContext = Activity.Current?.Context;
                        connection.SlotLinks = null;
                    }
                }
                Interpreter.ChasePush(connection);
            }
            else
            {
                // Restore state for the error case
                connection.PortState = portState;

                // Detailed error information should not add overhead to the hot path
                ReactiveStreamsCompliance.RequireNonNullElement(element);
                if (IsClosed(outlet)) throw new ArgumentException($"Cannot push closed port {outlet}");
                if (!IsAvailable(outlet)) throw new ArgumentException($"Cannot push port twice {outlet}");

                // No error, just InClosed caused the actual pull to be ignored, but the status flag still needs to be flipped
                connection.PortState = portState ^ PushStartFlip;
            }
        }

        /// <summary>
        /// Reads the trace context that the upstream stage attached to the next element waiting
        /// at <paramref name="inlet"/>. Returns <c>null</c> if no element is pending or if the
        /// upstream Push happened outside any traced scope. Designed to be called from inside
        /// <c>OnPush</c> BEFORE <c>Grab</c> — fan-in stages use it to remember which producer
        /// trace each input element came from so that a later flushed batch can link back to
        /// all of them via <see cref="ActivityLink"/>s.
        /// </summary>
        internal ActivityContext? CurrentInletTraceContext<T>(Inlet<T> inlet)
            => GetConnection(inlet).SlotContext;

        /// <summary>
        /// Marks the next <c>Push</c> on <paramref name="outlet"/> as a fan-in flush — it will
        /// carry <paramref name="primary"/> as its primary trace parent and the entries in
        /// <paramref name="links"/> as additional <see cref="ActivityLink"/>s on the downstream
        /// stage span. Used by <c>BatchWeighted</c>, <c>GroupedWithin</c>, <c>Merge</c>, and
        /// other fan-in stages so that one flushed output element preserves trace continuity
        /// with all N input elements that contributed to it.
        ///
        /// The override is consumed (and cleared) by the very next <c>Push</c> on the outlet.
        /// If the next <c>Push</c> never happens (e.g. stage failure), the override is harmless
        /// and is replaced on the next call.
        /// </summary>
        internal void SetFanInTraceContext<T>(
            Outlet<T> outlet,
            ActivityContext primary,
            IReadOnlyList<ActivityContext> links)
        {
            var connection = GetConnection(outlet);
            connection.PendingPushPrimaryContext = primary;
            if (links == null || links.Count == 0)
            {
                connection.PendingPushLinks = null;
            }
            else
            {
                var arr = new ActivityContext[links.Count];
                for (int i = 0; i < links.Count; i++) arr[i] = links[i];
                connection.PendingPushLinks = arr;
            }
        }

        /// <summary>
        /// Pass-through fan-in helper for 1-to-1 merge stages (<c>Merge</c>, <c>MergePreferred</c>,
        /// <c>Concat</c>, etc.): reads the upstream context off <paramref name="inlet"/>, grabs the
        /// element, stages the upstream context as the primary parent for the next push on
        /// <paramref name="outlet"/>, and pushes. Used so that multi-producer merge stages preserve
        /// each element's originating TraceId across the slow-path <c>OnPull</c> boundary where
        /// <c>Activity.Current</c> would otherwise be null. Single-source stages that don't cross
        /// an interpreter dispatcher hop between <c>Grab</c> and <c>Push</c> don't need this — plain
        /// <c>Push(outlet, Grab(inlet))</c> captures <c>Activity.Current</c> correctly.
        /// </summary>
        internal void GrabAndPushFanIn<TIn, TOut>(Inlet<TIn> inlet, Outlet<TOut> outlet) where TIn : TOut
        {
            var ctx = CurrentInletTraceContext(inlet);
            var element = Grab(inlet);
            if (ctx.HasValue) SetFanInTraceContext(outlet, ctx.Value, null);
            Push(outlet, element);
        }

        /// <summary>
        /// Like <see cref="GrabAndPushFanIn{TIn,TOut}"/> but uses <see cref="Emit{T}(Outlet{T},T,Action)"/>
        /// instead of <c>Push</c>, for fan-in stages that need the deferred-pull callback overload.
        /// </summary>
        internal void GrabAndEmitFanIn<TIn, TOut>(Inlet<TIn> inlet, Outlet<TOut> outlet, Action andThen = null) where TIn : TOut
        {
            var ctx = CurrentInletTraceContext(inlet);
            var element = Grab(inlet);
            if (ctx.HasValue) SetFanInTraceContext(outlet, ctx.Value, null);
            if (andThen != null)
                Emit(outlet, element, andThen);
            else
                Emit(outlet, element);
        }

        /// <summary>
        /// Controls whether this stage shall shut down when all its ports are closed, which
        /// is the default. In order to have it keep going past that point this method needs
        /// to be called with a true argument before all ports are closed, and afterwards
        /// it will not be closed until this method is called with a false argument or the
        /// stage is terminated via <see cref="CompleteStage"/> or <see cref="FailStage"/>.
        /// </summary>
        /// <param name="enabled">TBD</param>
        protected void SetKeepGoing(bool enabled) => Interpreter.SetKeepGoing(this, enabled);

        /// <summary>
        /// Signals that there will be no more elements emitted on the given port.
        /// </summary>
        /// <param name="outlet">TBD</param>
        private void Complete(Outlet outlet)
        {
            if (GetHandler(outlet) is Emitting e)
                e.AddFollowUp(new EmittingCompletion(e.Out, e.Previous, this));
            else
                Interpreter.Complete(GetConnection(outlet));
        }

        /// <summary>
        /// Signals that there will be no more elements emitted on the given port.
        /// </summary>
        /// <param name="outlet">TBD</param>
        protected void Complete<T>(Outlet<T> outlet) => Complete((Outlet)outlet);

        /// <summary>
        /// Signals failure through the given port.
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <param name="reason">TBD</param>
        protected void Fail<T>(Outlet<T> outlet, Exception reason) => Interpreter.Fail(GetConnection(outlet), reason);

        /// <summary>
        /// INTERNAL API
        ///
        /// Variable used from `OutHandler.onDownstreamFinish` to carry over cancellation cause in cases where
        /// `OutHandler` implementations call `InternalOnDownstreamFinish()`.
        /// </summary>
        [InternalApi] private Exception _lastCancellationCause = null;

        [InternalApi]
        public void InternalOnDownstreamFinish(Exception cause)
        {
            try
            {
                if (_lastCancellationCause != null)
                    throw new ArgumentException("OnDownstreamFinish must not be called recursively", nameof(cause));

                // Some stages might propagate null exceptions due to improper Task continuation handling
                // (see https://github.com/akkadotnet/Akka.Persistence.Sql/issues/498)
                // This is a stop gap solution to make sure that Akka.Streams doesn't behave improperly
                // until we can fix all of those
                if (cause is null)
                {
                    try
                    {
                        throw new DownstreamCompletedWithNoCauseException();
                    }
                    catch (DownstreamCompletedWithNoCauseException e)
                    {
                        cause = e;
                    }
                }

                _lastCancellationCause = cause;
                CancelStage(_lastCancellationCause);
            }
            finally
            {
                _lastCancellationCause = null;
            }
        }

        public void CancelStage(Exception cause)
        {
            switch (cause)
            {
                case SubscriptionWithCancelException.NonFailureCancellation _:
                    InternalCompleteStage(cause, Option<Exception>.None);
                    break;
                default:
                    InternalCompleteStage(cause, cause);
                    break;
            }
        }

        /// <summary>
        /// Automatically invokes <see cref="Cancel{T}(Inlet{T}, Exception)"/> or <see cref="Complete"/> on all the input or output ports that have been called,
        /// then marks the stage as stopped.
        /// </summary>
        public void CompleteStage()
            => InternalCompleteStage(SubscriptionWithCancelException.StageWasCompleted.Instance,
                Option<Exception>.None);

        /// <summary>
        /// Automatically invokes <see cref="Cancel{T}(Inlet{T}, Exception)"/> or <see cref="Fail{T}"/> on all the input or output ports that have been called,
        /// then marks the stage as stopped.
        /// </summary>
        /// <param name="reason">TBD</param>
        public void FailStage(Exception reason)
            => InternalCompleteStage(reason, reason);

        private void InternalCompleteStage(Exception cancelCause, Option<Exception> optionalFailureCause)
        {
            for (var i = 0; i < PortToConn.Length; i++)
            {
                if (i < InCount)
                {
                    Interpreter.Cancel(PortToConn[i], cancelCause);
                }
                else if (optionalFailureCause.HasValue)
                {
                    Interpreter.Fail(PortToConn[i], optionalFailureCause.Value);
                }
                else
                {
                    if (Handlers[i] is Emitting e)
                        e.AddFollowUp(new EmittingCompletion(e.Out, e.Previous, this));
                    else
                        Interpreter.Complete(PortToConn[i]);
                }
            }

            SetKeepGoing(false);
        }

        /// <summary>
        /// Return true if the given output port is ready to be pushed.
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <returns>TBD</returns>
        protected internal bool IsAvailable<T>(Outlet<T> outlet)
            => (GetConnection(outlet).PortState & (OutReady | OutClosed)) == OutReady;

        /// <summary>
        /// Indicates whether the port has been closed. A closed port cannot be pushed.
        /// </summary>
        /// <param name="outlet">TBD</param>
        /// <returns>TBD</returns>
        protected bool IsClosed<T>(Outlet<T> outlet)
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        /// <param name="n">TBD</param>
        /// <param name="andThen">TBD</param>
        /// <param name="onComplete">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="n"/> is less than zero.
        /// </exception>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the specified <paramref name="inlet"/> is currently reading.
        /// </exception>
        protected void ReadMany<T>(Inlet<T> inlet, int n, Action<IEnumerable<T>> andThen,
            Action<IEnumerable<T>> onComplete)
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
                    //If we already have data available, then short-circuit and read the first
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        /// <param name="andThen">TBD</param>
        /// <param name="onClose">TBD</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when the specified <paramref name="inlet"/> is currently reading.
        /// </exception>
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="inlet">TBD</param>
        protected void AbortReading<T>(Inlet<T> inlet)
        {
            if (GetHandler(inlet) is Reading<T> reading)
                SetHandler(inlet, reading.Previous);
        }

        private void RequireNotReading<T>(Inlet<T> inlet)
        {
            if (GetHandler(inlet) is Reading<T>)
                throw new IllegalStateException($"Already reading on inlet {inlet}");
        }

        /// <summary>
        /// Emit a sequence of elements through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="elements">TBD</param>
        /// <param name="andThen">TBD</param>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerable<T> elements, Action andThen)
            => EmitMultiple(outlet, elements.GetEnumerator(), andThen);

        /// <summary>
        /// Emit a sequence of elements through the given outlet, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="elements">TBD</param>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerable<T> elements)
            => EmitMultiple(outlet, elements, DoNothing);

        /// <summary>
        /// Emit a sequence of elements through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and reinstalls the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="enumerator">TBD</param>
        /// <param name="andThen">TBD</param>
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="enumerator">TBD</param>
        protected internal void EmitMultiple<T>(Outlet<T> outlet, IEnumerator<T> enumerator)
        {
            EmitMultiple(outlet, enumerator, DoNothing);
        }

        /// <summary>
        /// Emit an element through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and re-installs the current handler upon receiving an <see cref="OutHandler.OnPull"/>
        /// signal (before invoking the <paramref name="andThen"/> function).
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="element">TBD</param>
        /// <param name="andThen">TBD</param>
        protected internal void Emit<T>(Outlet<T> outlet, T element, Action andThen)
        {
            if (IsAvailable(outlet))
            {
                Push(outlet, element);
                andThen();
            }
            else
                SetOrAddEmitting(outlet,
                    new EmittingSingle<T>(outlet, element, GetNonEmittingHandler(outlet), andThen, this));
        }

        /// <summary>
        /// Emit an element through the given outlet and continue with the given thunk
        /// afterwards, suspending execution if necessary.
        /// This action replaces the <see cref="OutHandler"/> for the given outlet if suspension
        /// is needed and re-installs the current handler upon receiving an <see cref="OutHandler.OnPull"/>.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        /// <param name="element">TBD</param>
        protected internal void Emit<T>(Outlet<T> outlet, T element) => Emit(outlet, element, DoNothing);

        /// <summary>
        /// Abort outstanding (suspended) emissions for the given outlet, if there are any.
        /// This will reinstall the replaced handler that was in effect before the <see cref="Emit{T}(Outlet{T},T,Action)"/>
        /// call.
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="outlet">TBD</param>
        protected internal void AbortEmitting<T>(Outlet<T> outlet)
        {
            if (GetHandler(outlet) is Emitting e)
                SetHandler(outlet, e.Previous);
        }

        private void SetOrAddEmitting<T>(Outlet<T> outlet, Emitting next)
        {
            if (GetHandler(outlet) is Emitting e)
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
        /// <typeparam name="TOut">TBD</typeparam>
        /// <typeparam name="TIn">TBD</typeparam>
        /// <param name="from">TBD</param>
        /// <param name="to">TBD</param>
        /// <param name="doFinish">TBD</param>
        /// <param name="doFail">TBD</param>
        /// <param name="doPull">TBD</param>
        protected void PassAlong<TOut, TIn>(Inlet<TIn> from, Outlet<TOut> to, bool doFinish = true, bool doFail = true,
            bool doPull = false)
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
        /// In case the stream is not yet materialized, the callback will buffer events until the stream is available.
        ///
        /// <see cref="IAsyncCallback{T}.InvokeWithFeedback"/> has an internal <see cref="TaskCompletionSource{TResult}"/>
        /// that will be failed if the event cannot be processed due to stream completion.
        ///
        /// To be thread safe this method must only be called from either the constructor of the graph operator during
        /// materialization or one of the methods invoked by the graph operator machinery, such as `OnPush` and `OnPull`.
        /// 
        /// This object can be cached and reused within the same <see cref="GraphStageLogic"/>.
        /// </summary>
        protected Action<T> GetAsyncCallback<T>(Action<T> handler)
        {
            // backwards compat
            return GetTypedAsyncCallback(handler).Invoke;
        }
        
        /// <summary>
        /// Obtain a callback object that can be used asynchronously to re-enter the
        /// current <see cref="GraphStage{TShape}"/> with an asynchronous notification. The delegate returned 
        /// is safe to be called from other threads and it will in the background thread-safely
        /// delegate to the passed callback function. I.e. it will be called by the external world and
        /// the passed handler will be invoked eventually in a thread-safe way by the execution environment.
        ///
        /// In case the stream is not yet materialized, the callback will buffer events until the stream is available.
        ///
        /// <see cref="IAsyncCallback{T}.InvokeWithFeedback"/> has an internal <see cref="TaskCompletionSource{TResult}"/>
        /// that will be failed if the event cannot be processed due to stream completion.
        ///
        /// To be thread safe this method must only be called from either the constructor of the graph operator during
        /// materialization or one of the methods invoked by the graph operator machinery, such as `OnPush` and `OnPull`.
        /// 
        /// This object can be cached and reused within the same <see cref="GraphStageLogic"/>.
        /// </summary>
        protected IAsyncCallback<T> GetTypedAsyncCallback<T>(Action<T> handler)
        {
            var callback = new ConcurrentAsyncCallback<T>(handler, this);
            if (_interpreter != null) callback.OnStart();
            else _callbacksWaitingForInterpreter = _callbacksWaitingForInterpreter.Add(callback);
            
            // backwards compatibility
            return callback;
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
        protected Action GetAsyncCallback(Action handler)
        {
            // ugly
            var callback = GetTypedAsyncCallback<NotUsed>(_ => handler());
            return () => callback.Invoke(NotUsed.Instance);
        }

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
        [ApiMayChange]
        protected StageActor GetStageActor(StageActorRef.Receive receive)
        {
            if (_stageActor == null)
            {
                var actorMaterializer = ActorMaterializerHelper.Downcast(Interpreter.Materializer);
                _stageActor = new StageActor(
                    actorMaterializer,
                    r => GetAsyncCallback<(IActorRef, object)>(message => r(message)),
                    receive,
                    StageActorName);
            }
            else
                _stageActor.Become(receive);

            return _stageActor;
        }

        /// <summary>
        /// Override and return a name to be given to the StageActor of this stage.
        /// 
        /// This method will be only invoked and used once, during the first <see cref="GetStageActor"/>
        /// invocation which creates the actor, since subsequent `getStageActors` calls function
        /// like `become`, rather than creating new actors.
        /// 
        /// Returns an empty string by default, which means that the name will a unique generated String (e.g. "$$a").
        /// </summary>
        [ApiMayChange]
        protected virtual string StageActorName => "";

        protected internal virtual void BeforePreStart()
        {
            foreach(var cb in _callbacksWaitingForInterpreter)
            {
                cb.OnStart();
            }
            _callbacksWaitingForInterpreter = _callbacksWaitingForInterpreter.Clear();
        }

        protected internal virtual void AfterPostStop()
        {
            if (_stageActor != null)
            {
                _stageActor.Stop();
                _stageActor = null;
            }
            
            // make sure any InvokeWithFeedback after this fails fast
            var inProgress = _asyncCallbacksInProgress.GetAndSet(null);
            if (inProgress is { Count: > 0 })
            {
                var ex = StreamDetachedException;
                foreach (var tcs in inProgress)
                {
                    tcs.TrySetException(ex);
                }
            }
            // TODO: cleanUpSubstreams
        }

        private long _asyncCleanupCounter = 0;

        /// <summary>
        /// Called from interpreter thread by <see cref="ActorGraphInterpreter.AsyncInput"/>
        /// </summary>
        internal void OnFeedbackDispatched()
        {
            _asyncCleanupCounter++;
            
            // 256 seemed to be a sweet spot in JVM benchmarks
            // It means that at most 255 completed promises are retained per logic that
            // uses invokeWithFeedback callbacks.
            if(_asyncCleanupCounter % 256 == 0)
            {
                void Cleanup()
                {
                    while (true)
                    {
                        var previous = _asyncCallbacksInProgress.Value;
                        if (previous is not null)
                        {
                            var updated = previous.Where(c => !c.Task.IsCompleted).ToImmutableList();
                            if (!_asyncCallbacksInProgress.CompareAndSet(previous, updated)) continue;
                        }

                        break;
                    }
                }
                
                Cleanup();
            }
        }


        /// <summary>
        /// Invoked before any external events are processed, at the startup of the stage.
        /// </summary>
        public virtual void PreStart()
        {
        }

        /// <summary>
        /// Invoked after processing of external events stopped because the stage is about to stop or fail.
        /// </summary>
        public virtual void PostStop()
        {
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// This allows the dynamic creation of an Inlet for a GraphStage which is
        /// connected to a Sink that is available for materialization (e.g. using
        /// the <see cref="SubFusingMaterializer"/>). Care needs to be taken to cancel this Inlet
        /// when the stage shuts down lest the corresponding Sink be left hanging.
        /// </summary>
        [InternalApi]
        protected class SubSinkInlet<T>
        {
            private readonly string _name;
            private IInHandler _handler;
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

                        if (msg is OnNext next)
                        {
                            _elem = (T)next.Element;
                            _pulled = false;
                            _handler!.OnPush();
                        }
                        else if (msg is OnComplete)
                        {
                            _closed = true;
                            _handler!.OnUpstreamFinish();
                        }
                        else if (msg is OnError error)
                        {
                            _closed = true;
                            _handler!.OnUpstreamFailure(error.Cause);
                        }
                    }));
            }

            public IGraph<SinkShape<T>, NotUsed> Sink => _sink;

            /// <summary>
            /// Sets the input handler.
            /// </summary>
            public void SetHandler(IInHandler handler) => _handler = handler;

            /// <summary>
            /// Returns true if there is an element available to be grabbed.
            /// </summary>
            public bool IsAvailable => _elem.HasValue;

            /// <summary>
            /// Returns true if this inlet is closed.
            /// </summary>
            public bool IsClosed => _closed;

            /// <summary>
            /// Returns true if this inlet has been pulled and is not closed.
            /// </summary>
            public bool HasBeenPulled => _pulled && !IsClosed;

            /// <summary>
            /// Grab the most recent element from this inlet.
            /// </summary>
            /// <exception cref="IllegalStateException">
            /// This exception is thrown when this inlet is empty.
            /// </exception>
            /// <returns>The element</returns>
            public T Grab()
            {
                if (!_elem.HasValue)
                    throw new IllegalStateException(
                        $"cannot grab element from port {this} when data has not yet arrived");

                var ret = _elem.Value;
                _elem = Option<T>.None;
                return ret;
            }

            /// <summary>
            /// Pull data from upstream.
            /// </summary>
            /// <exception cref="IllegalStateException">
            /// This exception is thrown when this inlet is closed or already pulled.
            /// </exception>
            public void Pull()
            {
                if (_pulled)
                    throw new IllegalStateException($"cannot pull port {this} twice");
                if (_closed)
                    throw new IllegalStateException($"cannot pull closed port {this}");

                _pulled = true;
                _sink.PullSubstream();
            }

            /// <summary>
            /// Cancel this graph stage using a default reason.
            /// </summary>
            public void Cancel() => Cancel(SubscriptionWithCancelException.NoMoreElementsNeeded.Instance);

            /// <summary>
            /// Cancel this graph stage with a specific reason.
            /// </summary>
            public void Cancel(Exception cause)
            {
                _closed = true;
                _sink.CancelSubstream(cause);
            }

            public override string ToString() => $"SubSinkInlet{_name}";
        }

        protected SubSinkInlet<T> CreateSubSinkInlet<T>(string name) => new(this, name);

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
        [InternalApi]
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
                            _handler!.OnPull();
                        }
                    }
                    else if (command is SubSink.Cancel cancel)
                    {
                        if (!_closed)
                        {
                            _available = false;
                            _closed = true;
                            _handler!.OnDownstreamFinish(SubscriptionWithCancelException.StageWasCompleted.Instance);
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
            /// <param name="d">TBD</param>
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
            /// <param name="elem">The element to be processed</param>
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

            /// <inheritdoc/>
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
        /// <param name="e">TBD</param>
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
        /// <param name="e">TBD</param>
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
        void OnDownstreamFinish(Exception cause);
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
        public virtual void OnDownstreamFinish(Exception cause) =>
            Current.ActiveStage.InternalOnDownstreamFinish(cause);
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
        /// <param name="e">TBD</param>
        public virtual void OnUpstreamFailure(Exception e) => Current.ActiveStage.FailStage(e);

        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        public abstract void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnDownstreamFinish(Exception cause) =>
            Current.ActiveStage.InternalOnDownstreamFinish(cause);
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
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inCount">TBD</param>
        /// <param name="outCount">TBD</param>
        protected InGraphStageLogic(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
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
        /// <param name="e">TBD</param>
        public virtual void OnUpstreamFailure(Exception e) => FailStage(e);
    }

    /// <summary>
    /// A <see cref="GraphStageLogic"/> that implements <see cref="IOutHandler"/>.
    /// <para/>
    /// <see cref="OnDownstreamFinish"/> calls <see cref="GraphStageLogic.CompleteStage"/>
    /// </summary>
    public abstract class OutGraphStageLogic : GraphStageLogic, IOutHandler
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inCount">TBD</param>
        /// <param name="outCount">TBD</param>
        protected OutGraphStageLogic(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
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
        public virtual void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
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
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inCount">TBD</param>
        /// <param name="outCount">TBD</param>
        protected InAndOutGraphStageLogic(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
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
        /// <param name="e">TBD</param>
        public virtual void OnUpstreamFailure(Exception e) => FailStage(e);

        /// <summary>
        /// Called when the output port has received a pull, and therefore ready to emit an element, 
        /// i.e. <see cref="GraphStageLogic.Push{T}"/> is now allowed to be called on this port.
        /// </summary>
        public abstract void OnPull();

        /// <summary>
        /// Called when the output port will no longer accept any new elements. After this callback no other callbacks will be called for this port.
        /// </summary>
        public virtual void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public class StageActorRefNotInitializedException : Exception
    {
        /// <summary>
        /// The singleton instance of this exception
        /// </summary>
        public static readonly StageActorRefNotInitializedException Instance = new();

        private StageActorRefNotInitializedException() : base(
            "You must first call GetStageActorRef(StageActorRef.Receive), to initialize the actor's behavior")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StageActorRefNotInitializedException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected StageActorRefNotInitializedException(SerializationInfo info, StreamingContext context) : base(info,
            context)
        {
        }
    }

    /// <summary>
    /// Input handler that terminates the stage upon receiving completion. The stage fails upon receiving a failure.
    /// </summary>
    public sealed class EagerTerminateInput : InHandler
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly EagerTerminateInput Instance = new();

        private EagerTerminateInput()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPush()
        {
        }
    }

    /// <summary>
    /// Input handler that does not terminate the stage upon receiving completion. The stage fails upon receiving a failure.
    /// </summary>
    public sealed class IgnoreTerminateInput : InHandler
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly IgnoreTerminateInput Instance = new();

        private IgnoreTerminateInput()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPush()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnUpstreamFinish()
        {
        }
    }

    /// <summary>
    /// Input handler that terminates the state upon receiving completion 
    /// if the given condition holds at that time.The stage fails upon receiving a failure.
    /// </summary>
    public class ConditionalTerminateInput : InHandler
    {
        private readonly Func<bool> _predicate;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        public ConditionalTerminateInput(Func<bool> predicate) => _predicate = predicate;

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPush()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
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
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly TotallyIgnorantInput Instance = new();

        private TotallyIgnorantInput()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPush()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnUpstreamFinish()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="e">TBD</param>
        public override void OnUpstreamFailure(Exception e)
        {
        }
    }

    /// <summary>
    /// Output handler that terminates the stage upon cancellation.
    /// </summary>
    public sealed class EagerTerminateOutput : OutHandler
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly EagerTerminateOutput Instance = new();

        private EagerTerminateOutput()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPull()
        {
        }
    }

    /// <summary>
    /// Output handler that does not terminate the stage upon cancellation.
    /// </summary>
    public sealed class IgnoreTerminateOutput : OutHandler
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly IgnoreTerminateOutput Instance = new();

        private IgnoreTerminateOutput()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPull()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnDownstreamFinish(Exception cause)
        {
        }
    }

    /// <summary>
    /// Output handler that terminates the state upon receiving completion if the
    /// given condition holds at that time.The stage fails upon receiving a failure.
    /// </summary>
    public class ConditionalTerminateOutput : OutHandler
    {
        private readonly Func<bool> _predicate;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="predicate">TBD</param>
        public ConditionalTerminateOutput(Func<bool> predicate) => _predicate = predicate;

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnPull()
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnDownstreamFinish(Exception cause)
        {
            if (_predicate())
                Current.ActiveStage.CancelStage(cause);
        }
    }

    public static class StageActorRef
    {
        public delegate void Receive((IActorRef, object) args);
    }

    /// <summary>
    /// Minimal actor to work with other actors and watch them in a synchronous ways.
    /// </summary>
    public sealed class StageActor
    {
        private readonly Action<(IActorRef, object)> _callback;
        private readonly ActorCell _cell;
        private readonly FunctionRef _functionRef;
        private StageActorRef.Receive _behavior;

        public StageActor(
            ActorMaterializer materializer,
            Func<StageActorRef.Receive, Action<(IActorRef, object)>> getAsyncCallback,
            StageActorRef.Receive initialReceive,
            string name = null)
        {
            _callback = getAsyncCallback(InternalReceive);
            _behavior = initialReceive;

            switch (materializer.Supervisor)
            {
                case LocalActorRef r: _cell = r.Cell; break;
                case RepointableActorRef r: _cell = (ActorCell)r.Underlying; break;
                default:
                    throw new IllegalStateException(
                        $"Stream supervisor must be a local actor, was [{materializer.Supervisor.GetType()}]");
            }

            _functionRef = _cell.AddFunctionRef((sender, message) =>
            {
                switch (message)
                {
                    case PoisonPill _:
                    case Kill _:
                        materializer.Logger.Warning(
                            "{0} message sent to StageActor({1}) will be ignored, since it is not a real Actor. " +
                            "Use a custom message type to communicate with it instead.", message, _functionRef.Path);
                        break;
                    default: _callback((sender, message)); break;
                }
            });
        }

        /// <summary>
        /// The <see cref="IActorRef"/> by which this <see cref="StageActor"/> can be contacted from the outside.
        /// This is a full-fledged <see cref="IActorRef"/> that supports watching and being watched
        /// as well as location transparent (remote) communication.
        /// </summary>
        public IActorRef Ref => _functionRef;

        /// <summary>
        /// Special `Become` allowing to swap the behaviour of this <see cref="StageActor"/>.
        /// Unbecome is not available.
        /// </summary>
        public void Become(StageActorRef.Receive receive) => Volatile.Write(ref _behavior, receive);

        /// <summary>
        /// Stops current <see cref="StageActor"/>.
        /// </summary>
        public void Stop() => _cell.RemoveFunctionRef(_functionRef);

        /// <summary>
        /// Makes current <see cref="StageActor"/> watch over given <paramref name="actorRef"/>.
        /// It will be notified when an underlying actor is <see cref="Terminated"/>.
        /// </summary>
        /// <param name="actorRef"></param>
        public void Watch(IActorRef actorRef) => _functionRef.Watch(actorRef);

        /// <summary>
        /// Makes current <see cref="StageActor"/> stop watching previously <see cref="Watch"/>ed <paramref name="actorRef"/>.
        /// If <paramref name="actorRef"/> was not watched over, this method has no result.
        /// </summary>
        /// <param name="actorRef"></param>
        public void Unwatch(IActorRef actorRef) => _functionRef.Unwatch(actorRef);

        internal void InternalReceive((IActorRef, object) pack)
        {
            if (pack.Item2 is Terminated terminated)
            {
                if (_functionRef.IsWatching(terminated.ActorRef))
                {
                    _functionRef.Unwatch(terminated.ActorRef);
                    _behavior(pack);
                }
            }
            else _behavior(pack);
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
    /// <typeparam name="T">TBD</typeparam>
    internal class GraphStageLogicWithCallbackWrapper<T> : GraphStageLogic
    {
        private interface ICallbackState
        {
        }

        /// <summary>
        /// Args queued before <see cref="InitCallback"/> was called. Each entry carries the
        /// arg itself plus the producer thread's <see cref="ActivityContext"/> captured at
        /// <see cref="InvokeCallbacks"/> time. This is the only place we can observe
        /// <see cref="Activity.Current"/> from the external caller, because by the time
        /// <see cref="InitCallback"/> drains the queue we are on the interpreter thread.
        /// </summary>
        private sealed class QueuedArg
        {
            public T Arg { get; }
            public ActivityContext? IngressContext { get; }
            public QueuedArg(T arg, ActivityContext? ingressContext)
            {
                Arg = arg;
                IngressContext = ingressContext;
            }
        }

        private sealed class NotInitialized : ICallbackState
        {
            public IList<QueuedArg> Args { get; }

            public NotInitialized(IList<QueuedArg> args) => Args = args;
        }

        private sealed class Initialized : ICallbackState
        {
            public Action<T> Callback { get; }

            public Initialized(Action<T> callback) => Callback = callback;
        }

        private sealed class Stopped : ICallbackState
        {
            public Action<T> Callback { get; }

            public Stopped(Action<T> callback) => Callback = callback;
        }

        private readonly AtomicReference<ICallbackState> _callbackState = new(new NotInitialized(new List<QueuedArg>()));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="inCount">TBD</param>
        /// <param name="outCount">TBD</param>
        public GraphStageLogicWithCallbackWrapper(int inCount, int outCount) : base(inCount, outCount)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="shape">TBD</param>
        public GraphStageLogicWithCallbackWrapper(Shape shape) : base(shape)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="callback">TBD</param>
        protected void StopCallback(Action<T> callback) => Locked(() => _callbackState.Value = new Stopped(callback));

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="callback">TBD</param>
        protected void InitCallback(Action<T> callback) => Locked(() =>
        {
            var state = _callbackState.GetAndSet(new Initialized(callback));
            if (state is NotInitialized notInitialized)
            {
                // Drain any args that were queued before the interpreter started, restoring
                // the producer thread's trace context around each callback invocation so
                // downstream stages see Activity.Current as it was at the original caller's side.
                foreach (var queued in notInitialized.Args)
                {
                    if (queued.IngressContext.HasValue)
                    {
                        using var restored = StreamsDiagnostics.ActivitySource.StartActivity(
                            StreamsDiagnostics.OperationIngressQueued,
                            ActivityKind.Internal,
                            queued.IngressContext.Value);
                        callback(queued.Arg);
                    }
                    else
                    {
                        callback(queued.Arg);
                    }
                }
            }
        });

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="arg">TBD</param>
        protected void InvokeCallbacks(T arg) => Locked(() =>
        {
            var state = _callbackState.Value;
            if (state is Initialized initialized)
            {
                initialized.Callback(arg);
            }
            else if (state is NotInitialized notInitialized)
            {
                // Only capture the producer thread's trace context when we're actually going
                // to queue the arg — Initialized and Stopped paths don't need it, so there's
                // no reason to touch Activity.Current on those hot paths.
                var ingressContext = StreamsDiagnostics.ActivitySource.HasListeners()
                    ? Activity.Current?.Context
                    : null;
                notInitialized.Args.Add(new QueuedArg(arg, ingressContext));
            }
            else if (state is Stopped stopped)
            {
                stopped.Callback(arg);
            }
        });

        private void Locked(Action body)
        {
            lock (this)
            {
                body();
            }
        }
    }
}