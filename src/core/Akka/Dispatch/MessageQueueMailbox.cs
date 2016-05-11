//-----------------------------------------------------------------------
// <copyright file="MessageQueueMailbox.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    public abstract class MessageQueueMailbox : Mailbox
    {
        public abstract IMessageQueue MessageQueue { get; }
    }
}

