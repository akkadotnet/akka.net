using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    public abstract class MessageQueueMailbox : Mailbox
    {
        public abstract MessageQueue MessageQueue { get; }
    }
}
