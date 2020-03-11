//-----------------------------------------------------------------------
// <copyright file="PersistentFSM.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Serialization;
using static Akka.Persistence.Fsm.PersistentFSM;

namespace Akka.Persistence.Fsm
{
    /// <summary>
    /// Finite state machine (FSM) persistent actor.
    /// </summary>
    /// <typeparam name="TState">The state name type</typeparam>
    /// <typeparam name="TData">The state data type</typeparam>
    /// <typeparam name="TEvent">The event data type</typeparam>
    public abstract class PersistentFSM<TState, TData, TEvent> : PersistentFSMBase<TState, TData, TEvent> where TState : IFsmState 
    {
        /// <summary>
        /// Map from state identifier to state instance
        /// </summary>
        private Dictionary<string, TState> StatesMap => StateNames.ToDictionary(c => c.Identifier, c => c);

        /// <summary>
        /// Timeout set for the current state. Used when saving a snapshot
        /// </summary>
        private TimeSpan? CurrentStateTimeout { get; set; } = null;

        /// <summary>
        /// Override this handler to define the action on Domain Event
        /// </summary>
        /// <param name="domainEvent">Domain event to apply.</param>
        /// <param name="currentData">State data of the previous state.</param>
        /// <returns>Updated state data</returns>
        protected abstract TData ApplyEvent(TEvent domainEvent, TData currentData);

        /// <summary>
        /// Override this handler to define the action on recovery completion
        /// </summary>
        protected virtual void OnRecoveryCompleted() { }

        /// <summary>
        /// Save the current state as a snapshot
        /// </summary>
        public void SaveStateSnapshot()
        {
            SaveSnapshot(new PersistentFSMSnapshot<TData>(StateName.Identifier, StateData, CurrentStateTimeout));
        }

