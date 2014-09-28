using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// Lets the <see cref="StashFactory"/> know that this Actor needs stash support
    /// with restricted storage capacity
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface WithBoundedStash : IActorStash, RequiresMessageQueue<BoundedDequeBasedMessageQueueSemantics>
    { }
}