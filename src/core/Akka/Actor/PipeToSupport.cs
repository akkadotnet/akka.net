using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Creates the PipeTo pattern for automatically sending the results of completed tasks
    /// into the inbox of a designated Actor
    /// </summary>
    public static class PipeToSupport
    {
        /// <summary>
        /// Pipes the output of a Task directly to the <see cref="recipient"/>'s mailbox once
        /// the task completes
        /// </summary>
        public static Task PipeTo<T>(this Task<T> taskToPipe, ICanTell recipient, IActorRef sender = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            return taskToPipe.ContinueWith(tresult =>
            {
                if(tresult.IsCanceled  || tresult.IsFaulted)
                    recipient.Tell(new Status.Failure(tresult.Exception), sender);
                else if (tresult.IsCompleted)
                    recipient.Tell(tresult.Result, sender);
            }, TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.AttachedToParent);
        }
    }
}
