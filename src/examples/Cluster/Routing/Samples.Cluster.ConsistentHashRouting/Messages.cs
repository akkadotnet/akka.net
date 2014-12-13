using System;
using Akka.Routing;

namespace Samples.Cluster.ConsistentHashRouting
{
    public class FrontendCommand : ConsistentHashable {
        public string Message { get; set; }

        public string JobId { get; set; }

        public object ConsistentHashKey { get { return JobId; } }
    }

    public class StartCommand
    {
        public StartCommand(string commandText)
        {
            CommandText = commandText;
        }

        public string CommandText { get; private set; }

        public override string ToString()
        {
            return CommandText;
        }
    }

    public class CommandComplete
    {
    }
}
