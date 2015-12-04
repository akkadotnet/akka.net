using Akka.Actor;
using Akka.Event;

namespace ClusterToolsExample.Shared
{
    public class Worker : ReceiveActor
    {
        public Worker(IActorRef counter)
        {
            var log = Context.GetLogger();

            Receive<Work>(work =>
            {
                var result = new Result(work.Id);
                log.Info("Worker {0} - result: {1}", Self.Path.Name, result.Id);
                Sender.Tell(result);
                counter.Tell(result);
            });
        }
    }
}