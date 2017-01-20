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
        public TextMessageReceiver()
        {
            Receive<TextMessage>(x => Console.WriteLine($"{x.GetType().Name} {x.Body} from {x.SenderPath}"));
        }
    }

    public class PoliteResponder : ReceiveActor
    {
        public readonly IActorRef Mediator = DistributedPubSub.Get(Context.System).Mediator;
        public PoliteResponder()
        {
            Receive<ThankYou>(x =>
            {
                Console.WriteLine($"{x.Body} from {x.SenderPath}");
                Sender.Tell(new YouAreWelcome("Got it", Self.Path.ToStringWithoutAddress()));
                Mediator.Tell(new Publish(Topics.TextMessages.ToString(), new YouAreWelcome("Got it", Self.Path.ToStringWithoutAddress())));
            }, x => !Sender.Equals(Self));

            Receive<YouAreWelcome>(x =>
            {
                Console.WriteLine($"{x.Body} from {x.SenderPath}");
            }, x => !Sender.Equals(Self));
        }

        protected override void PreStart()
        {
            base.PreStart();
            //Mediator.Tell(new Subscribe(Topics.TextMessages.ToString(), Self));
        }

        protected override void PostStop()
        {
            base.PostStop();
            //Mediator.Tell(new Unsubscribe(Topics.TextMessages.ToString(), Self));
        }
    }
}
