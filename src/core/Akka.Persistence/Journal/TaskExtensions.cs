using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Journal
{
    internal static class TaskExtensions
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
    }
}