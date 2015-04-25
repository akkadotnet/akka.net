using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Dispatch;

namespace Akka.Actor.Await
{
    public static class ActorAwareTaskAwaitableExtensions
    {
        public static ActorAwareTaskAwaitable ActorAwait(this Task task, AsyncBehavior behavior = AsyncBehavior.Suspend)
        {
            return new ActorAwareTaskAwaitable(task, behavior);
        }

        public static ActorAwareTaskAwaitable<TResult> ActorAwait<TResult>(this Task<TResult> task, AsyncBehavior behavior = AsyncBehavior.Suspend)
        {
            return new ActorAwareTaskAwaitable<TResult>(task, behavior);
        }
    }
}
