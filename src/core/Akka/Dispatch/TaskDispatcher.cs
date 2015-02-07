using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
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
            Task.Factory.StartNew(run, CancellationToken.None, TaskCreationOptions.LongRunning,
                ActorTaskScheduler.Instance);
        }
    }

    //public class MessageSynchronizationContext : SynchronizationContext
    //{
    //    private readonly ActorRef _sender;
    //    private readonly ActorRef _self;
    //    public MessageSynchronizationContext(ActorRef self, ActorRef sender)
    //    {
    //        _self = self;
    //        _sender = sender;
    //    }
    //    public override void Post(SendOrPostCallback d, object state)
    //    {
    //        try
    //        {
    //           //send the continuation as a message to self, and preserve sender from before await
    //            _self.Tell(new CompleteTask(() => d(state)), _sender);
    //        }
    //        catch
    //        {

    //        }
    //    }
    //}

    public class AmbientState
    {
        public ActorRef Self { get; set; }
        public ActorRef Sender { get; set; }
        public object Message { get; set; }
    }


    //public class ActorSynchronizationContext : SynchronizationContext
    //{
    //    private static readonly ActorSynchronizationContext _instance = new ActorSynchronizationContext();

    //    public static ActorSynchronizationContext Instance
    //    {
    //        get { return _instance; }
    //    }

    //    public override void Post(SendOrPostCallback d, object state)
    //    {
    //        try
    //        {
    //            //This will only occur when a continuation is about to run
    //            var s = CallContext.LogicalGetData("akka.state") as AmbientState;

    //            //send the continuation as a message to self, and preserve sender from before await
    //            s.Self.Tell(new CompleteTask(() => d(state)), s.Sender);
    //        }
    //        catch
    //        {

    //        }
    //    }
    //}

    public class ActorTaskScheduler : TaskScheduler
    {
        private const TaskCreationOptions mailboxTaskCreationOptions = TaskCreationOptions.LongRunning;
        private static readonly TaskScheduler instance = new ActorTaskScheduler();

        public static TaskCreationOptions MailboxTaskCreationOptions
        {
            get { return mailboxTaskCreationOptions; }
        }

        public static TaskScheduler Instance
        {
            get { return instance; }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }

        protected override void QueueTask(Task task)
        {
            var state = task.AsyncState;

            if (task.CreationOptions == MailboxTaskCreationOptions)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ => { TryExecuteTask(task); }, null);
            }
            else
            {
                var s = CallContext.LogicalGetData("akka.state") as AmbientState;

                if (s == null)
                {
                    TryExecuteTask(task);
                    return;
                }

                s.Self.Tell(new CompleteTask(s, () => { TryExecuteTask(task); }), ActorRef.NoSender);
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            return false;
        }
    }
}