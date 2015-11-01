//-----------------------------------------------------------------------
// <copyright file="FSM.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.Persistence.Serialization;
using Akka.Routing;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Persistence.Fsm
{
    public abstract class PersistentFSMBase<TState, TData, TEvent> : PersistentActor, IListeners
    {
        public delegate State<TState, TData, TEvent> StateFunction(
            FSMBase.Event<TData> fsmEvent, State<TState, TData, TEvent> state = null);

        public delegate void TransitionHandler(TState initialState, TState nextState);

        protected readonly ListenerSupport _listener = new ListenerSupport();
        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        ///     State definitions
        /// </summary>
        private readonly Dictionary<TState, StateFunction> _stateFunctions = new Dictionary<TState, StateFunction>();

        private readonly Dictionary<TState, TimeSpan?> _stateTimeouts = new Dictionary<TState, TimeSpan?>();

        private readonly AtomicCounter _timerGen = new AtomicCounter(0);

        /// <summary>
        ///     Timer handling
        /// </summary>
        protected readonly IDictionary<string, Timer> _timers = new Dictionary<string, Timer>();

        /// <summary>
        ///     Transition handling
        /// </summary>
        private readonly IList<TransitionHandler> _transitionEvent = new List<TransitionHandler>();

        /// <summary>
        ///     FSM state data and current timeout handling
        /// </summary>
        /// a
        protected State<TState, TData, TEvent> _currentState;

        protected long _generation;
        private StateFunction _handleEvent;
        private State<TState, TData, TEvent> _nextState;

        /// <summary>
        ///     Termination handling
        /// </summary>
        private Action<FSMBase.StopEvent<TState, TData>> _terminateEvent = @event => { };

        protected ICancelable _timeoutFuture;

        /// <summary>
        ///     Can be set to enable debugging on certain actions taken by the FSM
        /// </summary>
        protected bool DebugEvent;

        protected PersistentFSMBase()
        {
            if (this is ILoggingFSM)
                DebugEvent = Context.System.Settings.FsmDebugEvent;
        }

        /// <summary>
        ///     Current state name
        /// </summary>
        public TState StateName
        {
            get { return _currentState.StateName; }
        }

        /// <summary>
        ///     Current state data
        /// </summary>
        public TData StateData
        {
            get { return _currentState.StateData; }
        }

        /// <summary>
        ///     Return next state data (available in <see cref="OnTransition" /> handlers)
        /// </summary>
        public TData NextStateData
        {
            get
            {
                if (_nextState == null)
                    throw new InvalidOperationException("NextStateData is only available during OnTransition");
                return _nextState.StateData;
            }
        }

        /// <summary>
        ///     Unhandled event handler
        /// </summary>
        private StateFunction HandleEventDefault
        {
            get
            {
                return delegate(FSMBase.Event<TData> @event, State<TState, TData, TEvent> state)
                {
                    _log.Warning("unhandled event {0} in state {1}", @event.FsmEvent, StateName);
                    return Stay();
                };
            }
        }

        private StateFunction HandleEvent
        {
            get { return _handleEvent ?? (_handleEvent = HandleEventDefault); }
            set { _handleEvent = value; }
        }

        public bool IsStateTimerActive { get; }

        public ListenerSupport Listeners
        {
            get { return _listener; }
        }

        /// <summary>
        ///     Insert a new <see cref="StateFunction" /> at the end of the processing chain for the
        ///     given state. If the stateTimeout parameter is set, entering this state without a
        ///     differing explicit timeout setting will trigger a <see cref="FSMBase.StateTimeout" />.
        /// </summary>
        /// <param name="stateName">designator for the state</param>
        /// <param name="func">delegate describing this state's response to input</param>
        /// <param name="timeout">default timeout for this state</param>
        public void When(TState stateName, StateFunction func, TimeSpan? timeout = null)
        {
            Register(stateName, func, timeout);
        }

        /// <summary>
        ///     Sets the initial state for this FSM. Call this method from the constructor before the <see cref="Initialize" />
        ///     method.
        ///     If different state is needed after a restart this method, followed by <see cref="Initialize" />, can be used in the
        ///     actor
        ///     life cycle hooks <see cref="ActorBase.PreStart()" /> and <see cref="ActorBase.PostRestart" />.
        /// </summary>
        /// <param name="stateName">Initial state designator.</param>
        /// <param name="stateData">Initial state data.</param>
        /// <param name="timeout">State timeout for the initial state, overriding the default timeout for that state.</param>
        public void StartWith(TState stateName, TData stateData, TimeSpan? timeout = null)
        {
            _currentState = new State<TState, TData, TEvent>(stateName, stateData, timeout);
        }

        /// <summary>
        ///     Produce transition to other state. Return this from a state function
        ///     in order to effect the transition.
        /// </summary>
        /// <param name="nextStateName">State designator for the next state</param>
        /// <returns>State transition descriptor</returns>
        public State<TState, TData, TEvent> GoTo(TState nextStateName)
        {
            return new State<TState, TData, TEvent>(nextStateName, _currentState.StateData);
        }

        /// <summary>
        ///     Produce transition to other state. Return this from a state function
        ///     in order to effect the transition.
        /// </summary>
        /// <param name="nextStateName">State designator for the next state</param>
        /// <param name="stateData">Data for next state</param>
        /// <returns>State transition descriptor</returns>
        public State<TState, TData, TEvent> GoTo(TState nextStateName, TData stateData)
        {
            return new State<TState, TData, TEvent>(nextStateName, stateData);
        }

        /// <summary>
        ///     Produce "empty" transition descriptor. Return this from a state function
        ///     when no state change is to be effected.
        /// </summary>
        /// <returns>Descriptor for staying in the current state.</returns>
        public State<TState, TData, TEvent> Stay()
        {
            return GoTo(_currentState.StateName);
        }

        /// <summary>
        ///     Produce change descriptor to stop this FSM actor with <see cref="FSMBase.Reason" /> <see cref="FSMBase.Normal" />
        /// </summary>
        public State<TState, TData, TEvent> Stop()
        {
            return Stop(new FSMBase.Normal());
        }

        /// <summary>
        ///     Produce change descriptor to stop this FSM actor with the specified <see cref="FSMBase.Reason" />.
        /// </summary>
        public State<TState, TData, TEvent> Stop(FSMBase.Reason reason)
        {
            return Stop(reason, _currentState.StateData);
        }

        public State<TState, TData, TEvent> Stop(FSMBase.Reason reason, TData stateData)
        {
            return Stay().Using(stateData).WithStopReason(reason);
        }

        /// <summary>
        ///     Schedule named timer to deliver message after given delay, possibly repeating.
        ///     Any existing timer with the same name will automatically be canceled before adding
        ///     the new timer.
        /// </summary>
        /// <param name="name">identifier to be used with <see cref="CancelTimer" />.</param>
        /// <param name="msg">message to be delivered</param>
        /// <param name="timeout">delay of first message delivery and between subsequent messages.</param>
        /// <param name="repeat">send once if false, scheduleAtFixedRate if true</param>
        public void SetTimer(string name, object msg, TimeSpan timeout, bool repeat = false)
        {
            if (DebugEvent)
                _log.Debug("setting " + (repeat ? "repeating" : "") + "timer '{0}' / {1}: {2}", name, timeout, msg);
            if (_timers.ContainsKey(name))
                _timers[name].Cancel();
            var timer = new Timer(name, msg, repeat, _timerGen.Next(), Context, DebugEvent ? _log : null);
            timer.Schedule(Self, timeout);

            if (!_timers.ContainsKey(name))
                _timers.Add(name, timer);
            else
                _timers[name] = timer;
        }

        /// <summary>
        ///     Cancel a named <see cref="System.Threading.Timer" />, ensuring that the message is not subsequently delivered (no
        ///     race.)
        /// </summary>
        /// <param name="name">The name of the timer to cancel.</param>
        public void CancelTimer(string name)
        {
            if (DebugEvent)
            {
                _log.Debug("Cancelling timer {0}", name);
            }

            if (_timers.ContainsKey(name))
            {
                _timers[name].Cancel();
                _timers.Remove(name);
            }
        }

        /// <summary>
        ///     Determines whether the named timer is still active. Returns true
        ///     unless the timer does not exist, has previously been cancelled, or
        ///     if it was a single-shot timer whose message was already received.
        /// </summary>
        public bool IsTimerActive(string name)
        {
            return _timers.ContainsKey(name);
        }

        /// <summary>
        ///     Set the state timeout explicitly. This method can be safely used from
        ///     within a state handler.
        /// </summary>
        public void SetStateTimeout(TState state, TimeSpan? timeout)
        {
            if (!_stateTimeouts.ContainsKey(state))
                _stateTimeouts.Add(state, timeout);
            else
                _stateTimeouts[state] = timeout;
        }

        /// <summary>
        ///     Set handler which is called upon each state transition, i.e. not when
        ///     staying in the same state.
        /// </summary>
        public void OnTransition(TransitionHandler transitionHandler)
        {
            _transitionEvent.Add(transitionHandler);
        }

        /// <summary>
        ///     Set the handler which is called upon termination of this FSM actor. Calling this
        ///     method again will overwrite the previous contents.
        /// </summary>
        public void OnTermination(Action<FSMBase.StopEvent<TState, TData>> terminationHandler)
        {
            _terminateEvent = terminationHandler;
        }

        /// <summary>
        ///     Set handler which is called upon reception of unhandled FSM messages. Calling
        ///     this method again will overwrite the previous contents.
        /// </summary>
        /// <param name="stateFunction"></param>
        public void WhenUnhandled(StateFunction stateFunction)
        {
            HandleEvent = OrElse(stateFunction, HandleEventDefault);
        }

        /// <summary>
        ///     Verify the existence of initial state and setup timers. This should be the
        ///     last call within the constructor or <see cref="ActorBase.PreStart" /> and
        ///     <see cref="ActorBase.PostRestart" />.
        /// </summary>
        public void Initialize()
        {
            MakeTransition(_currentState);
        }

        public TransformHelper Transform(StateFunction func)
        {
            return new TransformHelper(func);
        }

        private void Register(TState name, StateFunction function, TimeSpan? timeout)
        {
            if (_stateFunctions.ContainsKey(name))
            {
                _stateFunctions[name] = OrElse(_stateFunctions[name], function);
                _stateTimeouts[name] = _stateTimeouts[name] ?? timeout;
            }
            else
            {
                _stateFunctions.Add(name, function);
                _stateTimeouts.Add(name, timeout);
            }
        }

        private void HandleTransition(TState previous, TState next)
        {
            foreach (var tran in _transitionEvent)
            {
                tran.Invoke(previous, next);
            }
        }

        /// <summary>
        ///     C# port of Scala's orElse method for partial function chaining.
        ///     See http://scalachina.com/api/scala/PartialFunction.html
        /// </summary>
        /// <param name="original">The original <see cref="StateFunction" /> to be called</param>
        /// <param name="fallback">The <see cref="StateFunction" /> to be called if <paramref name="original" /> returns null</param>
        /// <returns>
        ///     A <see cref="StateFunction" /> which combines both the results of <paramref name="original" /> and
        ///     <paramref name="fallback" />
        /// </returns>
        private static StateFunction OrElse(StateFunction original, StateFunction fallback)
        {
            StateFunction chained = delegate(FSMBase.Event<TData> @event, State<TState, TData, TEvent> state)
            {
                var originalResult = original.Invoke(@event, state);
                if (originalResult == null) return fallback.Invoke(@event, state);
                return originalResult;
            };

            return chained;
        }

        protected void ProcessMsg(object any, object source)
        {
            var fsmEvent = new FSMBase.Event<TData>(any, _currentState.StateData);
            ProcessEvent(fsmEvent, source);
        }

        private void ProcessEvent(FSMBase.Event<TData> fsmEvent, object source)
        {
            if (DebugEvent)
            {
                var srcStr = GetSourceString(source);
                _log.Debug("processing {0} from {1}", fsmEvent, srcStr);
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
            if (DebugEvent && !Equals(oldState, upcomingState))
            {
                _log.Debug("transition {0} -> {1}", oldState, upcomingState);
            }
        }

        private string GetSourceString(object source)
        {
            var s = source as string;
            if (s != null) return s;
            var timer = source as Timer;
            if (timer != null) return "timer '" + timer.Name + "'";
            var actorRef = source as IActorRef;
            if (actorRef != null) return actorRef.ToString();
            return "unknown";
        }


        protected virtual void ApplyState(State<TState, TData, TEvent> upcomingState)
        {
            if (upcomingState.StopReason == null)
            {
                MakeTransition(upcomingState);
                return;
            }
            var replies = upcomingState.Replies;
            replies.Reverse();
            foreach (var reply in replies)
            {
                Sender.Tell(reply);
            }
            Terminate(upcomingState);
            Context.Stop(Self);
        }

        private void MakeTransition(State<TState, TData, TEvent> upcomingState)
        {
            if (!_stateFunctions.ContainsKey(upcomingState.StateName))
            {
                Terminate(
                    Stay()
                        .WithStopReason(
                            new FSMBase.Failure(string.Format("Next state {0} does not exist", upcomingState.StateName))));
            }
            else
            {
                var replies = upcomingState.Replies;
                replies.Reverse();
                foreach (var r in replies)
                {
                    Sender.Tell(r);
                }
                if (!_currentState.StateName.Equals(upcomingState.StateName))
                {
                    _nextState = upcomingState;
                    HandleTransition(_currentState.StateName, _nextState.StateName);
                    Listeners.Gossip(new FSMBase.Transition<TState>(Self, _currentState.StateName, _nextState.StateName));
                    _nextState = null;
                }
                _currentState = upcomingState;
                var timeout = _currentState.Timeout ?? _stateTimeouts[_currentState.StateName];
                if (timeout.HasValue)
                {
                    var t = timeout.Value;
                    if (t < TimeSpan.MaxValue)
                    {
                        _timeoutFuture = Context.System.Scheduler.ScheduleTellOnceCancelable(t, Context.Self,
                            new TimeoutMarker(_generation), Context.Self);
                    }
                }
            }
        }

        protected override bool ReceiveCommand(object message)
        {
            var match = message.Match()
                .With<TimeoutMarker>(marker =>
                {
                    if (_generation == marker.Generation)
                    {
                        ProcessMsg(new StateTimeout(), "state timeout");
                    }
                })
                .With<Timer>(t =>
                {
                    if (_timers.ContainsKey(t.Name) && _timers[t.Name].Generation == t.Generation)
                    {
                        if (_timeoutFuture != null)
                        {
                            _timeoutFuture.Cancel(false);
                            _timeoutFuture = null;
                        }
                        _generation++;
                        if (!t.Repeat)
                        {
                            _timers.Remove(t.Name);
                        }
                        ProcessMsg(t.Message, t);
                    }
                })
                .With<FSMBase.SubscribeTransitionCallBack>(cb =>
                {
                    Context.Watch(cb.ActorRef);
                    Listeners.Add(cb.ActorRef);
                    //send the current state back as a reference point
                    cb.ActorRef.Tell(new FSMBase.CurrentState<TState>(Self, _currentState.StateName));
                })
                .With<Listen>(l =>
                {
                    Context.Watch(l.Listener);
                    Listeners.Add(l.Listener);
                    l.Listener.Tell(new FSMBase.CurrentState<TState>(Self, _currentState.StateName));
                })
                .With<FSMBase.UnsubscribeTransitionCallBack>(ucb =>
                {
                    Context.Unwatch(ucb.ActorRef);
                    Listeners.Remove(ucb.ActorRef);
                })
                .With<Deafen>(d =>
                {
                    Context.Unwatch(d.Listener);
                    Listeners.Remove(d.Listener);
                })
                .With<InternalActivateFsmLogging>(_ => { DebugEvent = true; })
                .Default(msg =>
                {
                    if (_timeoutFuture != null)
                    {
                        _timeoutFuture.Cancel(false);
                        _timeoutFuture = null;
                    }
                    _generation++;
                    ProcessMsg(msg, Sender);
                });
            return match.WasHandled;
        }

        protected void Terminate(State<TState, TData, TEvent> upcomingState)
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
                _currentState = upcomingState;

                var stopEvent = new FSMBase.StopEvent<TState, TData>(reason, _currentState.StateName,
                    _currentState.StateData);
                _terminateEvent(stopEvent);
            }
        }

        /// <summary>
        ///     Call the <see cref="PersistentFSMBase.OnTermination" /> hook if you want to retain this behavior.
        ///     When overriding make sure to call base.PostStop();
        ///     Please note that this method is called by default from <see cref="ActorBase.PreRestart" /> so
        ///     override that one if <see cref="PersistentFSMBase.OnTermination" /> shall not be called during restart.
        /// </summary>
        protected override void PostStop()
        {
            /*
             * Setting this instance's state to Terminated does no harm during restart, since
             * the new instance will initialize fresh using StartWith.
             */
            Terminate(Stay().WithStopReason(new FSMBase.Shutdown()));
            base.PostStop();
        }

        /// <summary>
        ///     By default, <see cref="Failure" /> is logged at error level and other
        ///     reason types are not logged. It is possible to override this behavior.
        /// </summary>
        /// <param name="reason"></param>
        protected virtual void LogTermination(FSMBase.Reason reason)
        {
            reason.Match()
                .With<FSMBase.Failure>(f =>
                {
                    if (f.Cause is Exception)
                    {
                        _log.Error(f.Cause.AsInstanceOf<Exception>(), "terminating due to Failure");
                    }
                    else
                    {
                        _log.Error(f.Cause.ToString());
                    }
                });
        }

        public sealed class TransformHelper
        {
            public TransformHelper(StateFunction func)
            {
                Func = func;
            }

            public StateFunction Func { get; }

            public StateFunction Using(Func<State<TState, TData, TEvent>, State<TState, TData, TEvent>> andThen)
            {
                StateFunction continuedDelegate = (@event, state) => andThen.Invoke(Func.Invoke(@event, state));
                return continuedDelegate;
            }
        }

        public class StateChangeEvent : IMessage
        {
            public StateChangeEvent(TState state, TimeSpan? timeOut)
            {
                State = state;
                TimeOut = timeOut;
            }

            public TState State { get; private set; }

            public TimeSpan? TimeOut { get; private set; }
        }

        #region States

        /// <summary>
        ///     Used in the event of a timeout between transitions
        /// </summary>
        public class StateTimeout
        {
        }

        /*
         * INTERNAL API - used for ensuring that state changes occur on-time
         */

        internal class TimeoutMarker
        {
            public TimeoutMarker(long generation)
            {
                Generation = generation;
            }

            public long Generation { get; }
        }

        [DebuggerDisplay("Timer {Name,nq}, message: {Message")]
        public class Timer : INoSerializationVerificationNeeded
        {
            private readonly ILoggingAdapter _debugLog;
            private readonly ICancelable _ref;

            private readonly IScheduler _scheduler;

            public Timer(string name, object message, bool repeat, int generation, IActorContext context,
                ILoggingAdapter debugLog)
            {
                _debugLog = debugLog;
                Context = context;
                Generation = generation;
                Repeat = repeat;
                Message = message;
                Name = name;
                var scheduler = context.System.Scheduler;
                _scheduler = scheduler;
                _ref = new Cancelable(scheduler);
            }

            public string Name { get; }

            public object Message { get; }

            public bool Repeat { get; }

            public int Generation { get; }

            public IActorContext Context { get; }

            public void Schedule(IActorRef actor, TimeSpan timeout)
            {
                var name = Name;
                var message = Message;

                Action send;
                if (_debugLog != null)
                    send = () =>
                    {
                        _debugLog.Debug("{0}Timer '{1}' went off. Sending {2} -> {3}",
                            _ref.IsCancellationRequested ? "Cancelled " : "", name, message, actor);
                        actor.Tell(this, Context.Self);
                    };
                else
                    send = () => actor.Tell(this, Context.Self);

                if (Repeat) _scheduler.Advanced.ScheduleRepeatedly(timeout, timeout, send, _ref);
                else _scheduler.Advanced.ScheduleOnce(timeout, send, _ref);
            }

            public void Cancel()
            {
                if (!_ref.IsCancellationRequested)
                {
                    _ref.Cancel(false);
                }
            }
        }


        /// <summary>
        ///     This captures all of the managed state of the <see cref="PersistentFSM{T,S,E}" />: the state name,
        ///     the state data, possibly custom timeout, stop reason, and replies accumulated while
        ///     processing the last message.
        /// </summary>
        /// <typeparam name="TS">The name of the state</typeparam>
        /// <typeparam name="TD">The data of the state</typeparam>
        /// <typeparam name="TE">The event of the state</typeparam>
        public class State<TS, TD, TE> : FSMBase.State<TS, TD>
        {
            public Action<TD> AfterTransitionHandler { get; }


            public State(TS stateName, TD stateData, TimeSpan? timeout = null, FSMBase.Reason stopReason = null,
                List<object> replies = null, ILinearSeq<TE> domainEvents = null, Action<TD> afterTransitionDo = null)
                : base(stateName, stateData, timeout, stopReason, replies)
            {
                AfterTransitionHandler = afterTransitionDo;
                DomainEvents = domainEvents;
                Notifies = true;
            }

            public ILinearSeq<TE> DomainEvents { get; }

            public bool Notifies { get; set; }

            /// <summary>
            ///     Specify domain events to be applied when transitioning to the new state.
            /// </summary>
            /// <param name="events"></param>
            /// <returns></returns>
            public State<TS, TD, TE> Applying(ILinearSeq<TE> events)
            {
                if (DomainEvents == null)
                {
                    return Copy(null, null, null, events);
                }
                return Copy(null, null, null, new ArrayLinearSeq<TE>(DomainEvents.Concat(events).ToArray()));
            }


            /// <summary>
            ///     Specify domain event to be applied when transitioning to the new state.
            /// </summary>
            /// <param name="e"></param>
            /// <returns></returns>
            public State<TS, TD, TE> Applying(TE e)
            {
                if (DomainEvents == null)
                {
                    return Copy(null, null, null, new ArrayLinearSeq<TE>(new[] {e}));
                }
                var events = new List<TE>();
                events.AddRange(DomainEvents);
                events.Add(e);
                return Copy(null, null, null, new ArrayLinearSeq<TE>(DomainEvents.Concat(events).ToArray()));
            }


            /// <summary>
            ///     Register a handler to be triggered after the state has been persisted successfully
            /// </summary>
            /// <param name="handler"></param>
            /// <returns></returns>
            public State<TS, TD, TE> AndThen(Action<TD> handler)
            {
                return Copy(null, null, null, null, handler);
            }

            public State<TS, TD, TE> Copy(TimeSpan? timeout, FSMBase.Reason stopReason = null,
                List<object> replies = null, ILinearSeq<TE> domainEvents = null, Action<TD> afterTransitionDo = null)
            {
                return new State<TS, TD, TE>(StateName, StateData, timeout ?? Timeout, stopReason ?? StopReason,
                    replies ?? Replies,
                    domainEvents ?? DomainEvents, afterTransitionDo ?? AfterTransitionHandler);
            }

            /// <summary>
            ///     Modify state transition descriptor with new state data. The data will be set
            ///     when transitioning to the new state.
            /// </summary>
            public new State<TS, TD, TE> Using(TD nextStateData)
            {
                return new State<TS, TD, TE>(StateName, nextStateData, Timeout, StopReason, Replies);
            }


            public new State<TS, TD, TE> Replying(object replyValue)
            {
                if (Replies == null) Replies = new List<object>();
                var newReplies = Replies.ToArray().ToList();
                newReplies.Add(replyValue);
                return Copy(Timeout, replies: newReplies);
            }

            public new State<TS, TD, TE> ForMax(TimeSpan timeout)
            {
                if (timeout <= TimeSpan.MaxValue) return Copy(timeout);
                return Copy(null);
            }

            /// <summary>
            ///     INTERNAL API
            /// </summary>
            internal State<TS, TD, TE> WithStopReason(FSMBase.Reason reason)
            {
                return Copy(null, reason);
            }

            #endregion
        }
    }
}