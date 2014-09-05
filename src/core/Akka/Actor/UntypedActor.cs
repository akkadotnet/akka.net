using System;

namespace Akka.Actor
{
    /// <summary>
    /// Class UntypedActor.
    /// </summary>
    public abstract class UntypedActor : ActorBase
    {
        protected sealed override bool Receive(object message)
        {
            OnReceive(message);
            return true;
        }

        /// <summary>
        /// To be implemented by concrete UntypedActor, this defines the behavior of the UntypedActor.
        /// This method is called for every message received by the actor.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void OnReceive(object message);

        /// <summary>
        /// Changes the Actor's behavior to become the new <see cref="Receive"/> handler.
        /// This method acts upon the behavior stack as follows:
        /// <para>if <paramref name="discardOld"/>==<c>true</c> it will replace the current behavior (i.e. the top element)</para>
        /// <para>if <paramref name="discardOld"/>==<c>false</c> it will keep the current behavior and push the given one atop</para>
        /// The default of replacing the current behavior on the stack has been chosen to avoid memory
        /// leaks in case client code is written without consulting this documentation first (i.e.
        /// always pushing new behaviors and never issuing an <see cref="ActorBase.Unbecome"/>)
        /// </summary>
        /// <param name="receive">The receive delegate.</param>
        /// <param name="discardOld">If <c>true</c> it will replace the current behavior; 
        /// otherwise it will keep the current behavior and it can be reverted using <see cref="ActorBase.Unbecome"/></param>
        protected void Become(UntypedReceive receive, bool discardOld = true)
        {
            Context.Become(receive, discardOld);
        }

        protected static new IUntypedActorContext Context { get { return (IUntypedActorContext) ActorBase.Context; } }
    }
}