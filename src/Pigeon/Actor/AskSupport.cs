using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Dispatch;

namespace Pigeon.Actor
{
    //public class AskableActorRef : ActorRef
    //{
    //    private ActorRef subject;
    //    public AskableActorRef(ActorRef subject)
    //    {
    //        this.subject = subject;
    //    }

    //    public override void Tell(object message, ActorRef sender = null)
    //    {
    //        throw new NotImplementedException();
    //    }
    //}

    /// <summary>
    /// Extension method class designed to create Ask support for
    /// non-ActorRef objects such as <see cref="ActorSelection"/>.
    /// </summary>
    public static class Futures
    {
        public static Task<object> Ask(this ActorSelection selection, object message, ActorSystem system)
        {
            var result = new TaskCompletionSource<object>();
            var future = system.ActorOf(Props.Create(() => new FutureActor(result, ActorRef.NoSender)));
            selection.Tell(message, future);
            return result.Task;
        }
    }
}
