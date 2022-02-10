#region SamplePublisher
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;

namespace SamplePublisher
{
    public sealed class Publisher: ReceiveActor
    {
        public Publisher()
        {
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            Receive<string>(input => mediator.Tell(new Publish("content", input.ToUpperInvariant())));
        }
    }
}
#endregion
