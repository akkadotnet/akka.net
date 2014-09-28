namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL
    /// A stash implementation that is bounded
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public class BoundedStashImpl : AbstractStash
    {
        /// <summary>INTERNAL
        /// A stash implementation that is bounded
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        public BoundedStashImpl(IActorContext context, int capacity = 100)
            : base(context, capacity)
        {
        }
    }
}