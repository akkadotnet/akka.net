//-----------------------------------------------------------------------
// <copyright file="FSM.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.Pattern;
using Akka.Routing;
using Akka.Util.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class FSMBase : ActorBase
    {
        #region States

        /// <summary>
        /// Message type which is sent directly to the subscriber Actor in <see cref="SubscribeTransitionCallBack"/>
        /// before sending any <see cref="Transition{TS}"/> messages.
        /// </summary>
        /// <typeparam name="TS">The type of the state being used in this finite state machine.</typeparam>
        public sealed class CurrentState<TS>
        {
            /// <summary>
            /// Initializes a new instance of the CurrentState
            /// </summary>
            /// <param name="fsmRef">TBD</param>
            /// <param name="state">TBD</param>
            public CurrentState(IActorRef fsmRef, TS state)
            {
                State = state;
                FsmRef = fsmRef;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef FsmRef { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TS State { get; }

            #region Equality
            private bool Equals(CurrentState<TS> other)
            {
                return Equals(FsmRef, other.FsmRef) && EqualityComparer<TS>.Default.Equals(State, other.State);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is CurrentState<TS> && Equals((CurrentState<TS>)obj);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    return ((FsmRef != null ? FsmRef.GetHashCode() : 0) * 397) ^ EqualityComparer<TS>.Default.GetHashCode(State);
                }
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"CurrentState <{State}>";
            }
            #endregion
        }

        /// <summary>
        /// Message type which is used to communicate transitions between states to all subscribed listeners
        /// (use <see cref="SubscribeTransitionCallBack"/>)
        /// </summary>
        /// <typeparam name="TS">The type of state used</typeparam>
        public sealed class Transition<TS>
        {
            /// <summary>
            /// Initializes a new instance of the Transition
            /// </summary>
            /// <param name="fsmRef">TBD</param>
            /// <param name="from">TBD</param>
            /// <param name="to">TBD</param>
            public Transition(IActorRef fsmRef, TS from, TS to)
            {
                To = to;
                From = from;
                FsmRef = fsmRef;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef FsmRef { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TS From { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TS To { get; }

            #region Equality
            private bool Equals(Transition<TS> other)
            {
                return Equals(FsmRef, other.FsmRef) && EqualityComparer<TS>.Default.Equals(From, other.From) && EqualityComparer<TS>.Default.Equals(To, other.To);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                return obj is Transition<TS> && Equals((Transition<TS>)obj);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (FsmRef != null ? FsmRef.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ EqualityComparer<TS>.Default.GetHashCode(From);
                    hashCode = (hashCode * 397) ^ EqualityComparer<TS>.Default.GetHashCode(To);
                    return hashCode;
                }
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"Transition({From}, {To})";
            }

            #endregion
        }

        /// <summary>
        /// Send this to an <see cref="SubscribeTransitionCallBack"/> to request first the <see cref="UnsubscribeTransitionCallBack"/>
        /// followed by a series of <see cref="Transition{TS}"/> updates. Cancel the subscription using
        /// <see cref="CurrentState{TS}"/>.
        /// </summary>
        public sealed class SubscribeTransitionCallBack
        {
            /// <summary>
            /// Initializes a new instance of the SubscribeTransitionCallBack
            /// </summary>
            /// <param name="actorRef">TBD</param>
            public SubscribeTransitionCallBack(IActorRef actorRef)
            {
                ActorRef = actorRef;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef ActorRef { get; }
        }

        /// <summary>
        /// Unsubscribe from <see cref="SubscribeTransitionCallBack"/> notifications which were
        /// initialized by sending the corresponding <see cref="Transition{TS}"/>.
        /// </summary>
        public sealed class UnsubscribeTransitionCallBack
        {
            /// <summary>
            /// Initializes a new instance of the UnsubscribeTransitionCallBack
            /// </summary>
            /// <param name="actorRef">TBD</param>
            public UnsubscribeTransitionCallBack(IActorRef actorRef)
            {
                ActorRef = actorRef;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef ActorRef { get; }
        }

        /// <summary>
        /// Reason why this <see cref="FSM{T,S}"/> is shutting down
        /// </summary>
        public abstract class Reason { }

        /// <summary>
        /// Default <see cref="Reason"/> if calling Stop().
        /// </summary>
        public sealed class Normal : Reason
        {
            internal Normal() { }

            /// <summary>
            /// Singleton instance of Normal
            /// </summary>
            public static Normal Instance { get; } = new Normal();
        }

        /// <summary>
        /// Reason given when someone as calling <see cref="FSM{TState,TData}.Stop()"/> from outside;
        /// also applies to <see cref="ActorSystem"/> supervision directive.
        /// </summary>
        public sealed class Shutdown : Reason
        {
            internal Shutdown() { }

            /// <summary>
            /// Singleton instance of Shutdown
            /// </summary>
            public static Shutdown Instance { get; } = new Shutdown();
        }

        /// <summary>
        /// Signifies that the <see cref="FSM{T,S}"/> is shutting itself down because of an error,
        /// e.g. if the state to transition into does not exist. You can use this to communicate a more
        /// precise cause to the <see cref="FSM{T,S}.OnTermination"/> block.
        /// </summary>
        public sealed class Failure : Reason
        {
            /// <summary>
            /// Initializes a new instance of the Failure
            /// </summary>
            /// <param name="cause">TBD</param>
            public Failure(object cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public object Cause { get; }

            /// <inheritdoc/>
            public override string ToString() => $"Failure({Cause})";
        }

        /// <summary>
        /// Used in the event of a timeout between transitions
        /// </summary>
        public sealed class StateTimeout
        {
            internal StateTimeout() { }

            /// <summary>
            /// Singleton instance of StateTimeout
            /// </summary>
            public static StateTimeout Instance { get; } = new StateTimeout();
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal sealed class TimeoutMarker
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="generation">TBD</param>
            public TimeoutMarker(long generation)
            {
                Generation = generation;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public long Generation { get; }
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        [DebuggerDisplay("Timer {Name,nq}, message: {Message}")]
        internal class Timer : INoSerializationVerificationNeeded
        {
            private ICancelable _ref;
            private readonly IScheduler _scheduler;

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="name">TBD</param>
            /// <param name="message">TBD</param>
            /// <param name="repeat">TBD</param>
            /// <param name="generation">TBD</param>
            /// <param name="owner">TBD</param>
            /// <param name="context">TBD</param>
            public Timer(string name, object message, bool repeat, int generation, ActorBase owner, IActorContext context)
            {
                Context = context;
                Generation = generation;
                Repeat = repeat;
                Message = message;
                Name = name;
                Owner = owner;

                _scheduler = context.System.Scheduler;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public string Name { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public object Message { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public bool Repeat { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public int Generation { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public ActorBase Owner { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorContext Context { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actor">TBD</param>
            /// <param name="timeout">TBD</param>
            public void Schedule(IActorRef actor, TimeSpan timeout)
            {
                object timerMsg;
                if (Message is IAutoReceivedMessage)
                    timerMsg = Message;
                else
                    timerMsg = this;

                _ref = Repeat
                    ? _scheduler.ScheduleTellRepeatedlyCancelable(timeout, timeout, actor, timerMsg, Context.Self)
                    : _scheduler.ScheduleTellOnceCancelable(timeout, actor, timerMsg, Context.Self);
            }

            /// <summary>
            /// TBD
            /// </summary>
            public void Cancel()
            {
                if (_ref != null)
                {
                    _ref.Cancel(false);
                    _ref = null;
                }
            }
        }

        /// <summary>
        /// Log entry of the <see cref="ILoggingFSM"/> - can be obtained by calling <see cref="GetLog"/>
        /// </summary>
        /// <typeparam name="TS">The name of the state</typeparam>
        /// <typeparam name="TD">The data of the state</typeparam>
        public sealed class LogEntry<TS, TD>
        {
            /// <summary>
            /// Initializes a new instance of the LogEntry
            /// </summary>
            /// <param name="stateName">TBD</param>
            /// <param name="stateData">TBD</param>
            /// <param name="fsmEvent">TBD</param>
            public LogEntry(TS stateName, TD stateData, object fsmEvent)
            {
                FsmEvent = fsmEvent;
                StateData = stateData;
                StateName = stateName;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TS StateName { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TD StateData { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public object FsmEvent { get; }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"StateName: <{StateName}>, StateData: <{StateData}>, FsmEvent: <{FsmEvent}>";
            }
        }

        /// <summary>
        /// This captures all of the managed state of the <see cref="FSM{T,S}"/>: the state name,
        /// the state data, possibly custom timeout, stop reason, and replies accumulated while
        /// processing the last message.
        /// </summary>
        /// <typeparam name="TS">The name of the state</typeparam>
        /// <typeparam name="TD">The data of the state</typeparam>
        public class State<TS, TD> : IEquatable<State<TS, TD>>
        {
            /// <summary>
            /// Initializes a new instance of the State
            /// </summary>
            /// <param name="stateName">TBD</param>
            /// <param name="stateData">TBD</param>
            /// <param name="timeout">TBD</param>
            /// <param name="stopReason">TBD</param>
            /// <param name="replies">TBD</param>
            /// <param name="notifies">TBD</param>
            public State(TS stateName, TD stateData, TimeSpan? timeout = null, Reason stopReason = null, IReadOnlyList<object> replies = null, bool notifies = true)
            {
                Replies = replies ?? new List<object>();
                StopReason = stopReason;
                Timeout = timeout;
                StateData = stateData;
                StateName = stateName;
                Notifies = notifies;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TS StateName { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TD StateData { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TimeSpan? Timeout { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public Reason StopReason { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public IReadOnlyList<object> Replies { get; protected set; }

            /// <summary>
            /// TBD
            /// </summary>
            internal bool Notifies { get; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="timeout">TBD</param>
            /// <param name="stopReason">TBD</param>
            /// <param name="replies">TBD</param>
            /// <returns>TBD</returns>
            internal State<TS, TD> Copy(TimeSpan? timeout, Reason stopReason = null, IReadOnlyList<object> replies = null)
            {
                return new State<TS, TD>(StateName, StateData, timeout, stopReason ?? StopReason, replies ?? Replies, Notifies);
            }

            /// <summary>
            /// Modify the state transition descriptor to include a state timeout for the
            /// next state. This timeout overrides any default timeout set for the next state.
            /// <remarks>Use <see cref="TimeSpan.MaxValue"/> to cancel a timeout.</remarks>
            /// </summary>
            /// <param name="timeout">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD> ForMax(TimeSpan timeout)
            {
                if (timeout <= TimeSpan.MaxValue)
                    return Copy(timeout);
                return Copy(timeout: null);
            }

            /// <summary>
            /// Send reply to sender of the current message, if available.
            /// </summary>
            /// <param name="replyValue">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD> Replying(object replyValue)
            {
                var newReplies = new List<object>(Replies.Count + 1);
                newReplies.Add(replyValue);
                newReplies.AddRange(Replies);

                return Copy(Timeout, replies: newReplies);
            }

            /// <summary>
            /// Modify state transition descriptor with new state data. The data will be set
            /// when transitioning to the new state.
            /// </summary>
            /// <param name="nextStateData">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD> Using(TD nextStateData)
            {
                return new State<TS, TD>(StateName, nextStateData, Timeout, StopReason, Replies, Notifies);
            }

            /// <summary>
            /// INTERNAL API
            /// </summary>
            /// <param name="reason">TBD</param>
            /// <returns>TBD</returns>
            internal State<TS, TD> WithStopReason(Reason reason)
            {
                return Copy(Timeout, reason);
            }

            /// <summary>
            /// INTERNAL API
            /// </summary>
            internal State<TS, TD> WithNotification(bool notifies)
            {
                return new State<TS, TD>(StateName, StateData, Timeout, StopReason, Replies, notifies);
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"{StateName}, {StateData}";
            }

            /// <inheritdoc/>
            public bool Equals(State<TS, TD> other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return EqualityComparer<TS>.Default.Equals(StateName, other.StateName) && EqualityComparer<TD>.Default.Equals(StateData, other.StateData) && Timeout.Equals(other.Timeout) && Equals(StopReason, other.StopReason) && Equals(Replies, other.Replies);
            }

            /// <inheritdoc/>
            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != GetType()) return false;
                return Equals((State<TS, TD>)obj);
            }

            /// <inheritdoc/>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = EqualityComparer<TS>.Default.GetHashCode(StateName);
                    hashCode = (hashCode * 397) ^ EqualityComparer<TD>.Default.GetHashCode(StateData);
                    hashCode = (hashCode * 397) ^ Timeout.GetHashCode();
                    hashCode = (hashCode * 397) ^ (StopReason != null ? StopReason.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (Replies != null ? Replies.GetHashCode() : 0);
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// All messages sent to the <see cref="FSM{TS,TD}"/> will be wrapped inside an <see cref="Event{TD}"/>,
        /// which allows pattern matching to extract both state and data.
        /// </summary>
        /// <typeparam name="TD">The state data for this event</typeparam>
        public sealed class Event<TD> : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// Initializes a new instance of the Event
            /// </summary>
            /// <param name="fsmEvent">TBD</param>
            /// <param name="stateData">TBD</param>
            public Event(object fsmEvent, TD stateData)
            {
                StateData = stateData;
                FsmEvent = fsmEvent;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public object FsmEvent { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TD StateData { get; }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"Event: <{FsmEvent}>, StateData: <{StateData}>";
            }
        }

        /// <summary>
        /// Class representing the state of the <see cref="FSM{TS,TD}"/> within the OnTermination block.
        /// </summary>
        /// <typeparam name="TS">TBD</typeparam>
        /// <typeparam name="TD">TBD</typeparam>
        public sealed class StopEvent<TS, TD> : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// Initializes a new instance of the StopEvent
            /// </summary>
            /// <param name="reason">TBD</param>
            /// <param name="terminatedState">TBD</param>
            /// <param name="stateData">TBD</param>
            public StopEvent(Reason reason, TS terminatedState, TD stateData)
            {
                StateData = stateData;
                TerminatedState = terminatedState;
                Reason = reason;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Reason Reason { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TS TerminatedState { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TD StateData { get; }

            /// <inheritdoc/>
            public override string ToString()
            {
                return $"Reason: <{Reason}>, TerminatedState: <{TerminatedState}>, StateData: <{StateData}>";
            }
        }

        #endregion
    }

    /// <summary>
    /// Finite state machine (FSM) actor.
    /// </summary>
    /// <typeparam name="TState">The state name type</typeparam>
    /// <typeparam name="TData">The state data type</typeparam>
    public abstract class FSM<TState, TData> : FSMBase, IListeners, IInternalSupportsTestFSMRef<TState,TData>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        /// <summary>
        /// Initializes a new instance of the FSM class.
        /// </summary>
        protected FSM()
        {
            if (this is ILoggingFSM)
                DebugEvent = Context.System.Settings.FsmDebugEvent;
        }

        /// <summary>
        /// Delegate describing this state's response to input
        /// </summary>
        /// <param name="fsmEvent">TBD</param>
        /// <returns>TBD</returns>
        public delegate State<TState, TData> StateFunction(Event<TData> fsmEvent);

        /// <summary>
        /// Handler which is called upon each state transition
        /// </summary>
        /// <param name="initialState">State designator for the initial state</param>
        /// <param name="nextState">State designator for the next state</param>
        public delegate void TransitionHandler(TState initialState, TState nextState);

        #region Finite State Machine Domain Specific Language (FSM DSL if you like acronyms)

        /// <summary>
        /// Insert a new <see cref="StateFunction"/> at the end of the processing chain for the
        /// given state. If the stateTimeout parameter is set, entering this state without a
        /// differing explicit timeout setting will trigger a <see cref="FSMBase.StateTimeout"/>.
        /// </summary>
        /// <param name="stateName">designator for the state</param>
        /// <param name="func">delegate describing this state's response to input</param>
        /// <param name="timeout">default timeout for this state</param>
        public void When(TState stateName, StateFunction func, TimeSpan? timeout = null)
        {
            Register(stateName, func, timeout);
        }

        /// <summary>
        /// Sets the initial state for this FSM. Call this method from the constructor before the <see cref="Initialize"/> method.
        /// If different state is needed after a restart this method, followed by <see cref="Initialize"/>, can be used in the actor
        /// life cycle hooks <see cref="ActorBase.PreStart()"/> and <see cref="ActorBase.PostRestart"/>.
        /// </summary>
        /// <param name="stateName">Initial state designator.</param>
        /// <param name="stateData">Initial state data.</param>
        /// <param name="timeout">State timeout for the initial state, overriding the default timeout for that state.</param>
        public void StartWith(TState stateName, TData stateData, TimeSpan? timeout = null)
        {
            _currentState = new State<TState, TData>(stateName, stateData, timeout);
        }

        /// <summary>
        /// Produce transition to other state. Return this from a state function
        /// in order to effect the transition.
        /// </summary>
        /// <param name="nextStateName">State designator for the next state</param>
        /// <returns>State transition descriptor</returns>
        public State<TState, TData> GoTo(TState nextStateName)
        {
            return new State<TState, TData>(nextStateName, _currentState.StateData);
        }

        /// <summary>
        /// Obsolete. Use <c>GoTo(nextStateName).Using(stateData) instead.</c>
        /// </summary>
        /// <param name="nextStateName">N/A</param>
        /// <param name="stateData">N/A</param>
        /// <returns>N/A</returns>
        [Obsolete("This method is obsoleted. Use GoTo(nextStateName).Using(newStateData) [1.2.0]")]
        public State<TState, TData> GoTo(TState nextStateName, TData stateData)
        {
            return new State<TState, TData>(nextStateName, stateData);
        }

        /// <summary>
        /// Produce "empty" transition descriptor. Return this from a state function
        /// when no state change is to be effected.
        /// </summary>
        /// <returns>Descriptor for staying in the current state.</returns>
        public State<TState, TData> Stay()
        {
            return GoTo(_currentState.StateName).WithNotification(false);
        }

        /// <summary>
        /// Produce change descriptor to stop this FSM actor with <see cref="FSMBase.Reason"/> <see cref="FSMBase.Normal"/>
        /// </summary>
        /// <returns>Descriptor for stopping in the current state.</returns>
        public State<TState, TData> Stop()
        {
            return Stop(Normal.Instance);
        }

        /// <summary>
        /// Produce change descriptor to stop this FSM actor with the specified <see cref="FSMBase.Reason"/>.
        /// </summary>
        /// <param name="reason">Reason why this <see cref="FSM{TState,TData}"/> is shutting down.</param>
        /// <returns>Descriptor for stopping in the current state.</returns>
        public State<TState, TData> Stop(Reason reason)
        {
            return Stop(reason, _currentState.StateData);
        }

        /// <summary>
        /// Produce change descriptor to stop this FSM actor with the specified <see cref="FSMBase.Reason"/>.
        /// </summary>
        /// <param name="reason">Reason why this <see cref="FSM{TState,TData}"/> is shutting down.</param>
        /// <param name="stateData">State data.</param>
        /// <returns>Descriptor for stopping in the current state.</returns>
        public State<TState, TData> Stop(Reason reason, TData stateData)
        {
            return Stay().Using(stateData).WithStopReason(reason);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class TransformHelper
        {
            /// <summary>
            /// TBD
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
            public StateFunction Using(Func<State<TState, TData>, State<TState, TData>> andThen)
            {
                StateFunction continuedDelegate = @event => andThen.Invoke(Func.Invoke(@event));
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
        /// <param name="name">identifier to be used with <see cref="CancelTimer"/>.</param>
        /// <param name="msg">message to be delivered</param>
        /// <param name="timeout">delay of first message delivery and between subsequent messages.</param>
        /// <param name="repeat">send once if false, scheduleAtFixedRate if true</param>
        public void SetTimer(string name, object msg, TimeSpan timeout, bool repeat = false)
        {
            if (DebugEvent)
                _log.Debug($"setting {(repeat ? "repeating" : "")} timer {name}/{timeout}: {msg}");

            if (_timers.TryGetValue(name, out var timer))
                timer.Cancel();

            timer = new Timer(name, msg, repeat, _timerGen.Next(), this, Context);
            timer.Schedule(Self, timeout);
            _timers[name] = timer;
        }

        /// <summary>
        /// Cancel a named <see cref="FSMBase.Timer"/>, ensuring that the message is not subsequently delivered (no race.)
        /// </summary>
        /// <param name="name">The name of the timer to cancel.</param>
        public void CancelTimer(string name)
        {
            if (DebugEvent)
                _log.Debug($"Cancelling timer {name}");

            if (_timers.TryGetValue(name, out var timer))
            {
                timer.Cancel();
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
        public void SetStateTimeout(TState state, TimeSpan? timeout)
        {
            _stateTimeouts[state] = timeout;
        }

        // Internal API
        bool IInternalSupportsTestFSMRef<TState, TData>.IsStateTimerActive => _timeoutFuture != null;

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
        public void OnTermination(Action<StopEvent<TState, TData>> terminationHandler)
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
        /// <exception cref="IllegalStateException">
        /// This exception is thrown when this method is called before <see cref="StartWith"/> is called.
        /// </exception>
        public void Initialize()
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
                throw new InvalidOperationException("NextStateData is only available during OnTransition");
            }
        }

        #endregion

        #region Internal implementation details

        /// <summary>
        /// Retrieves the support needed to interact with an actor's listeners.
        /// </summary>
        public ListenerSupport Listeners { get; } = new ListenerSupport();

        /// <summary>
        /// Can be set to enable debugging on certain actions taken by the FSM
        /// </summary>
        protected bool DebugEvent;

        /// <summary>
        /// FSM state data and current timeout handling
        /// </summary>
        private State<TState, TData> _currentState;

        private ICancelable _timeoutFuture;
        private State<TState, TData> _nextState;
        private long _generation = 0L;

        /// <summary>
        /// Timer handling
        /// </summary>
        private readonly IDictionary<string, Timer> _timers = new Dictionary<string, Timer>();
        private readonly AtomicCounter _timerGen = new AtomicCounter(0);

        /// <summary>
        /// State definitions
        /// </summary>
        private readonly Dictionary<TState, StateFunction> _stateFunctions = new Dictionary<TState, StateFunction>();
        private readonly Dictionary<TState, TimeSpan?> _stateTimeouts = new Dictionary<TState, TimeSpan?>();

        private void Register(TState name, StateFunction function, TimeSpan? timeout)
        {
            if (_stateFunctions.TryGetValue(name, out var stateFunction))
            {
                _stateFunctions[name] = OrElse(stateFunction, function);
                _stateTimeouts[name] = _stateTimeouts[name] ?? timeout;
            }
            else
            {
                _stateFunctions.Add(name, function);
                _stateTimeouts.Add(name, timeout);
            }
        }

        /// <summary>
        /// Unhandled event handler
        /// </summary>
        private StateFunction HandleEventDefault
        {
            get
            {
                return delegate(Event<TData> @event)
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


        /// <summary>
        /// Termination handling
        /// </summary>
        private Action<StopEvent<TState, TData>> _terminateEvent = @event =>{};

        /// <summary>
        /// Transition handling
        /// </summary>
        private readonly IList<TransitionHandler> _transitionEvent = new List<TransitionHandler>();

        private void HandleTransition(TState previous, TState next)
        {
            foreach (var tran in _transitionEvent)
            {
                tran.Invoke(previous, next);
            }
        }

        /// <summary>
        /// C# port of Scala's orElse method for partial function chaining.
        ///
        /// See http://scalachina.com/api/scala/PartialFunction.html
        /// </summary>
        /// <param name="original">The original <see cref="StateFunction"/> to be called</param>
        /// <param name="fallback">The <see cref="StateFunction"/> to be called if <paramref name="original"/> returns null</param>
        /// <returns>A <see cref="StateFunction"/> which combines both the results of <paramref name="original"/> and <paramref name="fallback"/></returns>
        private static StateFunction OrElse(StateFunction original, StateFunction fallback)
        {
            StateFunction chained = delegate(Event<TData> @event)
            {
                var originalResult = original.Invoke(@event);
                if (originalResult == null) return fallback.Invoke(@event);
                return originalResult;
            };

            return chained;
        }

        #endregion

        #region Actor methods

        /// <inheritdoc/>
        protected override bool Receive(object message)
        {
            var timeoutMarker = message as TimeoutMarker;
            if (timeoutMarker != null)
            {
                if (_generation == timeoutMarker.Generation)
                {
                    ProcessMsg(StateTimeout.Instance, "state timeout");
                }
                return true;
            }

            if (message is Timer timer)
            {
                if (ReferenceEquals(timer.Owner, this) && _timers.TryGetValue(timer.Name, out var oldTimer) && oldTimer.Generation == timer.Generation)
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

            var subscribeTransitionCallBack = message as SubscribeTransitionCallBack;
            if (subscribeTransitionCallBack != null)
            {
                Context.Watch(subscribeTransitionCallBack.ActorRef);
                Listeners.Add(subscribeTransitionCallBack.ActorRef);
                //send the current state back as a reference point
                subscribeTransitionCallBack.ActorRef.Tell(new CurrentState<TState>(Self, _currentState.StateName));
                return true;
            }

            var listen = message as Listen;
            if (listen != null)
            {
                Context.Watch(listen.Listener);
                Listeners.Add(listen.Listener);
                listen.Listener.Tell(new CurrentState<TState>(Self, _currentState.StateName));
                return true;
            }

            var unsubscribeTransitionCallBack = message as UnsubscribeTransitionCallBack;
            if (unsubscribeTransitionCallBack != null)
            {
                Context.Unwatch(unsubscribeTransitionCallBack.ActorRef);
                Listeners.Remove(unsubscribeTransitionCallBack.ActorRef);
                return true;
            }

            var deafen = message as Deafen;
            if (deafen != null)
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
            var fsmEvent = new Event<TData>(any, _currentState.StateData);
            ProcessEvent(fsmEvent, source);
        }

        private void ProcessEvent(Event<TData> fsmEvent, object source)
        {
            if (DebugEvent)
            {
                var srcStr = GetSourceString(source);
                _log.Debug("processing {0} from {1} in state {2}", fsmEvent, srcStr, StateName);
            }

            var stateFunc = _stateFunctions[_currentState.StateName];
            var oldState = _currentState;

            State<TState, TData> nextState = null;

            if (stateFunc != null)
            {
                nextState = stateFunc(fsmEvent);
            }

            if (nextState == null)
            {
                nextState = HandleEvent(fsmEvent);
            }

            ApplyState(nextState);

            if (DebugEvent && !Equals(oldState, nextState))
            {
                _log.Debug("transition {0} -> {1}", oldState, nextState);
            }
        }

        private string GetSourceString(object source)
        {
            var s = source as string;
            if (s != null)
                return s;

            var timer = source as Timer;
            if (timer != null)
                return "timer '" + timer.Name + "'";

            var actorRef = source as IActorRef;
            if (actorRef != null)
                return actorRef.ToString();

            return "unknown";
        }

        //Internal API
        void IInternalSupportsTestFSMRef<TState, TData>.ApplyState(State<TState, TData> upcomingState)
        {
            ApplyState(upcomingState);
        }

        private void ApplyState(State<TState, TData> nextState)
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

        private void MakeTransition(State<TState, TData> nextState)
        {
            if (!_stateFunctions.ContainsKey(nextState.StateName))
            {
                Terminate(Stay().WithStopReason(new Failure($"Next state {nextState.StateName} does not exist")));
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
                    HandleTransition(_currentState.StateName, nextState.StateName);
                    Listeners.Gossip(new Transition<TState>(Self, _currentState.StateName, nextState.StateName));
                    _nextState = null;
                }
                _currentState = nextState;

                var timeout = _currentState.Timeout ?? _stateTimeouts[_currentState.StateName];
                if (timeout.HasValue)
                {
                    var t = timeout.Value;
                    if (t < TimeSpan.MaxValue)
                    {
                        _timeoutFuture = Context.System.Scheduler.ScheduleTellOnceCancelable(t, Context.Self, new TimeoutMarker(_generation), Context.Self);
                    }
                }
            }
        }

        private void Terminate(State<TState, TData> upcomingState)
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

                var stopEvent = new StopEvent<TState, TData>(reason, _currentState.StateName, _currentState.StateData);
                _terminateEvent(stopEvent);
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
            Terminate(Stay().WithStopReason(Shutdown.Instance));
            base.PostStop();
        }

        #endregion

        /// <summary>
        /// By default, <see cref="Failure"/> is logged at error level and other
        /// reason types are not logged. It is possible to override this behavior.
        /// </summary>
        /// <param name="reason">TBD</param>
        protected virtual void LogTermination(Reason reason)
        {
            var failure = reason as Failure;
            if (failure != null)
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
    }

    /// <summary>
    /// Marker interface to let the setting "akka.actor.debug.fsm" control if logging should occur in <see cref="FSM{TS,TD}"/>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface ILoggingFSM { }
}

