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
        /// <summary>
        ///     The respond to
        /// </summary>
        private ActorRef respondTo;

        /// <summary>
        ///     The result
        /// </summary>
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
            if (message is SetRespondTo)
            {
                result = ((SetRespondTo) message).Result;
                respondTo = Sender;
            }
            else
            {
                if (respondTo != ActorRef.NoSender)
                {
                    ((InternalActorRef)Self).Stop();
                    respondTo.Tell(new CompleteFuture(() => result.SetResult(message)));
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
            }
            return true;
        }
    }

    /// <summary>
    ///     Class SetRespondTo.
    /// </summary>
    public class SetRespondTo
    {
        /// <summary>
        ///     Gets or sets the result.
        /// </summary>
        /// <value>The result.</value>
        public TaskCompletionSource<object> Result { get; set; }
    }
}