using Akka.Actor;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used for <see cref="MessageQueue"/> instances that support double-ended queues.
    /// </summary>
    public interface IDequeBasedMailbox
    {
        /// <summary>
        /// Enqueues an <see cref="Envelope"/> to the front of
        /// the <see cref="MessageQueue"/>. Typically called during
        /// a <see cref="IStash.Unstash"/> or <see cref="IStash.UnstashAll()"/>operation.
        /// </summary>
        /// <param name="envelope">The message that will be prepended to the queue.</param>
        void EnqueueFirst(Envelope envelope);

        /// <summary>
        /// Posts a message to the back of the <see cref="MessageQueue"/>
        /// </summary>
        /// <param name="receiver">The intended recipient of the message.</param>
        /// <param name="envelope">The message that will be appended to the queue.</param>
        void Post(IActorRef receiver, Envelope envelope);
    }
}
