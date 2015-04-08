namespace Akka.Actor
{
    public interface ICanTell
    {
        void Tell(object message, IActorRef sender);
    }
}
