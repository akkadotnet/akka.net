using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Task based dispatcher
    /// </summary>
    public class TaskDispatcher : MessageDispatcher
    {
        public override void Schedule(Action run)
        {
            Task.Factory.StartNew(run,CancellationToken.None);
        }
    }

    public class MessageSynchronizationContext : SynchronizationContext
    {
        private readonly ActorRef _sender;
        private readonly ActorRef _self;
        public MessageSynchronizationContext(ActorRef self, ActorRef sender)
        {
            _self = self;
            _sender = sender;
        }
        public override void Post(SendOrPostCallback d, object state)
        {
            try
            {
               //send the continuation as a message to self, and preserve sender from before await
                _self.Tell(new CompleteTask(() => d(state)), _sender);
            }
            catch
            {

            }
        }
    }

    public class AmbientState
    {
        public ActorRef Self { get; set; }
        public ActorRef Sender { get; set; }
        public object Message { get; set; }
    }

    public class ActorSynchronizationContext : SynchronizationContext
    {
        private static readonly ActorSynchronizationContext _instance = new ActorSynchronizationContext();

        public static ActorSynchronizationContext Instance
        {
            get { return _instance; }
        }

        public override void Post(SendOrPostCallback d, object state)
        {
            try
            {
                //This will only occur when a continuation is about to run
                var s = CallContext.LogicalGetData("akka.state") as AmbientState;

                //send the continuation as a message to self, and preserve sender from before await
                s.Self.Tell(new CompleteTask(() => d(state)), s.Sender);
            }
            catch
            {

            }
        }
    }

    //public class ActorTaskScheduler : TaskScheduler
    //{
    //    private static readonly TaskScheduler _instance = new ActorTaskScheduler();
    //    private static readonly TaskCreationOptions _mailboxTaskCreationOptions = TaskCreationOptions.LongRunning;

    //    public static TaskCreationOptions MailboxTaskCreationOptions
    //    {
    //        get { return _mailboxTaskCreationOptions; }
    //    }

    //    public static TaskScheduler Instance
    //    {
    //        get { return _instance; }
    //    }

    //    protected override IEnumerable<Task> GetScheduledTasks()
    //    {
    //        return null;
    //    }

    //    protected override void QueueTask(Task task)
    //    {
    //        if (task.CreationOptions == MailboxTaskCreationOptions)
    //        {
    //            ThreadPool.QueueUserWorkItem(_ =>
    //            {
    //                var res = TryExecuteTask(task);
    //                Console.WriteLine(res);
    //            });
    //        }
    //        else
    //        {
    //            var currentContext = InternalCurrentActorCellKeeper.Current;
    //            if (currentContext == null)
    //            {
    //                var res = TryExecuteTask(task);
    //                Console.WriteLine(res);
    //                return;
    //            }

    //            currentContext.Self.Tell(new CompleteTask(() =>
    //            {
    //                var res = TryExecuteTask(task);
    //                Console.WriteLine(res);
    //            }),currentContext.Sender);
    //        }
    //    }

    //    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    //    {
    //        return false;
    //    }
    //}
}
