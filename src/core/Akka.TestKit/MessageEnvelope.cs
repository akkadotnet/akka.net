//-----------------------------------------------------------------------
// <copyright file="MessageEnvelope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.TestKit
{
    public abstract class MessageEnvelope   //this is called Message in Akka JVM
    {
        public abstract object Message { get; }

        public abstract IActorRef Sender { get; }
    }
}

