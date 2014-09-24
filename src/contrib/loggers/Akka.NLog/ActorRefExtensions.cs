using Akka.Actor;

namespace Akka.NLog
{
    public static class ActorRefExtensions
    {
        /// <summary>
        /// Simple generic extension method, enabling the use of Tell generically.
        /// </summary>
        /// <typeparam name="T">Event's type. Needs to have a parameterless constructor</typeparam>
        /// <param name="actor">Target actor reference</param>
        public static void Tell<T>(this ActorRef actor)
            where T : class, new()
        {
            actor.Tell(new T());
        }
    }
}