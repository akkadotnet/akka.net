//-----------------------------------------------------------------------
// <copyright file="DeadLetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Represents a message that could not be delivered to it's recipient. 
    /// This message wraps the original message, the sender and the intended recipient of the message.
    /// </summary>
    public class DeadLetter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetter"/> class.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="sender">The sender.</param>
        /// <param name="recipient">The recipient.</param>
        public DeadLetter(object message, IActorRef sender, IActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        /// Gets the original message that could not be delivered.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        /// Gets the recipient of the message.
        /// </summary>
        /// <value>The recipient of the message.</value>
        public IActorRef Recipient { get; private set; }

        /// <summary>
        /// Gets the sender of the message.
        /// </summary>
        /// <value>The sender of the message.</value>
        public IActorRef Sender { get; private set; }

        public override string ToString()
        {
            return "DeadLetter from " + Sender + " to " + Recipient + ": <" + Message + ">";
        }
    }
}

