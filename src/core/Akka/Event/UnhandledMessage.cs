//-----------------------------------------------------------------------
// <copyright file="UnhandledMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a message that was not handled by the recipient.
    /// </summary>
    public sealed class UnhandledMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="UnhandledMessage" /> class.
        /// </summary>
        /// <param name="message">The original message that could not be handled.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message.</param>
        public UnhandledMessage(object message, IActorRef sender, IActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        /// The original message that could not be handled.
        /// </summary>
        public object Message { get; private set; }

        /// <summary>
        /// The actor that sent the message.
        /// </summary>
        public IActorRef Sender { get; private set; }

        /// <summary>
        /// The actor that was to receive the message.
        /// </summary>
        public IActorRef Recipient { get; private set; }
    }
}
