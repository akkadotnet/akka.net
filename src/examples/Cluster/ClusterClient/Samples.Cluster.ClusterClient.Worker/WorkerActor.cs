using Akka.Actor;
using Akka.Event;
using Samples.Cluster.ClusterClient.Messages;

namespace Samples.Cluster.ClusterClient.Worker
{
    public class WorkerActor : ReceiveActor
    {
        private readonly Akka.Cluster.Cluster _cluster = Akka.Cluster.Cluster.Get(Context.System);
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public WorkerActor()
        {
            Receive<Ping>(p =>
            {
                _log.Info("[{0}] Received [{1}] from {2}", _cluster.SelfAddress, p.Msg, Sender);
                Sender.Tell(new Pong(p.Msg, _cluster.SelfAddress));
            });
        }
    }
}