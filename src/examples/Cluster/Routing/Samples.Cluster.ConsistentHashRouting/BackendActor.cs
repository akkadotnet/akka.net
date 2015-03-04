using System;
using Akka.Actor;
using Akka.Event;

namespace Samples.Cluster.ConsistentHashRouting
{
    public class BackendActor : UntypedActor
    {
        protected Akka.Cluster.Cluster Cluster = Akka.Cluster.Cluster.Get(Context.System);

        protected override void OnReceive(object message)
        {
            var command = message as FrontendCommand;
            if (command != null)
            {
                Console.WriteLine("Backend [{0}]: Received command {1} for job {2} from {3}", Cluster.SelfAddress, command.Message, command.JobId, Sender);
                Sender.Tell(new CommandComplete());
            }
            else
            {
                Unhandled(message);
            }
        }
    }
}
