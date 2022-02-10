#region SampleSubscriber
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;

namespace SampleSubscriber
{
    public sealed class Subscriber: ReceiveActor
    {
        private readonly IActorRef _mediator;
        private readonly ILoggingAdapter _log;
        public Subscriber()
        {
            _log = Context.GetLogger();
            _mediator = DistributedPubSub.Get(Context.System).Mediator;
            _mediator.Tell(new Subscribe("content", Self));
            Receive<SubscribeAck>(ack => 
            {
                if (ack != null && ack.Subscribe.Topic == "content" && ack.Subscribe.Ref.Equals(Self))
                {
                    Become(Ready);
                }
            });
        }
        private void Ready()
        {
            Receive<string>(message => _log.Info("Got {0}", message));
        }
    }
}
#endregion
