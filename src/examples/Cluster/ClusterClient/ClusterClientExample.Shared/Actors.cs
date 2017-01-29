using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;

namespace ClusterClientExample.Shared
{
    public class TextMessageReceiver : ReceiveActor
    {
        private readonly IActorRef _publisherRef;

        public TextMessageReceiver(IActorRef publisherRef)
        {
            _publisherRef = publisherRef;
            Receive<TextMessage>(x =>
            {
                var senderPath = x.PublisherRef.Equals(_publisherRef) ? "self" : x.PublisherRef.Path.ToStringWithUid();
                Console.WriteLine(
                    $"{x.GetType().Name} {x.Body} from {senderPath} at:{DateTime.UtcNow.ToLongTimeString()}");
            });
        }
    }

    public class PoliteResponder : ReceiveActor
    {
        public readonly IActorRef Mediator = DistributedPubSub.Get(Context.System).Mediator;
        public PoliteResponder()
        {
            Receive<ThankYou>(x =>
            {
                Console.WriteLine($"{x.Body} from {x.PublisherRef.Path.ToStringWithUid()} at:{DateTime.UtcNow.ToLongTimeString()}");
                Mediator.Tell(new Publish(Topics.TextMessages.ToString(), new YouAreWelcome("my pleasure", Self)));
            }, x => !Sender.Equals(Self));

            Receive<YouAreWelcome>(x =>
            {
                Console.WriteLine($"{x.Body} from {x.PublisherRef.Path.ToStringWithUid()} at:{DateTime.UtcNow.ToLongTimeString()}");
            }, x => !Sender.Equals(Self));
        }
    }
}
