//-----------------------------------------------------------------------
// <copyright file="TestFSMRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Actor.Internal;

namespace Akka.TestKit
{
    /// <summary>
    /// This is a specialized form of the <see cref="TestActorRef{TActor}"/> with support for querying and
    /// setting the state of a <see cref="FSM{TState,TData}"/>. 
    /// </summary>
    /// <typeparam name="TActor">The type of the actor.</typeparam>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <typeparam name="TData">The type of the data.</typeparam>
    public class TestFSMRef<TActor, TState, TData> : TestActorRefBase<TActor> where TActor : FSM<TState, TData>
    {
        public TestFSMRef(ActorSystem system, Props props, IActorRef supervisor = null, string name = null, bool activateLogging = false)
            : base(system, props, supervisor, name)
        {
            if(activateLogging)
                Tell(InternalActivateFsmLogging.Instance, this);
        }

        /// <summary>Get current state name of this FSM.</summary>
        public TState StateName { get { return UnderlyingActor.StateName; } }

        /// <summary>Get current state data of this FSM.</summary>
        public TData StateData { get { return UnderlyingActor.StateData; } }


        /// <summary>
        /// Change FSM state data; but do not transition to a new state name. 
        /// This method is directly equivalent to a transition initiated from within the FSM.
        /// </summary>
        public void SetStateData(TData stateData, TimeSpan? timeout = null)
        {
            SetState(UnderlyingActor.StateName, stateData, timeout);
        }


        /// <summary>
        /// Change FSM state timeout. This method is directly equivalent to a
        /// transition initiated from within the FSM using the current state name and data
        /// but with the specified timeout.
        /// </summary>
        public void SetStateTimeout(TimeSpan timeout)
        {
            SetState(UnderlyingActor.StateName, UnderlyingActor.StateData, timeout);
        }

        /// <summary>
        /// Change FSM state; but keeps the current state data. 
        /// This method is directly equivalent to a  transition initiated from within the FSM.
        /// </summary>
        public void SetState(TState stateName, TimeSpan? timeout = null)
        {
            SetState(stateName, UnderlyingActor.StateData, timeout);
        }

        /// <summary>
        /// Change FSM state. This method is directly equivalent to a
        /// corresponding transition initiated from within the FSM, including timeout
        /// and stop handling.
        /// </summary>
        public void SetState(TState stateName, TData stateData, TimeSpan? timeout = null, FSMBase.Reason stopReason = null)
        {
            var fsm = ((IInternalSupportsTestFSMRef<TState, TData>)UnderlyingActor);
            InternalRef.Cell.UseThreadContext(() => fsm.ApplyState(new FSMBase.State<TState, TData>(stateName, stateData, timeout, stopReason)));
        }

        /// <summary>
        /// Proxy for <see cref="FSM{TState,TData}.SetTimer"/>
        /// </summary>
        public void SetTimer(string name, object msg, TimeSpan timeout, bool repeat = false)
        {
            InternalRef.Cell.UseThreadContext(() => UnderlyingActor.SetTimer(name, msg, timeout, repeat));
        }

        /// <summary>
        /// Proxy for <see cref="FSM{TState,TData}.CancelTimer"/>
        /// </summary>
        public void CancelTimer(string name)
        {
            UnderlyingActor.CancelTimer(name);
        }

        /// <summary>
        /// Proxy for <see cref="FSM{TState,TData}.IsTimerActive"/>
        /// </summary>
        public bool IsTimerActive(string name)
        {
            return UnderlyingActor.IsTimerActive(name);
        }


        /// <summary>
        /// Determines whether the FSM has a active state timer active.
        /// </summary>
        /// <returns><c>true</c> if the FSM has a active state timer active; <c>false</c> otherwise</returns>
        public bool IsStateTimerActive()
        {
            var fsm = ((IInternalSupportsTestFSMRef<TState, TData>)UnderlyingActor);
            return fsm.IsStateTimerActive;
        }
    }

}

