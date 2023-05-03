//-----------------------------------------------------------------------
// <copyright file="RealMessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    /// <summary>
    /// TBD
    /// </summary>
    public class RealMessageEnvelope : MessageEnvelope
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public RealMessageEnvelope(object message, IActorRef sender)
        {
            Message = message;
            Sender = sender;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override object Message { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRef Sender { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender == ActorRefs.NoSender ? "NoSender" : Sender.ToString());
        }
    }
}
