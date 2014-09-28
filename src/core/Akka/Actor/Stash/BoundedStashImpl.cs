namespace Akka.Actor
{
    /// <summary>
    /// A stash implementation that is bounded
    /// </summary>
    internal class BoundedStashImpl : AbstractStash
    {
        public BoundedStashImpl(IActorContext context, int capacity = 100)
            : base(context, capacity)
        {
        }
    }
}