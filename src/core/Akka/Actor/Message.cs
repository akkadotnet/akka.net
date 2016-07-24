﻿//-----------------------------------------------------------------------
// <copyright file="Message.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// Envelope class, represents a message and the sender of the message.    
    /// </summary>
    public struct Envelope
    {
        public Envelope(object message, IActorRef sender, ActorSystem system)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            Message = message;
            Sender = sender != ActorRefs.NoSender ? sender : system.DeadLetters;
        }

        public Envelope(object message, IActorRef sender)
        {
            Message = message;
            Sender = sender;
        }

        /// <summary>
        /// Gets or sets the sender.
        /// </summary>
        /// <value>The sender.</value>
        public IActorRef Sender { get; private set; }

        /// <summary>
        /// Gets or sets the message.
        /// </summary>
        /// <value>The message.</value>
        public object Message { get; private set; }

        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender == ActorRefs.NoSender ? "NoSender" : Sender.ToString());
        }
    }
}

