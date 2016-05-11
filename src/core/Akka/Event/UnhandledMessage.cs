//-----------------------------------------------------------------------
// <copyright file="UnhandledMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Represents an UnhandledMessage that was not handled by the Recipient.
    /// </summary>
    public class UnhandledMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnhandledMessage" /> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="recipient">The recipient.</param>
        internal UnhandledMessage(object message, IActorRef sender, IActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        /// Gets the original message that could not be handled.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        /// Gets the sender of the message.
        /// </summary>
        /// <value>The sender of the message.</value>
        public IActorRef Sender { get; private set; }

        /// <summary>
        /// Gets the recipient of the message.
        /// </summary>
        /// <value>The recipient of the message.</value>
        public IActorRef Recipient { get; private set; }
    }
}

