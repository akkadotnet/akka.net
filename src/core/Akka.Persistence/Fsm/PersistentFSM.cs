//-----------------------------------------------------------------------
// <copyright file="FSM.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence.Fsm
{
    /// <summary>
    ///     Finite state machine (FSM) persistent actor.
    /// </summary>
    /// <typeparam name="TState">The state name type</typeparam>
    /// <typeparam name="TData">The state data type</typeparam>
    /// <typeparam name="TEvent">The event data type</typeparam>
    public abstract class PersistentFSM<TState, TData, TEvent> : PersistentFSMBase<TState, TData, TEvent>
    {
      

        protected abstract void OnRecoveryCompleted();

        protected override bool ReceiveRecover(object message)
        {
            var match = message.Match()
                .With<RecoveryCompleted>(t =>
                {
                    Initialize();
                    OnRecoveryCompleted();
                })
                .With<TEvent>(e => { StartWith(StateName, ApplyEvent(e, StateData)); })
                .With<StateChangeEvent>(sce => { StartWith(sce.State, StateData, sce.TimeOut); });

            return match.WasHandled;
        }

        protected abstract TData ApplyEvent(TEvent e, TData data);

        protected override void ApplyState(State<TState, TData, TEvent> upcomingState)
        {
            var eventsToPersist = new List<object>();
            if (upcomingState.DomainEvents != null)
            {
                foreach (var domainEvent in upcomingState.DomainEvents)
                {
                    eventsToPersist.Add(domainEvent);
                }
            }
            if (!StateName.Equals(upcomingState.StateName) || upcomingState.Timeout.HasValue)
            {
                eventsToPersist.Add(new StateChangeEvent(upcomingState.StateName, upcomingState.Timeout));
            }
            if (eventsToPersist.Count == 0)
            {
                base.ApplyState(upcomingState);
            }
            else
            {
                var nextData = StateData; // upcomingState.StateData;
                var handlersExecutedCounter = 0;


                Persist(eventsToPersist, @event =>
                {
                    handlersExecutedCounter++;
                    if (@event is TEvent)
                    {
                        nextData = ApplyEvent((TEvent) @event, nextData);
                    }
                    if (handlersExecutedCounter == eventsToPersist.Count)
                    {
                        base.ApplyState(upcomingState.Using(nextData));

                        if (upcomingState.AfterTransitionHandler != null)
                        {
                            upcomingState.AfterTransitionHandler(upcomingState.StateData);
                        }
                    }
                });
            }
        }
    }
}