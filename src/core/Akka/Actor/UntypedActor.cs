//-----------------------------------------------------------------------
// <copyright file="UntypedActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// Class UntypedActor.
    /// </summary>
    public abstract class UntypedActor : ActorBase
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected sealed override bool Receive(object message)
        {
            OnReceive(message);
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        protected void RunTask(Action action)
        {
            ActorTaskScheduler.RunTask(action);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="action">TBD</param>
        protected void RunTask(Func<Task> action)
        {
            ActorTaskScheduler.RunTask(action);
        }

        /// <summary>
        /// To be implemented by concrete UntypedActor, this defines the behavior of the UntypedActor.
        /// This method is called for every message received by the actor.
        /// </summary>
        /// <param name="message">The message.</param>
        protected abstract void OnReceive(object message);

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void Become(UntypedReceive receive)
        {
            Context.Become(receive);
        }

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="IActorContext.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="IActorContext.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void BecomeStacked(UntypedReceive receive)
        {
            Context.BecomeStacked(receive);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected new static IUntypedActorContext Context => (IUntypedActorContext) ActorBase.Context;
    }
}
