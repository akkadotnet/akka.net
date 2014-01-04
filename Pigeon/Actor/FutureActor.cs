using Pigeon.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
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
                    var context = Context;
                    var futureCompleteResponse = new CompleteFuture(
                    () => 
                        {
                            //if we dont close over a var here, we will stop the wrong actor
                            context.Parent.Stop((LocalActorRef)this.Self); //kill self
                            result.SetResult(message); //notify .NET that task is complete
                        });
                    RespondTo.Tell(futureCompleteResponse);
                });
        }
    }

    public class SetRespondTo 
    {
        public TaskCompletionSource<object> Result { get; set; }
    }
}
