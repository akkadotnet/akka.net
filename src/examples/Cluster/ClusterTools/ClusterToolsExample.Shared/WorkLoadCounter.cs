using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace ClusterToolsExample.Shared
{
    public class WorkLoadCounter : ReceiveActor
    {
        public WorkLoadCounter()
        {
            var log = Context.GetLogger();
            var counts = new Dictionary<IActorRef, int>();

            Receive<Result>(_ =>
            {
                int count;
                if (counts.TryGetValue(Sender, out count))
                    counts[Sender] = (++count);
                else
                    counts.Add(Sender, 1);
            });

            Receive<SendReport>(_ => Sender.Tell(new Report(counts)));
        }
    }
}