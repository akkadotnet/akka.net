using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Class FutureActor.
    /// </summary>
    public class FutureActor : ActorBase
    {
        private ActorRef respondTo;
        private TaskCompletionSource<object> result;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FutureActor" /> class.
        /// </summary>
        public FutureActor()
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FutureActor" /> class.
        /// </summary>
        /// <param name="completionSource">The completion source.</param>
        /// <param name="respondTo">The respond to.</param>
        public FutureActor(TaskCompletionSource<object> completionSource, ActorRef respondTo)
        {
            result = completionSource;
            this.respondTo = respondTo ?? ActorRef.NoSender;
        }

        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override bool Receive(object message)
        {
            if (respondTo != ActorRef.NoSender)
            {
                ((InternalActorRef)Self).Stop();
                result.SetResult(message);
                Become(EmptyReceive);
            }
            else
            {
                //if there is no listening actor asking,
                //just eval the result directly
                ((InternalActorRef)Self).Stop();
                Become(EmptyReceive);

                result.SetResult(message);
            }

            return true;
        }
    }
}