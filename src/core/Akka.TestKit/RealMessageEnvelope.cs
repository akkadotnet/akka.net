//-----------------------------------------------------------------------
// <copyright file="RealMessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    public class RealMessageEnvelope : MessageEnvelope
    {
        private readonly object _message;
        private readonly IActorRef _sender;

        public RealMessageEnvelope(object message, IActorRef sender)
        {
            _message = message;
            _sender = sender;
        }

        public override object Message { get { return _message; } }
        public override IActorRef Sender{get { return _sender; }}

        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender == ActorRefs.NoSender ? "NoSender" : Sender.ToString());
        }
    }
}

