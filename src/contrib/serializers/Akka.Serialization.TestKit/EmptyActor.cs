using Akka.Actor;

namespace Akka.Tests.Serialization
{
    public class EmptyActor : UntypedActor
    {
        protected override void OnReceive(object message)
        {
            Context.System.EventStream.Publish(Sender);
        }
    }
}