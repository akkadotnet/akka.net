using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Dispatch.MessageQueues
{

    /// <summary>
    /// Semantics for message queues that support multiple consumers
    /// </summary>
    public interface MultipleConsumerSemantics
    {
    }

    /// <summary>
    /// Semantics for message queues that have an unbounded size
    /// </summary>
    public interface UnboundedMessageQueueSemantics
    {        
    }

    /// <summary>
    /// Semantics for message queues that have a bounded size
    /// </summary>
    public interface BoundedMessageQueueSemantics
    {
        /// <summary>
        /// The enqueue time to wait until message is dropped to deadletters if the message queue is full
        /// </summary>
        TimeSpan PushTimeOut { get; set; }
    }
}
