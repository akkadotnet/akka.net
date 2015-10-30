using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;

namespace Akka.Persistence.Fsm
{
    public abstract class PersistentFSMBase : PersistentActor
    {
        #region States



        /// <summary>
        /// Used in the event of a timeout between transitions
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

            public long Generation { get; private set; }
        }

        [DebuggerDisplay("Timer {Name,nq}, message: {Message")]
        internal class Timer : INoSerializationVerificationNeeded
        {
            private readonly ILoggingAdapter _debugLog;

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

            private readonly IScheduler _scheduler;
            private readonly ICancelable _ref;

            public string Name { get; private set; }

            public object Message { get; private set; }

            public bool Repeat { get; private set; }

            public int Generation { get; private set; }

            public IActorContext Context { get; private set; }

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
        /// This captures all of the managed state of the <see cref="PersistentFSM{T,S,E}"/>: the state name,
        /// the state data, possibly custom timeout, stop reason, and replies accumulated while
        /// processing the last message.
        /// </summary>
        /// <typeparam name="TS">The name of the state</typeparam>
        /// <typeparam name="TD">The data of the state</typeparam>
        /// <typeparam name="TE">The event of the state</typeparam>
        public class State<TS, TD, TE> : FSMBase.State<TS, TD>
        {
            public Action<TD> AfterTransitionHandler { get; private set; }


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
            /// Specify domain events to be applied when transitioning to the new state.
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
            /// Specify domain event to be applied when transitioning to the new state.
            /// </summary>
            /// <param name="e"></param>
            /// <returns></returns>
            public State<TS, TD, TE> Applying(TE e)
            {
                if (DomainEvents == null)
                {
                    return Copy(null, null, null, new ArrayLinearSeq<TE>(new TE[] {e}));
                }
                var events = new List<TE>();
                events.AddRange(DomainEvents);
                events.Add(e);
                return Copy(null, null, null, new ArrayLinearSeq<TE>(DomainEvents.Concat(events).ToArray()));
            }


            /// <summary>
            /// Register a handler to be triggered after the state has been persisted successfully
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
                return new State<TS, TD, TE>(StateName, StateData, timeout ?? Timeout, stopReason ?? StopReason, replies ?? Replies,
                    domainEvents ?? DomainEvents, afterTransitionDo ?? AfterTransitionHandler);
            }

            /// <summary>
            /// Modify state transition descriptor with new state data. The data will be set
            /// when transitioning to the new state.
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

            public new State<TS, TD,TE> ForMax(TimeSpan timeout)
            {
                if (timeout <= TimeSpan.MaxValue) return Copy(timeout);
                return Copy(null);
            }

            /// <summary>
            /// INTERNAL API
            /// </summary>
            internal State<TS, TD, TE> WithStopReason(FSMBase.Reason reason)
            {
                return Copy(null, reason);
            }


            #endregion
        }
    }
}