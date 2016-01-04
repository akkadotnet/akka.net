using System;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Tools.PublishSubscribe;

namespace ClusterToolsExample.Shared
{
    public sealed class Echo
    {
        public readonly string Message;

        public Echo(string message)
        {
            Message = message;
        }
    }

    public class EchoReceiver : ReceiveActor
    {
        public const string Topic = "echo";

        public readonly Cluster Cluster = Cluster.Get(Context.System);
        public readonly IActorRef Mediator = DistributedPubSub.Get(Context.System).Mediator;

        public EchoReceiver()
        {
            Receive<Echo>(echo => Console.WriteLine(echo.Message));
            Receive<SubscribeAck>(ack =>
                Console.WriteLine("Actor [{0}] has subscribed to topic [{1}]", ack.Subscribe.Ref, ack.Subscribe.Topic));
        }

        protected override void PreStart()
        {
            base.PreStart();
            Mediator.Tell(new Subscribe(Topic, Self));
        }

        protected override void PostStop()
        {
            Mediator.Tell(new Unsubscribe(Topic, Self));
            base.PostStop();
        }
    }
}