        /// <inheritdoc />
        protected override bool ReceiveRecover(object message)
        {
            if (message is TEvent domainEvent)
            {
                StartWith(StateName, ApplyEvent(domainEvent, StateData));
                return true;
            }

            if (message is StateChangeEvent stateChangeEvent)
            {
                StartWith(StatesMap[stateChangeEvent.StateIdentifier], StateData, stateChangeEvent.Timeout);
                return true;
            }

            if (message is SnapshotOffer snapshotOffer)
            {
                var persistentFSMSnapshot = snapshotOffer.Snapshot as PersistentFSMSnapshot<TData>;
                if (persistentFSMSnapshot != null)
                {
                    StartWith(StatesMap[persistentFSMSnapshot.StateIdentifier], persistentFSMSnapshot.Data, persistentFSMSnapshot.Timeout);
                    return true;
                }

                return false;
            }

            if (message is RecoveryCompleted)
            {
                Initialize();
                OnRecoveryCompleted();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Persist FSM State and FSM State Data
        /// </summary>
        /// <param name="nextState">TBD</param>
        protected override void ApplyState(State<TState, TData, TEvent> nextState)
        {
            var eventsToPersist = new List<object>();
            if (nextState.DomainEvents != null)
            {
                foreach (var domainEvent in nextState.DomainEvents)
                {
                    eventsToPersist.Add(domainEvent);
                }
            }

            // Prevent StateChangeEvent persistence when staying in the same state, except when state defines a timeout
            if (nextState.Notifies || nextState.Timeout.HasValue)
            {
                eventsToPersist.Add(new StateChangeEvent(nextState.StateName.Identifier, nextState.Timeout));
            }

            if (eventsToPersist.Count == 0)
            {
                // If there are no events to persist, just apply the state
                base.ApplyState(nextState);
            }
            else
            {
                // Persist the events and apply the new state after all event handlers were executed
                var nextData = StateData;
                var handlersExecutedCounter = 0;

                var snapshotAfterExtension = SnapshotAfterExtension.Get(Context.System);
                var doSnapshot = false;

                void ApplyStateOnLastHandler()
                {
                    handlersExecutedCounter++;
                    if (handlersExecutedCounter == eventsToPersist.Count)
                    {
                        base.ApplyState(nextState.Copy(stateData: nextData));
                        CurrentStateTimeout = nextState.Timeout;
                        nextState.AfterTransitionDo?.Invoke(StateData);
                        if (doSnapshot)
                        {
                            Log.Info($"Saving snapshot, sequence number [{SnapshotSequenceNr}]");
                            SaveStateSnapshot();
                        }
                    }
                }

                PersistAll(eventsToPersist, @event =>
                {
                    if (@event is TEvent evt)
                    {
                        nextData = ApplyEvent(evt, nextData);
                        doSnapshot = doSnapshot || snapshotAfterExtension.IsSnapshotAfterSeqNo(LastSequenceNr);
                        ApplyStateOnLastHandler();
                    }
                    else if (@event is StateChangeEvent)
                    {
                        doSnapshot = doSnapshot || snapshotAfterExtension.IsSnapshotAfterSeqNo(LastSequenceNr);
                        ApplyStateOnLastHandler();
                    }
                });
            }
        }
    }

    public static class PersistentFSM
    {
        /// <summary>
        /// IFsmState interface, makes possible for simple default serialization by conversion to String
        /// </summary>
        public interface IFsmState
        {
            string Identifier { get; }
        }

        /// <summary>
        /// Persisted on state change
        /// </summary>
        public class StateChangeEvent : IMessage
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="StateChangeEvent"/> class.
            /// </summary>
            /// <param name="stateIdentifier">FSM state identifier.</param>
            /// <param name="timeout">FSM state timeout.</param>
            public StateChangeEvent(string stateIdentifier, TimeSpan? timeout)
            {
                StateIdentifier = stateIdentifier;
                Timeout = timeout;
            }

            /// <summary>
            /// FSM state identifier.
            /// </summary>
            public string StateIdentifier { get; }

            /// <summary>
            /// FSM state timeout.
            /// </summary>
            public TimeSpan? Timeout { get; }
        }

        /// <summary>
        /// FSM state and data snapshot
        /// </summary>
        public class PersistentFSMSnapshot<TD> : IMessage
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="PersistentFSMSnapshot{TD}"/> class.
            /// </summary>
            /// <param name="stateIdentifier">FSM state identifier.</param>
            /// <param name="data">FSM state data</param>
            /// <param name="timeout">FSM state timeout.</param>
            public PersistentFSMSnapshot(string stateIdentifier, TD data, TimeSpan? timeout)
            {
                StateIdentifier = stateIdentifier;
                Data = data;
                Timeout = timeout;
            }

            /// <summary>
            /// FSM state identifier.
            /// </summary>
            public string StateIdentifier { get; }

            /// <summary>
            /// FSM state data.
            /// </summary>
            public TD Data { get; }

            /// <summary>
            /// FSM state timeout.
            /// </summary>
            public TimeSpan? Timeout { get; }

            protected bool Equals(PersistentFSMSnapshot<TD> other)
            {
                return string.Equals(StateIdentifier, other.StateIdentifier)
                    && EqualityComparer<TD>.Default.Equals(Data, other.Data)
                    && Timeout.Equals(other.Timeout);
            }

            public override bool Equals(object obj)
            {
                if (ReferenceEquals(null, obj)) return false;
                if (ReferenceEquals(this, obj)) return true;
                if (obj.GetType() != this.GetType()) return false;
                return Equals((PersistentFSMSnapshot<TD>)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = (StateIdentifier != null ? StateIdentifier.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ EqualityComparer<TD>.Default.GetHashCode(Data);
                    hashCode = (hashCode * 397) ^ Timeout.GetHashCode();
                    return hashCode;
                }
            }
        }

        public class State<TS, TD, TE>
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="State{TS, TE, TD}"/>
            /// </summary>
            /// <param name="stateName">TBD</param>
            /// <param name="stateData">TBD</param>
            /// <param name="timeout">TBD</param>
            /// <param name="stopReason">TBD</param>
            /// <param name="replies">TBD</param>
            /// <param name="domainEvents">TBD</param>
            /// <param name="afterTransitionDo"></param>
            /// <param name="notifies">TBD</param>
            public State(
                TS stateName,
                TD stateData,
                TimeSpan? timeout = null,
                FSMBase.Reason stopReason = null,
                IReadOnlyList<object> replies = null,
                IReadOnlyList<TE> domainEvents = null,
                Action<TD> afterTransitionDo = null,
                bool notifies = true)
            {
                StateName = stateName;
                StateData = stateData;
                Timeout = timeout;
                StopReason = stopReason;
                AfterTransitionDo = afterTransitionDo;
                Replies = replies ?? new List<object>();
                DomainEvents = domainEvents ?? new List<TE>();
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
            public FSMBase.Reason StopReason { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public IReadOnlyList<object> Replies { get; protected set; }

            /// <summary>
            /// TBD
            /// </summary>
            public IReadOnlyList<TE> DomainEvents { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public Action<TD> AfterTransitionDo { get; }

            /// <summary>
            /// TBD
            /// </summary>
            internal bool Notifies { get; }

            internal State<TS, TD, TE> Copy(
                TS stateName = default(TS),
                TD stateData = default(TD),
                TimeSpan? timeout = null,
                FSMBase.Reason stopReason = null,
                IReadOnlyList<object> replies = null,
                IReadOnlyList<TE> domainEvents = null,
                Action<TD> afterTransitionDo = null,
                bool? notifies = null)
            {
                return new State<TS, TD, TE>(
                    Equals(stateName, default(TS)) ? StateName : stateName,
                    Equals(stateData, default(TD)) ? StateData : stateData,
                    timeout == TimeSpan.MinValue ? null : timeout ?? Timeout,
                    stopReason ?? StopReason,
                    replies ?? Replies,
                    domainEvents ?? DomainEvents,
                    afterTransitionDo ?? AfterTransitionDo,
                    notifies ?? Notifies);
            }

            /// <summary>
            /// Modify the state transition descriptor to include a state timeout for the 
            /// next state. This timeout overrides any default timeout set for the next state.
            /// <remarks>Use <see cref="TimeSpan.MaxValue"/> to cancel a timeout.</remarks>
            /// </summary>
            /// <param name="timeout">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD, TE> ForMax(TimeSpan timeout)
            {
                if (timeout <= TimeSpan.MaxValue)
                    return Copy(timeout: timeout);
                return Copy(timeout: TimeSpan.MinValue);
            }

            /// <summary>
            /// Send reply to sender of the current message, if available.
            /// </summary>
            /// <param name="replyValue">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD, TE> Replying(object replyValue)
            {
                var newReplies = new List<object>(Replies.Count + 1);
                newReplies.Add(replyValue);
                newReplies.AddRange(Replies);
                return Copy(replies: newReplies);
            }

            /// <summary>
            /// Modify state transition descriptor with new state data. The data will be set
            /// when transitioning to the new state.
            /// </summary>
            /// <param name="nextStateData">TBD</param>
            /// <returns>TBD</returns>
            [Obsolete("Internal API easily to be confused with regular FSM's using. " +
                "Use regular events (`Applying`). " +
                "Internally, `copy` can be used instead.")]
            public State<TS, TD, TE> Using(TD nextStateData)
            {
                return Copy(stateData: nextStateData);
            }

            /// <summary>
            /// INTERNAL API.
            /// </summary>
            /// <param name="reason">TBD</param>
            /// <returns>TBD</returns>
            internal State<TS, TD, TE> WithStopReason(FSMBase.Reason reason)
            {
                return Copy(stopReason: reason);
            }

            /// <summary>
            /// INTERNAL API.
            /// </summary>
            internal State<TS, TD, TE> WithNotification(bool notifies)
            {
                return Copy(notifies: notifies);
            }

            /// <summary>
            /// Specify domain events to be applied when transitioning to the new state.
            /// </summary>
            /// <param name="events">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD, TE> Applying(params TE[] events)
            {
                var newDomainEvents = new List<TE>(DomainEvents.Count + events.Length);
                newDomainEvents.AddRange(DomainEvents);
                newDomainEvents.AddRange(events);
                return Copy(domainEvents: newDomainEvents);
            }

            /// <summary>
            /// Register a handler to be triggered after the state has been persisted successfully
            /// </summary>
            /// <param name="handler">TBD</param>
            /// <returns>TBD</returns>
            public State<TS, TD, TE> AndThen(Action<TD> handler)
            {
                return Copy(afterTransitionDo: handler);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString()
            {
                return $"State<TS, TD, TE><StateName: {StateName}, StateData: {StateData}, Timeout: {Timeout}, StopReason: {StopReason}, Notifies: {Notifies}>";
            }
        }
    }

    internal sealed class SnapshotAfterExtensionProvider : ExtensionIdProvider<SnapshotAfterExtension>
    {
        public override SnapshotAfterExtension CreateExtension(ExtendedActorSystem system)
        {
            return new SnapshotAfterExtension(system.Settings.Config);
        }
    }

    internal class SnapshotAfterExtension : IExtension
    {
        private const string Key = "akka.persistence.fsm.snapshot-after";

        public SnapshotAfterExtension(Config config)
        {
            var useSnapshot = config.GetString(Key, "");
            if (useSnapshot.ToLowerInvariant().Equals("off") ||
                useSnapshot.ToLowerInvariant().Equals("false") ||
                useSnapshot.ToLowerInvariant().Equals("no"))
            {
                SnapshotAfterValue = null;
            }
            else
            {
                SnapshotAfterValue = config.GetInt(Key, 0);
            }
        }
        
        public int? SnapshotAfterValue { get; }

        public bool IsSnapshotAfterSeqNo(long lastSequenceNr)
        {
            if (SnapshotAfterValue.HasValue)
            {
                return lastSequenceNr % SnapshotAfterValue.Value == 0;
            }
            else
            {
                return false; //always false, if snapshotAfter is not specified in config
            }
        }

        public static SnapshotAfterExtension Get(ActorSystem system)
        {
            return system.WithExtension<SnapshotAfterExtension, SnapshotAfterExtensionProvider>();
        }
    }
}
