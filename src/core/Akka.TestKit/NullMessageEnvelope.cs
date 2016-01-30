//-----------------------------------------------------------------------
// <copyright file="NullMessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    public sealed class NullMessageEnvelope : MessageEnvelope
    {
        public static NullMessageEnvelope Instance=new NullMessageEnvelope();

        private NullMessageEnvelope(){}

        public override object Message
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        public override IActorRef Sender
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        public override string ToString()
        {
            return "<null>";
        }
    }
}

