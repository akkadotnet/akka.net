//-----------------------------------------------------------------------
// <copyright file="IUntypedActorContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// Interface IUntypedActorContext
    /// </summary>
    public interface IUntypedActorContext : IActorContext
    {
        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        void Become(UntypedReceive receive);

        /// <summary>
        /// Changes the actor's behavior and replaces the current receive handler with the specified handler.
        /// The current handler is stored on a stack, and you can revert to it by calling <see cref="IActorContext.UnbecomeStacked"/>
        /// <remarks>Please note, that in order to not leak memory, make sure every call to <see cref="BecomeStacked"/>
        /// is matched with a call to <see cref="IActorContext.UnbecomeStacked"/>.</remarks>
        /// </summary>
        /// <param name="receive">The new message handler.</param>
        void BecomeStacked(UntypedReceive receive);
    }
}

