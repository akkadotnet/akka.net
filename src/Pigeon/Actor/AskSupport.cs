using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Dispatch;
using Pigeon.Dispatch.SysMsg;
using System.Threading;

namespace Pigeon.Actor
{
    public interface ICanTell
    {
        void Tell(object mssage, ActorRef sender);
    }

    /// <summary>
    /// Extension method class designed to create Ask support for
    /// non-ActorRef objects such as <see cref="ActorSelection"/>.
    /// </summary>
    public static class Futures
    {
        //when asking from outside of an actor, we need to pass a system, so the FutureActor can register itself there and be resolvable for local and remote calls
        public static Task<object> Ask(this ICanTell self, object message,TimeSpan? timeout = null)
        {
            var provider = ResolveProvider(self);
            if (provider == null)
                throw new NotSupportedException("Unable to resolve the target Provider");

            var replyTo = ResolveReplyTo();

            return Ask(self, replyTo, message, provider,timeout);
        }

        public static Task<T> Ask<T>(this ICanTell self, object message,TimeSpan? timeout = null)
        {
            var provider = ResolveProvider(self);
            if (provider == null)
                throw new NotSupportedException("Unable to resolve the target Provider");

            var replyTo = ResolveReplyTo();

            return Ask(self, replyTo, message, provider,timeout).ContinueWith<T>(t => (T)t.Result);
        }

        private static ActorRef ResolveReplyTo()
        {
            if (ActorCell.Current != null)
                return ActorCell.Current.Self;

            return null;
        }

        private static ActorRefProvider ResolveProvider(ICanTell self)
        {
            if (ActorCell.Current != null)
                return ActorCell.Current.System.Provider;

            if (self is InternalActorRef)
                return self.AsInstanceOf<InternalActorRef>().Provider;

            if (self is ActorSelection)
                return ResolveProvider(self.AsInstanceOf<ActorSelection>().Anchor);

            return null;
        }

        private static Task<object> Ask(ICanTell self, ActorRef replyTo, object message, ActorRefProvider provider,TimeSpan? timeout)
        {
            var result = new TaskCompletionSource<object>();
            //create a new tempcontainer path
            var path = provider.TempPath();
            //callback to unregister from tempcontainer
            Action unregister = () => provider.UnregisterTempActor(path);
            FutureActorRef future = new FutureActorRef(result, replyTo, unregister, path,timeout);
            //The future actor needs to be registered in the temp container
            provider.RegisterTempActor(future, path);
            self.Tell(message, future);
            return result.Task;
        }
    }

    //We need to somehow pass tasks to the current actors mailbox and complete them in the mailbox run
    public class ActorTaskScheduler : TaskScheduler
    {
        protected override void QueueTask(Task task)
        {            
            var self = ActorCell.Current.Self;
            self.Tell(new ActorTask(task), ActorRef.NoSender);
        }

        public static readonly TaskScheduler Instance = new ActorTaskScheduler();

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            throw new NotImplementedException();
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }
    }

    public class FooContext : SynchronizationContext
    {
        public override void OperationCompleted()
        {
            base.OperationCompleted();
        }

        public override void OperationStarted()
        {
            base.OperationStarted();
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            base.Post(d, state);
        }

        public override void Send(SendOrPostCallback d, object state)
        {
            base.Send(d, state);
        }
    }
}
