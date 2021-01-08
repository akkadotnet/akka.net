//-----------------------------------------------------------------------
// <copyright file="MessageEnvelope.cs" company="Akka.NET Project">
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
    public abstract class MessageEnvelope   //this is called Message in Akka JVM
    {
        /// <summary>
        /// TBD
        /// </summary>
        public abstract object Message { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public abstract IActorRef Sender { get; }
    }
}
