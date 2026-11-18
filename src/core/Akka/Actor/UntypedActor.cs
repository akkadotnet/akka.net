//-----------------------------------------------------------------------
// <copyright file="UntypedActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Routes every message to <see cref="OnReceive"/> and reports it as handled.
        /// Derived actors that need to report unhandled messages must call <see cref="ActorBase.Unhandled"/> themselves.
        /// </summary>
        /// <param name="message">The message received by the actor.</param>
        /// <returns>Always <c>true</c>, regardless of whether <see cref="OnReceive"/> processes the message.</returns>
        protected sealed override bool Receive(object message)
        {
            OnReceive(message);
            return true;
        }

        /// <summary>
        /// Runs an action using Akka's actor task scheduler.
        /// </summary>
        /// <param name="action">The action to schedule.</param>
        protected void RunTask(Action action)
        {
            ActorTaskScheduler.RunTask(action);
        }

        /// <summary>
        /// Runs an asynchronous operation using Akka's actor task scheduler.
        /// </summary>
        /// <param name="action">The asynchronous operation to schedule.</param>
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
        /// The untyped actor context for the current actor invocation.
        /// </summary>
        protected new static IUntypedActorContext Context => (IUntypedActorContext) ActorBase.Context;
    }
}
