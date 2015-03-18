namespace Akka.Actor
{
    public interface ICanTell
    {
        void Tell(object message, ActorRef sender);
    }
}
