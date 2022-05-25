#region SampleDestination
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Event;

namespace SampleDestination
{
    public sealed class Destination : ReceiveActor
    {
        private readonly ILoggingAdapter log = Context.GetLogger();

        public Destination()
        {
            // activate the extension
            var mediator = DistributedPubSub.Get(Context.System).Mediator;

            // register to the path
            mediator.Tell(new Put(Self));

            Receive<string>(s =>
            {
                log.Info($"Got {s}");
            });
        }
    }
    #endregion
}
