//-----------------------------------------------------------------------
// <copyright file="RealMessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private readonly object _message;
        private readonly IActorRef _sender;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public RealMessageEnvelope(object message, IActorRef sender)
        {
            _message = message;
            _sender = sender;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override object Message { get { return _message; } }
        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRef Sender{get { return _sender; }}

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
