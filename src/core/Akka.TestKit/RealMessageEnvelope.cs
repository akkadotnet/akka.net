//-----------------------------------------------------------------------
// <copyright file="RealMessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// An envelope containing a received message and its sender.
    /// </summary>
    public class RealMessageEnvelope : MessageEnvelope
    {
        private readonly object _message;
        private readonly IActorRef _sender;

        /// <summary>
        /// Creates an envelope for a message and its sender.
        /// </summary>
        /// <param name="message">The received message.</param>
        /// <param name="sender">The actor that sent the message.</param>
        public RealMessageEnvelope(object message, IActorRef sender)
        {
            _message = message;
            _sender = sender;
        }

        /// <summary>
        /// Gets the received message.
        /// </summary>
        public override object Message { get { return _message; } }
        /// <summary>
        /// Gets the actor that sent the message.
        /// </summary>
        public override IActorRef Sender{get { return _sender; }}

        /// <summary>
        /// Returns the message and sender in diagnostic form.
        /// </summary>
        /// <returns>A string containing the message and sender.</returns>
        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender == ActorRefs.NoSender ? "NoSender" : Sender.ToString());
        }
    }
}
