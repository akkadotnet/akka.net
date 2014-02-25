namespace Akka.Actor
{
    /// <summary>
    /// An extension method class for working with ActorRefs
    /// </summary>
    public static class ActorRefExtensions
    {
        /// <summary>
        /// If we call a method such as <code>Context.Child(name)</code>
        /// and don't receive a valid result in return, this method will indicate
        /// whether or not the actor we received is valid.
        /// </summary>
        public static bool IsNobody(this ActorRef actorRef)
        {
            return actorRef == null || actorRef is Nobody || actorRef is NoSender || actorRef is DeadLetterActorRef;
        }
    }
}
