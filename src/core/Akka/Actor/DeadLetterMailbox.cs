﻿//-----------------------------------------------------------------------
// <copyright file="DeadLetterMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    public class DeadLetterMailbox : Mailbox
    {
        private readonly IActorRef _deadLetters;

        public DeadLetterMailbox(IActorRef deadLetters)
        {
            _deadLetters = deadLetters;
        }

        public override void Post(IActorRef receiver, Envelope envelope)
        {
            var message = envelope.Message;
            if(message is ISystemMessage)
            {
                Mailbox.DebugPrint("DeadLetterMailbox forwarded system message " + envelope+ " as a DeadLetter");
                _deadLetters.Tell(new DeadLetter(message, receiver, receiver), receiver);
            }
            else if(message is DeadLetter)
            {
                //Just drop it like it's hot
                Mailbox.DebugPrint("DeadLetterMailbox dropped DeadLetter " + envelope);
            }
            else
            {
                Mailbox.DebugPrint("DeadLetterMailbox forwarded message " + envelope + " as a DeadLetter");
                var sender = envelope.Sender;
                _deadLetters.Tell(new DeadLetter(message, sender, receiver),sender);
            }
        }

        public override void BecomeClosed()
        {
            
        }

        public override bool IsClosed{get { return true; }}

        protected override int GetNumberOfMessages()
        {
            return 0;
        }

        protected override void Schedule()
        {
            //Intentionally left blank
        }

        public override void CleanUp()
        {
            //Intentionally left blank
        }
    }
}

