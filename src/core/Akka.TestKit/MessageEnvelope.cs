//-----------------------------------------------------------------------
// <copyright file="MessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// Represents a received message together with the actor that sent it.
    /// </summary>
    public abstract class MessageEnvelope   //this is called Message in Akka JVM
    {
        /// <summary>
        /// Gets the received message.
        /// </summary>
        public abstract object Message { get; }

        /// <summary>
        /// Gets the sender of the received message.
        /// </summary>
        public abstract IActorRef Sender { get; }
    }
}
