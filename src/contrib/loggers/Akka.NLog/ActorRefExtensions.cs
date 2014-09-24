using Akka.Actor;

namespace Akka.NLog
{
    public static class ActorRefExtensions
    {
        public static void Tell<T>(this ActorRef actor)
            where T : class, new()
        {
            actor.Tell(new T());
        }
    }
}