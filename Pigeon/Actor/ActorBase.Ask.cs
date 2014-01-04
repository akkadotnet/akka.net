using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public abstract partial class ActorBase
    {
        public Task<object> Ask(ActorRef actor, object message)
        {
            var result = new TaskCompletionSource<object>();
            var future = Context.ActorOf<FutureActor>();
            future.Tell(new SetRespondTo { Result = result }, Self);
            actor.Tell(message, future);
            return result.Task;
        }
    }
}
