using Pigeon.Actor;
using Pigeon.Dispatch.SysMsg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Dispatch
{
    public class FutureActor : ActorBase
    {
        private TaskCompletionSource<object> result;
        private ActorRef RespondTo;

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
                this.result = ((SetRespondTo)message).Result;
                this.RespondTo = this.Sender;
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
