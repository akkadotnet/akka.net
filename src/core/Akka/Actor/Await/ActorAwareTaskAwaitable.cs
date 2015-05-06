using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Dispatch;

namespace Akka.Actor.Await
{
    public struct ActorAwareTaskAwaitable
    {
        private readonly Task _task;
        private readonly AsyncBehavior _behavior;

        public ActorAwareTaskAwaitable(Task task, AsyncBehavior behavior)
        {
            _task = task;
            _behavior = behavior;
        }

        public ActorAwareTaskAwaiter GetAwaiter()
        {
            return new ActorAwareTaskAwaiter(_task, _behavior);
        }
    }

    public struct ActorAwareTaskAwaitable<TResult>
    {
        private readonly Task<TResult> _task;
        private readonly AsyncBehavior _behavior;

        public ActorAwareTaskAwaitable(Task<TResult> task, AsyncBehavior behavior)
        {
            _task = task;
            _behavior = behavior;
        }

        public ActorAwareTaskAwaiter<TResult> GetAwaiter()
        {
            return new ActorAwareTaskAwaiter<TResult>(_task, _behavior);
        }
    }
}
