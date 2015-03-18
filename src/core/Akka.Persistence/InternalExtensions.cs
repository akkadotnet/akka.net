using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;

namespace Akka.Persistence
{
    internal static class InternalExtensions
    {
        /// <summary>
        /// Sends <paramref name="task"/> result to the <paramref name="receiver"/> in form of <see cref="ReplayMessagesSuccess"/> 
        /// or <see cref="ReplayMessagesFailure"/> depending on the success or failure of the task.
        /// </summary>
        public static Task NotifyAboutReplayCompletion(this Task task, ActorRef receiver)
        {
            return task
                .ContinueWith(t => !t.IsFaulted ? (object) ReplayMessagesSuccess.Instance : new ReplayMessagesFailure(t.Exception))
                .PipeTo(receiver);
        }

        /// <summary>
        /// Enqueues provided <paramref name="message"/> at the beggining of the internal actor cell mailbox message queue.
        /// Requires current actor to use unbounded deque-based message queue. It will fail otherwise.
        /// </summary>
        public static void EnqueueMessageFirst(this IActorContext context, object message)
        {
            var cell = (ActorCell)context;
            var mailbox = (Mailbox<UnboundedMessageQueue, UnboundedDequeMessageQueue>)cell.Mailbox;
            var queue = (UnboundedDequeBasedMessageQueueSemantics)mailbox.MessageQueue;
            queue.EnqueueFirst(new Envelope { Sender = context.Sender, Message = message });
        }
    }
}