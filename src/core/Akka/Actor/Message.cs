//-----------------------------------------------------------------------
// <copyright file="Message.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    //Note: this is a struct in order to lower GC pressure, it will be removed once the mailbox Run call goes out of scope. //Roger

    /// <summary>
    ///     Envelope class, represents a message and the sender of the message.    
    /// </summary>
    public struct Envelope
    {
        /// <summary>
        ///     Gets or sets the sender.
        /// </summary>
        /// <value>The sender.</value>
        public IActorRef Sender { get; set; }

        /// <summary>
        ///     Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; set; }

        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender == ActorRefs.NoSender ? "NoSender" : Sender.ToString());
        }
    }
}

