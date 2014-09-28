using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// Lets the <see cref="StashFactory"/> know that this Actor needs stash support
    /// with unrestricted storage capacity
    /// </summary>
// ReSharper disable once InconsistentNaming
    public interface WithUnboundedStash : IActorStash, RequiresMessageQueue<UnboundedDequeBasedMessageQueueSemantics>
    {
        IStash CurrentStash { get; set; }
    }
}