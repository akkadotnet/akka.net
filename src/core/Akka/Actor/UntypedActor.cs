﻿//-----------------------------------------------------------------------
// <copyright file="UntypedActor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        protected sealed override bool Receive(object message)
        {
            OnReceive(message);
            return true;
        }

        protected void RunTask(Action action)
        {
            ActorTaskScheduler.RunTask(action);
        }

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

        [Obsolete("Use Become or BecomeStacked instead. This method will be removed in future versions")]
        protected void Become(UntypedReceive receive, bool discardOld = true)
        {
            if (discardOld)
                Context.Become(receive);
            else
                Context.BecomeStacked(receive);
        }

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
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="IUntypedActorContext.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="IUntypedActorContext.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        protected void BecomeStacked(UntypedReceive receive)
        {
            Context.BecomeStacked(receive);
        }

        protected static new IUntypedActorContext Context { get { return (IUntypedActorContext) ActorBase.Context; } }
    }
}

