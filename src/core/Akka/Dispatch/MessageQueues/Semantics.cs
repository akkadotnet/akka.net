using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Dispatch.MessageQueues
{
    public interface MultipleConsumerSemantics
    {
    }

    public interface UnboundedMessageQueueSemantics
    {        
    }

    public interface BoundedMessageQueueSemantics
    {
        TimeSpan PushTimeOut { get; set; }
    }
}
