using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Class FutureActor.
    /// </summary>
    public class FutureActor : ActorBase
    {
        private IActorRef respondTo;
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
        public FutureActor(TaskCompletionSource<object> completionSource, IActorRef respondTo)
        {
            result = completionSource;
            this.respondTo = respondTo ?? ActorRefs.NoSender;
        }

        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override bool Receive(object message)
        {
            //if there is no listening actor asking,
            //just eval the result directly
            ((IInternalActorRef)Self).Stop();
            Become(EmptyReceive);

            result.SetResult(message);

            return true;
        }
    }
}