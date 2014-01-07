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
                    Self.Stop();
                    RespondTo.Tell(new CompleteFuture(() => result.SetResult(message)));
                    Become(_ => { });
                });
        }
    }

    public class SetRespondTo 
    {
        public TaskCompletionSource<object> Result { get; set; }
    }
}
