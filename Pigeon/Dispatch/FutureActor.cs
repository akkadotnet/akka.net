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

        protected override void OnReceive(object message)
        {
            Pattern.Match(message)
                .With<SetRespondTo>(m =>
                {
                    this.result = m.Result;
                    this.RespondTo = this.Sender;
                })
                .Default(m =>
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
                });
        }
    }

    public class SetRespondTo 
    {
        public TaskCompletionSource<object> Result { get; set; }
    }
}
