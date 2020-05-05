//-----------------------------------------------------------------------
// <copyright file="PersistentFSMBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Routing;
using Akka.Util.Internal;
using static Akka.Persistence.Fsm.PersistentFSM;

namespace Akka.Persistence.Fsm
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="TState">TBD</typeparam>
    /// <typeparam name="TData">TBD</typeparam>
    /// <typeparam name="TEvent">TBD</typeparam>
    public abstract class PersistentFSMBase<TState, TData, TEvent> : PersistentActor, IListeners
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// Initializes a new instance of the <see cref="PersistentFSMBase{TState, TData, TEvent}"/> class.
        /// </summary>
        protected PersistentFSMBase()
        {
            if (this is ILoggingFSM)
                _debugEvent = Context.System.Settings.FsmDebugEvent;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="fsmEvent">TBD</param>
        /// <param name="state">TBD</param>
        /// <returns>TBD</returns>
        public delegate State<TState, TData, TEvent> StateFunction(FSMBase.Event<TData> fsmEvent, State<TState, TData, TEvent> state = null);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="initialState">TBD</param>
        /// <param name="nextState">TBD</param>
        public delegate void TransitionHandler(TState initialState, TState nextState);

        // **
        // FSM DSL
        // **

        /// <summary>
        /// Insert a new <see cref="StateFunction" /> at the end of the processing chain for the
        /// given state. If the stateTimeout parameter is set, entering this state without a
        /// differing explicit timeout setting will trigger a <see cref="FSMBase.StateTimeout" />.
        /// </summary>
        /// <param name="stateName">designator for the state</param>
        /// <param name="func">delegate describing this state's response to input</param>
        /// <param name="timeout">default timeout for this state</param>
        public void When(TState stateName, StateFunction func, TimeSpan? timeout = null)
        {
            Register(stateName, func, timeout);
        }

        /// <summary>
        /// Sets the initial state for this FSM. Call this method from the constructor before the <see cref="Initialize" /> method.
        /// If different state is needed after a restart this method, followed by <see cref="Initialize" />, can be used in the actor
        /// life cycle hooks <see cref="ActorBase.PreStart()" /> and <see cref="ActorBase.PostRestart" />.
        /// </summary>
        /// <param name="stateName">Initial state designator.</param>
        /// <param name="stateData">Initial state data.</param>
        /// <param name="timeout">State timeout for the initial state, overriding the default timeout for that state.</param>
        public void StartWith(TState stateName, TData stateData, TimeSpan? timeout = null)
        {
            _currentState = new State<TState, TData, TEvent>(stateName, stateData, timeout);
        }

        /// <summary>
        /// Produce transition to other state. Return this from a state function
        /// in order to effect the transition.
        /// </summary>
        /// <param name="nextStateName">State designator for the next state</param>
        /// <returns>State transition descriptor</returns>
        public State<TState, TData, TEvent> GoTo(TState nextStateName)
        {
            return new State<TState, TData, TEvent>(nextStateName, _currentState.StateData);
        }

        /// <summary>
        /// Produce "empty" transition descriptor. Return this from a state function
        /// when no state change is to be effected.
        /// </summary>
        /// <returns>Descriptor for staying in the current state.</returns>
        public State<TState, TData, TEvent> Stay()
        {
            return GoTo(_currentState.StateName).WithNotification(false);
        }

        /// <summary>
        /// Produce change descriptor to stop this FSM actor with <see cref="FSMBase.Reason" /> <see cref="FSMBase.Normal" />
        /// </summary>
        /// <returns>TBD</returns>
        public State<TState, TData, TEvent> Stop()
        {
            return Stop(FSMBase.Normal.Instance);
        }

        /// <summary>
        /// Produce change descriptor to stop this FSM actor with the specified <see cref="FSMBase.Reason" />.
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <returns>TBD</returns>
        public State<TState, TData, TEvent> Stop(FSMBase.Reason reason)
        {
            return Stop(reason, _currentState.StateData);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        /// <param name="stateData">TBD</param>
        /// <returns>TBD</returns>
        public State<TState, TData, TEvent> Stop(FSMBase.Reason reason, TData stateData)
        {
            return Stay().Copy(stateData: stateData, stopReason: reason);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class TransformHelper
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="TransformHelper"/> class.
            /// </summary>
            /// <param name="func">TBD</param>
            public TransformHelper(StateFunction func)
            {
                Func = func;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public StateFunction Func { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="andThen">TBD</param>
            /// <returns>TBD</returns>
            public StateFunction Using(Func<State<TState, TData, TEvent>, State<TState, TData, TEvent>> andThen)
            {
                StateFunction continuedDelegate = (@event, state) => andThen.Invoke(Func.Invoke(@event, state));
                return continuedDelegate;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="func">TBD</param>
        /// <returns>TBD</returns>
        public TransformHelper Transform(StateFunction func) => new TransformHelper(func);

        /// <summary>
        /// Schedule named timer to deliver message after given delay, possibly repeating.
        /// Any existing timer with the same name will automatically be canceled before adding
        /// the new timer.
        /// </summary>
        /// <param name="name">identifier to be used with <see cref="CancelTimer" />.</param>
        /// <param name="msg">message to be delivered</param>
        /// <param name="timeout">delay of first message delivery and between subsequent messages.</param>
        /// <param name="repeat">send once if false, scheduleAtFixedRate if true</param>
        public void SetTimer(string name, object msg, TimeSpan timeout, bool repeat = false)
        {
            if (_debugEvent)
            {
                _log.Debug($"setting {(repeat ? "repeating" : "")} timer {name}/{timeout}: {msg}");
            }
            if (_timers.TryGetValue(name, out FSMBase.Timer oldTimer))
            {
                oldTimer.Cancel();
            }

            var timer = new FSMBase.Timer(name, msg, repeat, _timerGen.Next(), this, Context);
            timer.Schedule(Self, timeout);
            _timers[name] = timer;
        }

        /// <summary>
        /// Cancel a named <see cref="FSMBase.Timer" />, ensuring that the message is not subsequently delivered (no race.)
        /// </summary>
        /// <param name="name">The name of the timer to cancel.</param>
        public void CancelTimer(string name)
        {
            if (_debugEvent)
            {
                _log.Debug($"Cancelling timer {name}");
            }

            if (_timers.ContainsKey(name))
            {
                _timers[name].Cancel();
                _timers.Remove(name);
            }
        }

        /// <summary>
        /// Determines whether the named timer is still active. Returns true
        /// unless the timer does not exist, has previously been cancelled, or
        /// if it was a single-shot timer whose message was already received.
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public bool IsTimerActive(string name)
        {
            return _timers.ContainsKey(name);
        }

        /// <summary>
        /// Set the state timeout explicitly. This method can be safely used from
        /// within a state handler.
        /// </summary>
        /// <param name="state">TBD</param>
        /// <param name="timeout">TBD</param>
        public void SetStateTimeout(TState state, TimeSpan? timeout) => _stateTimeouts[state] = timeout;

        /// <summary>
        /// INTERNAL API, used for testing.
        /// </summary>
        internal bool IsStateTimerActive => _timeoutFuture != null;

        /// <summary>
        /// Set handler which is called upon each state transition
        /// </summary>
        /// <param name="transitionHandler">TBD</param>
        public void OnTransition(TransitionHandler transitionHandler)
        {
            _transitionEvent.Add(transitionHandler);
        }

        /// <summary>
        /// Set the handler which is called upon termination of this FSM actor. Calling this
        /// method again will overwrite the previous contents.
        /// </summary>
        /// <param name="terminationHandler">TBD</param>
        public void OnTermination(Action<FSMBase.StopEvent<TState, TData>> terminationHandler)
        {
            _terminateEvent = terminationHandler;
        }

        /// <summary>
        /// Set handler which is called upon reception of unhandled FSM messages. Calling
        /// this method again will overwrite the previous contents.
        /// </summary>
        /// <param name="stateFunction">TBD</param>
        public void WhenUnhandled(StateFunction stateFunction)
        {
            HandleEvent = OrElse(stateFunction, HandleEventDefault);
        }

        /// <summary>
        /// Verify the existence of initial state and setup timers. This should be the
        /// last call within the constructor or <see cref="ActorBase.PreStart"/> and
        /// <see cref="ActorBase.PostRestart"/>.
        /// </summary>
        internal void Initialize()
        {
            if (_currentState != null)
                MakeTransition(_currentState);
            else
                throw new IllegalStateException("You must call StartWith before calling Initialize.");
        }

        /// <summary>
        /// Current state name
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown if this property is accessed before <see cref="StartWith"/> was called.
        /// </exception>
        public TState StateName
        {
            get
            {
                if (_currentState != null)
                    return _currentState.StateName;
                throw new IllegalStateException("You must call StartWith before calling StateName.");
            }
        }

        /// <summary>
        /// Current state data
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown if this property is accessed before <see cref="StartWith"/> was called.
        /// </exception>
        public TData StateData
        {
            get
            {
                if (_currentState != null)
                    return _currentState.StateData;
                throw new IllegalStateException("You must call StartWith before calling StateData.");
            }
        }

        /// <summary>
        /// Return all defined state names
        /// </summary>
        protected IEnumerable<TState> StateNames => _stateFunctions.Keys;

        /// <summary>
        /// Return next state data (available in <see cref="OnTransition"/> handlers)
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if this property is accessed outside of an <see cref="OnTransition"/> handler.
        /// </exception>
        public TData NextStateData
        {
            get
            {
                if (_nextState != null)
                    return _nextState.StateData;
                throw new IllegalStateException("NextStateData is only available during OnTransition");
            }
        }

        // **
        // PRIVATE IMPLEMENTATION DETAILS
        // **

        // FSM Logging implementation
        private readonly bool _debugEvent;

        private string GetSourceString(object source)
        {
            if (source is string s)
                return s;

            if (source is FSMBase.Timer timer)
                return "timer '" + timer.Name + "'";

            if (source is IActorRef actorRef)
                return actorRef.ToString();

            return "unknown";
        }

        // FSM State data and current timeout handling
        private State<TState, TData, TEvent> _currentState;
        private ICancelable _timeoutFuture;
        private State<TState, TData, TEvent> _nextState;
        private long _generation;

        // Timer handling
        private readonly IDictionary<string, FSMBase.Timer> _timers = new Dictionary<string, FSMBase.Timer>();
        private readonly AtomicCounter _timerGen = new AtomicCounter(0);

        // State definitions
        private readonly Dictionary<TState, StateFunction> _stateFunctions = new Dictionary<TState, StateFunction>();
        private readonly Dictionary<TState, TimeSpan?> _stateTimeouts = new Dictionary<TState, TimeSpan?>();

        private void Register(TState name, StateFunction function, TimeSpan? timeout)
        {
            if (_stateFunctions.TryGetValue(name, out StateFunction oldFunction))
            {
                _stateFunctions[name] = OrElse(oldFunction, function);
                _stateTimeouts[name] = _stateTimeouts[name] ?? timeout;
            }
            else
            {
                _stateFunctions.Add(name, function);
                _stateTimeouts.Add(name, timeout);
            }
        }

        // Unhandled event handler
        private StateFunction HandleEventDefault
        {
            get
            {
                return (@event, state) =>
                {
                    _log.Warning("unhandled event {0} in state {1}", @event.FsmEvent, StateName);
                    return Stay();
                };
            }
        }

        private StateFunction _handleEvent;

        private StateFunction HandleEvent
        {
            get { return _handleEvent ?? (_handleEvent = HandleEventDefault); }
            set { _handleEvent = value; }
        }

        // Termination handling
        private Action<FSMBase.StopEvent<TState, TData>> _terminateEvent = @event => { };

        // Transition handling
        private readonly IList<TransitionHandler> _transitionEvent = new List<TransitionHandler>();

        private void HandleTransition(TState previous, TState next)
        {
            foreach (var tran in _transitionEvent)
            {
                tran.Invoke(previous, next);
            }
        }

        /// <summary>
        /// Listener support
        /// </summary>
        public ListenerSupport Listeners { get; } = new ListenerSupport();

        // **
        // Main actor Receive method
        // **

        /// <inheritdoc/>
        protected override bool ReceiveCommand(object message)
        {
            if (message is FSMBase.TimeoutMarker timeoutMarker)
            {
                if (_generation == timeoutMarker.Generation)
                {
                    ProcessMsg(FSMBase.StateTimeout.Instance, "state timeout");
                }
                return true;
            }

            if (message is FSMBase.Timer t)
            {
                if (ReferenceEquals(t.Owner, this) && _timers.TryGetValue(t.Name, out var timer) && timer.Generation == t.Generation)
                {
                    if (_timeoutFuture != null)
                    {
                        _timeoutFuture.Cancel(false);
                        _timeoutFuture = null;
                    }
                    _generation++;
                    if (!timer.Repeat)
                    {
                        _timers.Remove(timer.Name);
                    }
                    ProcessMsg(timer.Message, timer);
                }
                return true;
            }

            if (message is FSMBase.SubscribeTransitionCallBack subscribeTransitionCallBack)
            {
                Context.Watch(subscribeTransitionCallBack.ActorRef);
                Listeners.Add(subscribeTransitionCallBack.ActorRef);
                //send the current state back as a reference point
                subscribeTransitionCallBack.ActorRef.Tell(new FSMBase.CurrentState<TState>(Self, _currentState.StateName));
                return true;
            }

            if (message is Listen listen)
            {
                Context.Watch(listen.Listener);
                Listeners.Add(listen.Listener);
                listen.Listener.Tell(new FSMBase.CurrentState<TState>(Self, _currentState.StateName));
                return true;
            }

            if (message is FSMBase.UnsubscribeTransitionCallBack unsubscribeTransitionCallBack)
            {
                Context.Unwatch(unsubscribeTransitionCallBack.ActorRef);
                Listeners.Remove(unsubscribeTransitionCallBack.ActorRef);
                return true;
            }

            if (message is Deafen deafen)
            {
                Context.Unwatch(deafen.Listener);
                Listeners.Remove(deafen.Listener);
                return true;
            }

            if (_timeoutFuture != null)
            {
                _timeoutFuture.Cancel(false);
                _timeoutFuture = null;
            }
            _generation++;
            ProcessMsg(message, Sender);
            return true;
        }

        private void ProcessMsg(object any, object source)
        {
            var fsmEvent = new FSMBase.Event<TData>(any, _currentState.StateData);
            ProcessEvent(fsmEvent, source);
        }

        private void ProcessEvent(FSMBase.Event<TData> fsmEvent, object source)
        {
            if (_debugEvent)
            {
                var srcStr = GetSourceString(source);
                _log.Debug("processing {0} from {1} in state {2}", fsmEvent, srcStr, StateName);
            }

            var stateFunc = _stateFunctions[_currentState.StateName];
            var oldState = _currentState;

            State<TState, TData, TEvent> upcomingState = null;

            if (stateFunc != null)
            {
                upcomingState = stateFunc(fsmEvent);
            }

            if (upcomingState == null)
            {
                upcomingState = HandleEvent(fsmEvent);
            }

            ApplyState(upcomingState);

            if (_debugEvent && !Equals(oldState, upcomingState))
            {
                _log.Debug("transition {0} -> {1}", oldState, upcomingState);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="nextState">TBD</param>
        protected virtual void ApplyState(State<TState, TData, TEvent> nextState)
        {
            if (nextState.StopReason == null)
            {
                MakeTransition(nextState);
            }
            else
            {
                for (int i = nextState.Replies.Count - 1; i >= 0; i--)
                {
                    Sender.Tell(nextState.Replies[i]);
                }
                Terminate(nextState);
                Context.Stop(Self);
            }
        }

        private void MakeTransition(State<TState, TData, TEvent> nextState)
        {
            if (!_stateFunctions.ContainsKey(nextState.StateName))
            {
                Terminate(Stay().WithStopReason(new FSMBase.Failure($"Next state {nextState.StateName} does not exist")));
            }
            else
            {
                for (int i = nextState.Replies.Count - 1; i >= 0; i--)
                {
                    Sender.Tell(nextState.Replies[i]);
                }
                if (!_currentState.StateName.Equals(nextState.StateName) || nextState.Notifies)
                {
                    _nextState = nextState;
                    HandleTransition(_currentState.StateName, _nextState.StateName);
                    Listeners.Gossip(new FSMBase.Transition<TState>(Self, _currentState.StateName, _nextState.StateName));
                    _nextState = null;
                }
                _currentState = nextState;

                var timeout = _currentState.Timeout ?? _stateTimeouts[_currentState.StateName];
                if (timeout.HasValue)
                {
                    var t = timeout.Value;
                    if (t < TimeSpan.MaxValue)
                    {
                        _timeoutFuture = Context.System.Scheduler.ScheduleTellOnceCancelable(t, Context.Self, new FSMBase.TimeoutMarker(_generation), Context.Self);
                    }
                }
            }
        }

        /// <summary>
        /// Call the <see cref="OnTermination"/> hook if you want to retain this behavior.
        /// When overriding make sure to call base.PostStop();
        ///
        /// Please note that this method is called by default from <see cref="ActorBase.PreRestart"/> so
        /// override that one if <see cref="OnTermination"/> shall not be called during restart.
        /// </summary>
        protected override void PostStop()
        {
            /*
             * Setting this instance's state to Terminated does no harm during restart, since
             * the new instance will initialize fresh using StartWith.
             */
            Terminate(Stay().WithStopReason(FSMBase.Shutdown.Instance));
            base.PostStop();
        }

        private void Terminate(State<TState, TData, TEvent> upcomingState)
        {
            if (_currentState.StopReason == null)
            {
                var reason = upcomingState.StopReason;
                LogTermination(reason);
                foreach (var t in _timers)
                {
                    t.Value.Cancel();
                }
                _timers.Clear();
                _timeoutFuture?.Cancel();
                _currentState = upcomingState;

                var stopEvent = new FSMBase.StopEvent<TState, TData>(reason, _currentState.StateName, _currentState.StateData);
                _terminateEvent(stopEvent);
            }
        }

        /// <summary>
        /// By default, <see cref="Failure" /> is logged at error level and other
        /// reason types are not logged. It is possible to override this behavior.
        /// </summary>
        /// <param name="reason">TBD</param>
        protected virtual void LogTermination(FSMBase.Reason reason)
        {
            if (reason is FSMBase.Failure failure)
            {
                if (failure.Cause is Exception)
                {
                    _log.Error(failure.Cause.AsInstanceOf<Exception>(), "terminating due to Failure");
                }
                else
                {
                    _log.Error(failure.Cause.ToString());
                }
            }
        }

        /// <summary>
        /// C# port of Scala's orElse method for partial function chaining.
        /// See http://scalachina.com/api/scala/PartialFunction.html
        /// </summary>
        /// <param name="original">The original <see cref="StateFunction" /> to be called</param>
        /// <param name="fallback">The <see cref="StateFunction" /> to be called if <paramref name="original" /> returns null</param>
        /// <returns>
        /// A <see cref="StateFunction" /> which combines both the results of <paramref name="original" /> and
        /// <paramref name="fallback" />
        /// </returns>
        private static StateFunction OrElse(StateFunction original, StateFunction fallback)
        {
            StateFunction chained = (@event, state) =>
            {
                var originalResult = original.Invoke(@event, state);
                if (originalResult == null) return fallback.Invoke(@event, state);
                return originalResult;
            };

            return chained;
        }
    }
}
