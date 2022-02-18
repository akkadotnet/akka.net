#region SampleSender
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;

namespace SampleSender
{
    public sealed class Sender: ReceiveActor
    {
        public Sender()
        {
            // activate the extension
            var mediator = DistributedPubSub.Get(Context.System).Mediator;

            Receive<string>(str =>
            {
                var upperCase = str.ToUpper();
                mediator.Tell(new Send(path: "/user/destination", message: upperCase, localAffinity: true));
            });
        }
    }
}
#endregion
