namespace Akka.Actor
{
    /// <summary>
    /// Interface IUntypedActorContext
    /// </summary>
    public interface IUntypedActorContext : IActorContext
    {
        /// <summary>
        /// Becomes the specified receive.
        /// </summary>
        /// <param name="receive">The receive.</param>
        /// <param name="discardOld">if set to <c>true</c> [discard old].</param>
        void Become(UntypedReceive receive, bool discardOld = true);
    }
}