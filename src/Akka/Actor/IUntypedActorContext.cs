namespace Akka.Actor
{
    public interface IUntypedActorContext : IActorContext
    {
        void Become(UntypedReceive receive, bool discardOld = true);
    }
}