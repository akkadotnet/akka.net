//-----------------------------------------------------------------------
// <copyright file="DeadLetterWithNoSubscribers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster.Tools.PublishSubscribe
{
    /// <summary>
    /// Special case of Dead Letter that explicitly indicates the message was sent to
    /// DeadLetters because there were no subscribers for the topic in DistributedPubSub,
    /// NOT because the mediator itself is dead.
    /// </summary>
    internal sealed class DeadLetterWithNoSubscribers : AllDeadLetters
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeadLetterWithNoSubscribers"/> class.
        /// </summary>
        /// <param name="message">The original message that could not be delivered.</param>
        /// <param name="topic">The topic that the message was sent to.</param>
        /// <param name="sender">The actor that sent the message.</param>
        /// <param name="recipient">The actor that was to receive the message (usually the mediator itself).</param>
        public DeadLetterWithNoSubscribers(object message, string? topic, IActorRef sender, IActorRef recipient) 
            : base(message, sender, recipient)
        {
            Topic = topic;
        }

        public string? Topic { get; }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            return $"DeadLetterWithNoSubscribers from {Sender} to {Recipient}: <{Message}> - No subscribers found for topic {Topic}";
        }
    }
}