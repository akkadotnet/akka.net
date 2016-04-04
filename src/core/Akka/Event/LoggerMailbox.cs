// <copyright file="LoggerMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Event
{
    public class LoggerMailbox : UnboundedMailbox
    {
        public override void CleanUp()
        {
            if (HasMessages)
            {
                Envelope envelope;

                // Drain all remaining messages to the StandardOutLogger.
                // CleanUp is called after switching out the mailbox, which is why
                // this kind of look works without a limit.
                while (TryDequeue(out envelope))
                {
                    // Logging.StandardOutLogger is a MinimalActorRef, i.e. not a "real" actor
                    Logging.StandardOutLogger.Tell(envelope.Message, envelope.Sender);
                }
            }
            base.CleanUp();
        }
    }
}