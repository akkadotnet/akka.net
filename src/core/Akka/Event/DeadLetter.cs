//-----------------------------------------------------------------------
// <copyright file="DeadLetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a message that could not be delivered to its recipient.
    /// </summary>
    public class DeadLetter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetter"/> class.
        /// </summary>
        /// <param name="message">The original message that could not be delivered.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message.</param>
        public DeadLetter(object message, IActorRef sender, IActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        /// The original message that could not be delivered.
        /// </summary>
        public object Message { get; private set; }

        /// <summary>
        /// The actor that was to receive the message.
        /// </summary>
        public IActorRef Recipient { get; private set; }

        /// <summary>
        /// The actor that sent the message.
        /// </summary>
        public IActorRef Sender { get; private set; }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return "DeadLetter from " + Sender + " to " + Recipient + ": <" + Message + ">";
        }
    }
}
