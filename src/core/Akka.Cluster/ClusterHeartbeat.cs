using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{
    class ClusterHeartbeatReceiver : UntypedActor, IActorLogging
    {
        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }

        public LoggingAdapter Log { get; private set; }
    }
}
