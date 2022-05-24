#region headless-actor

using Akka.Actor;

namespace AkkaHeadlesssService
{
    internal class HeadlessActor : ReceiveActor
    {
        // Things that are possible with this actor:
        // Connect to an Apache Kafka, Apache Pulsar, RabbitMQ instance or send/receive messages to/from a remote actor!
        public HeadlessActor()
        {

        }
        public static Props Prop()
        {
            return Props.Create<HeadlessActor>();
        }
    }
}
#endregion
