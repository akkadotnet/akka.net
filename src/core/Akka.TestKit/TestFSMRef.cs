//-----------------------------------------------------------------------
// <copyright file="TestFSMRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <summary>
        /// Creates a test reference for an FSM actor.
        /// </summary>
        /// <param name="system">The actor system that owns the FSM.</param>
        /// <param name="props">The properties used to create the FSM actor.</param>
        /// <param name="supervisor">The supervising actor, or null to use the system guardian.</param>
        /// <param name="name">The actor name, or null to generate a unique name.</param>
        /// <param name="activateLogging">Whether to enable the FSM's transition logging.</param>
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
        /// <param name="stateData">The new state data.</param>
        /// <param name="timeout">The state timeout to apply, or null to use the FSM's normal timeout selection.</param>
        public void SetStateData(TData stateData, TimeSpan? timeout = null)
        {
            SetState(UnderlyingActor.StateName, stateData, timeout);
        }


        /// <summary>
        /// Change FSM state timeout. This method is directly equivalent to a
        /// transition initiated from within the FSM using the current state name and data
        /// but with the specified timeout.
        /// </summary>
        /// <param name="timeout">The new state timeout.</param>
        public void SetStateTimeout(TimeSpan timeout)
        {
            SetState(UnderlyingActor.StateName, UnderlyingActor.StateData, timeout);
        }

        /// <summary>
        /// Change FSM state; but keeps the current state data. 
        /// This method is directly equivalent to a  transition initiated from within the FSM.
        /// </summary>
        /// <param name="stateName">The new state name.</param>
        /// <param name="timeout">The state timeout to apply, or null to use the FSM's normal timeout selection.</param>
        public void SetState(TState stateName, TimeSpan? timeout = null)
        {
            SetState(stateName, UnderlyingActor.StateData, timeout);
        }

        /// <summary>
        /// Change FSM state. This method is directly equivalent to a
        /// corresponding transition initiated from within the FSM, including timeout
        /// and stop handling.
        /// </summary>
        /// <param name="stateName">The new state name.</param>
        /// <param name="stateData">The new state data.</param>
        /// <param name="timeout">The state timeout to apply, or null to use the FSM's normal timeout selection.</param>
        /// <param name="stopReason">The stop reason to apply, or null if the FSM should continue running.</param>
        public void SetState(TState stateName, TData stateData, TimeSpan? timeout = null, FSMBase.Reason stopReason = null)
        {
            var fsm = ((IInternalSupportsTestFSMRef<TState, TData>)UnderlyingActor);
            InternalRef.Cell.UseThreadContext(() => fsm.ApplyState(new FSMBase.State<TState, TData>(stateName, stateData, timeout, stopReason)));
        }

        /// <summary>
        /// Proxy for <see cref="FSM{TState,TData}.SetTimer"/>
        /// </summary>
        /// <param name="name">The timer name used to identify and cancel this timer.</param>
        /// <param name="msg">The message sent to the FSM when the timer expires.</param>
        /// <param name="timeout">The delay before the timer sends its message.</param>
        /// <param name="repeat">Whether the timer repeats at the specified interval.</param>
        public void SetTimer(string name, object msg, TimeSpan timeout, bool repeat = false)
        {
            InternalRef.Cell.UseThreadContext(() => UnderlyingActor.SetTimer(name, msg, timeout, repeat));
        }

        /// <summary>
        /// Proxy for <see cref="FSM{TState,TData}.CancelTimer"/>
        /// </summary>
        /// <param name="name">The name of the timer to cancel.</param>
        public void CancelTimer(string name)
        {
            UnderlyingActor.CancelTimer(name);
        }

        /// <summary>
        /// Proxy for <see cref="FSM{TState,TData}.IsTimerActive"/>
        /// </summary>
        /// <param name="name">The name of the timer to check.</param>
        /// <returns><c>true</c> if a timer with this name is active; otherwise, <c>false</c>.</returns>
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
