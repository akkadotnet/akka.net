//-----------------------------------------------------------------------
// <copyright file="UnhandledMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This message is published to the EventStream whenever an Actor receives a message it doesn't understand
    /// </summary>
    public sealed class UnhandledMessage : AllDeadLetters, IWrappedMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnhandledMessage" /> class.
        /// </summary>
        /// <param name="message">The original message that could not be handled.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message.</param>
        public UnhandledMessage(object message, IActorRef sender, IActorRef recipient)
            : base(message, sender, recipient)
        {
        }
    }
}
