//-----------------------------------------------------------------------
// <copyright file="DeadLetter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///  Use with caution: Messages extending this trait will not be logged by the default dead-letters listener.
    /// Instead they will be wrapped as <see cref="SuppressedDeadLetter"/> and may be subscribed for explicitly.
    /// </summary>
    public interface IDeadLetterSuppression
    {

    }

    /// <summary>
    /// Represents a message that could not be delivered to it's recipient. 
    /// This message wraps the original message, the sender and the intended recipient of the message.
    /// </summary>
    public abstract class AllDeadLetters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetter"/> class.
        /// </summary>
        /// <param name="message">The original message that could not be delivered.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message.</param>
        protected AllDeadLetters(object message, IActorRef sender, IActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        /// <summary>
        /// The original message that could not be delivered.
        /// </summary>
        public object Message { get; }

        /// <summary>
        /// The actor that was to receive the message.
        /// </summary>
        public IActorRef Recipient { get; }

        /// <summary>
        /// The actor that sent the message.
        /// </summary>
        public IActorRef Sender { get; }

        /// <summary>
        /// Returns a <see cref="System.String" /> that represents this instance.
        /// </summary>
        /// <returns>A <see cref="System.String" /> that represents this instance.</returns>
        public override string ToString()
        {
            return $"DeadLetter from {Sender} to {Recipient}: <{Message}>";
        }
    }

    /// <summary>
    /// When a message is sent to an Actor that is terminated before receiving the message, it will be sent as a DeadLetter
    /// to the ActorSystem's EventStream
    /// </summary>
    public sealed class DeadLetter : AllDeadLetters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetter"/> class.
        /// </summary>
        /// <param name="message">The original message that could not be delivered.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when either the sender or the recipient is undefined.
        /// </exception>
        public DeadLetter(object message, IActorRef sender, IActorRef recipient) : base(message, sender, recipient)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender), "DeadLetter sender may not be null");
            if (recipient == null) throw new ArgumentNullException(nameof(recipient), "DeadLetter recipient may not be null");
        }
    }

    /// <summary>
    /// Similar to <see cref="DeadLetter"/> with the slight twist of NOT being logged by the default dead letters listener.
    /// Messages which end up being suppressed dead letters are internal messages for which ending up as dead-letter is both expected and harmless.
    /// </summary>
    public sealed class SuppressedDeadLetter : AllDeadLetters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SuppressedDeadLetter"/> class.
        /// </summary>
        /// <param name="message">The original message that could not be delivered.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when either the sender or the recipient is undefined.
        /// </exception>
        public SuppressedDeadLetter(IDeadLetterSuppression message, IActorRef sender, IActorRef recipient) : base(message, sender, recipient)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender), "SuppressedDeadLetter sender may not be null");
            if (recipient == null) throw new ArgumentNullException(nameof(recipient), "SuppressedDeadLetter recipient may not be null");
        }
    }
}
