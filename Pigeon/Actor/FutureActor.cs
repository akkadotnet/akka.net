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
        public FutureActor(ActorContext context)
            : base(context)
        {
        }

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
                    var ownerMessage = new ActorAction
                    {
                        Action = () => result.SetResult(message),
                    };
                    RespondTo.Tell(ownerMessage);
                });
        }
    }

    public class SetRespondTo : IMessage
    {
        public TaskCompletionSource<IMessage> Result { get; set; }
    }
}
