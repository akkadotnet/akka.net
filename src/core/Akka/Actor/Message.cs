//-----------------------------------------------------------------------
// <copyright file="Message.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// Envelope class, represents a message and the sender of the message.
    /// </summary>
    public struct Envelope
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Envelope"/> struct.
        /// </summary>
        /// <param name="message">The message being sent.</param>
        /// <param name="sender">The actor who sent the message.</param>
        /// <param name="system">The current actor system.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="message"/> is undefined.
        /// </exception>
        public Envelope(object message, IActorRef sender, ActorSystem system)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message), "The message cannot be null.");
            Sender = sender != ActorRefs.NoSender ? sender : system.DeadLetters;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Envelope"/> struct.
        /// </summary>
        /// <param name="message">The message being sent.</param>
        /// <param name="sender">The actor who sent the message.</param>
        public Envelope(object message, IActorRef sender)
        {
            Message = message;
            Sender = sender;
        }

        /// <summary>
        /// Gets or sets the sender.
        /// </summary>
        /// <value>The sender.</value>
        public IActorRef Sender { get; private set; }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        /// <summary>
        /// Converts the <see cref="Envelope"/> to a string representation.
        /// </summary>
        /// <returns>A string.</returns>
        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender == ActorRefs.NoSender ? "NoSender" : Sender.ToString());
        }
    }
}

