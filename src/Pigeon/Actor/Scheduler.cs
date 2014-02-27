using Akka.Dispatch.SysMsg;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class Scheduler
    {
        public Task ScheduleOnce(TimeSpan initialDelay, ActorRef receiver, object message)
        {
            return ScheduleOnce(initialDelay, receiver, message, CancellationToken.None);
        }

        public Task ScheduleOnce(TimeSpan initialDelay, ActorRef receiver, object message,CancellationToken cancellationToken)
        {
            return RunOnceTask(cancellationToken, initialDelay, () => receiver.Tell(message));
        }

        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, ActorRef receiver, object message)
        {
            return Schedule(initialDelay, interval, receiver, message, CancellationToken.None);
        }

        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, ActorRef receiver, object message, CancellationToken cancellationToken)
        {
            return RunTask(cancellationToken, initialDelay, interval, () => receiver.Tell(message));
        }

        //the action will be wrapped so that it completes inside the currently active actors mailbox if there is called from within an actor
        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            return Schedule(initialDelay, interval, action, CancellationToken.None);
        }
        
        public Task Schedule(TimeSpan initialDelay, TimeSpan interval, Action action, CancellationToken cancellationToken)
        {
            Action wrapped = WrapActionInActorSafeAction(action);
            return RunTask(cancellationToken, initialDelay, interval, wrapped);
        }

        public Task ScheduleOnce(TimeSpan initialDelay, Action action)
        {
            return ScheduleOnce(initialDelay, action, CancellationToken.None);
        }
        public Task ScheduleOnce(TimeSpan initialDelay, Action action, CancellationToken cancellationToken)
        {
            Action wrapped = WrapActionInActorSafeAction(action);
            return RunOnceTask(cancellationToken, initialDelay, wrapped);
        }

        private async Task RunOnceTask(CancellationToken token, TimeSpan initialDelay, Action action)
        {
            await Task.Delay(initialDelay, token);
            action();
        }

        private async Task RunTask(CancellationToken token, TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            await Task.Delay(initialDelay, token);
            while (!token.IsCancellationRequested)
            {
                action();
                await Task.Delay(interval, token);
            }
        }

        private static Action WrapActionInActorSafeAction(Action action)
        {            
            var wrapped = action;
            if (ActorCell.Current != null)
            {
                var self = ActorCell.Current.Self;
                wrapped = () => self.Tell(new CompleteFuture(action));
            }           
            return wrapped;
        }
    }
}
