using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{

    internal class ClusterHeartbeatSender : UntypedActor
    {
        public ClusterHeartbeatSender()
        {
            throw new NotImplementedException();
        }

        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }
    }

    class ClusterHeartbeatReceiver : UntypedActor, IActorLogging
    {
        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }

        public LoggingAdapter Log { get; private set; }
    }
}
