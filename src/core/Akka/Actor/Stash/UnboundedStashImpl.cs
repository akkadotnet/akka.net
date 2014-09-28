using Akka.Actor.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// A stash implementation that is unbounded
    /// </summary>
    internal class UnboundedStashImpl : AbstractStash
    {
        public UnboundedStashImpl(IActorContext context)
            : base(context, int.MaxValue)
        {
        }
    }
}