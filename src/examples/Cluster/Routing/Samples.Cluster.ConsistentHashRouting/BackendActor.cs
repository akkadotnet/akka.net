using System;
using Akka.Actor;
using Akka.Event;

namespace Samples.Cluster.ConsistentHashRouting
{
    public class BackendActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            if (message is FrontendCommand)
            {
                var command = message as FrontendCommand;
                Console.WriteLine("Backend [{0}]: Received command {1} for job {2} from {3}", Self, command.Message, command.JobId, Sender);
                Sender.Tell(new CommandComplete());
            }
            else
            {
                Unhandled(message);
            }
        }
    }
}
