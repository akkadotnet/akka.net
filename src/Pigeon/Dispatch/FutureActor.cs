using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    public class FutureActor : ActorBase
    {
        private ActorRef RespondTo;
        private TaskCompletionSource<object> result;

        public FutureActor()
        {
        }

        public FutureActor(TaskCompletionSource<object> completionSource, ActorRef respondTo)
        {
            result = completionSource;
            RespondTo = respondTo ?? ActorRef.NoSender;
        }

        protected override void OnReceive(object message)
        {
            if (message is SetRespondTo)
            {
                result = ((SetRespondTo) message).Result;
                RespondTo = Sender;
            }
            else
            {
                if (RespondTo != ActorRef.NoSender)
                {
                    Self.Stop();
                    RespondTo.Tell(new CompleteFuture(() => result.SetResult(message)));
                    Become(_ => { });
                }
                else
                {
                    //if there is no listening actor asking,
                    //just eval the result directly
                    Self.Stop();
                    Become(_ => { });

                    result.SetResult(message);
                }
            }
        }
    }

    public class SetRespondTo
    {
        public TaskCompletionSource<object> Result { get; set; }
    }
}