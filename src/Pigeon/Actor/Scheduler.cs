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
        public CancellationTokenSource ScheduleOnce(TimeSpan initialDelay, ActorRef receiver, object message)
        {            
            var source = new CancellationTokenSource();
            RunOnceTask(source.Token, initialDelay, () => receiver.Tell(message));
            return source;
        }

        public CancellationTokenSource Schedule(TimeSpan initialDelay, TimeSpan interval, ActorRef receiver, object message)
        {
            var source = new CancellationTokenSource();
            RunTask(source.Token, initialDelay, interval, () => receiver.Tell(message) );
            return source;
        }

        private async void RunOnceTask(CancellationToken token, TimeSpan initialDelay, Action action)
        {
            await Task.Delay(initialDelay,token);
            action();
        }

        private async void RunTask(CancellationToken token, TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            await Task.Delay(initialDelay,token);
            while (!token.IsCancellationRequested)
            {
                action();
                await Task.Delay(interval,token);
            }
        }

        //the action will be wrapped so that it completes inside the currently active actors mailbox if there is called from within an actor
        public CancellationTokenSource Schedule(TimeSpan initialDelay, TimeSpan interval, Action action)
        {
            var source = new CancellationTokenSource();
            Action wrapped = WrapActionInActorSafeAction(action);

            RunTask(source.Token, initialDelay, interval, wrapped);
            return source;
        }

        public CancellationTokenSource ScheduleOnce(TimeSpan initialDelay, Action action)
        {
            var source = new CancellationTokenSource();
            Action wrapped = WrapActionInActorSafeAction(action);

            RunOnceTask(source.Token, initialDelay, wrapped);
            return source;
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
