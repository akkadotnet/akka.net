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
                .With<SetRespondTo>(m => this.RespondTo = this.Sender)
                .Default(m => this.RespondTo.Tell(m));
        }
    }

    public class FutureActorRef : ActorRef
    {
        private TaskCompletionSource<IMessage> result;

        public FutureActorRef(TaskCompletionSource<IMessage> result)
        {
            this.result = result;
        }
        public override void Tell(IMessage message, ActorRef sender = null)
        {
            result.SetResult(message);   
        }
    }

    public class SetRespondTo : IMessage
    {
    }
}
