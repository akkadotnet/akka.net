using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Messaging
{
    public abstract class BuiltInMessage
    {
    }

    public class SuperviceMe : BuiltInMessage
    {
        public Exception Reason { get; set; }
    }

    public class ActorAction : BuiltInMessage
    {
        public Action Action { get; set; }
    }

    public class Ping : BuiltInMessage
    {
        public DateTime LocalUtcNow { get; set; }
    }

    public class Pong : BuiltInMessage
    {
        public DateTime LocalUtcNow { get; set; }
        public DateTime RemoteUtcNow { get; set; }
    }

    public class Kill : BuiltInMessage
    {
    }

    public class Restart : BuiltInMessage
    {
    }

    public class Resume : BuiltInMessage
    {
    }

    public class Stop : BuiltInMessage
    {
    }

    public class Escalate : BuiltInMessage
    {
        public Exception Reason { get; set; }
    }
}
