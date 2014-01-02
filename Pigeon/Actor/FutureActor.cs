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
        private TaskCompletionSource<IMessage> result;
        private ActorRef RespondTo;

        protected override void OnReceive(IMessage message)
        {
            message
                .Match()
                .With<SetRespondTo>(m =>
                {
                    this.result = m.Result;
                    this.RespondTo = this.Sender;
                })
                .Default(m =>
                {
                    var futureCompleteResponse = new ActorAction
                    {
                        Action = () => 
                        {
                            Context.Parent.Stop(Self); //kill self
                            result.SetResult(message); //notify .NET that task is complete
                        },
                    };
                    RespondTo.Tell(futureCompleteResponse);
                });
        }
    }

    public class SetRespondTo : IMessage
    {
        public TaskCompletionSource<IMessage> Result { get; set; }
    }
}
