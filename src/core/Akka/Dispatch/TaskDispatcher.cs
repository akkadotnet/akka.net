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
        public override void Dispatch(ActorCell cell, Envelope envelope)
        {
            CallContext.LogicalSetData("akka.state", new AmbientState
            {
                Sender = envelope.Sender,
                Self = cell.Self,
                Message = envelope.Message
            });

            base.Dispatch(cell, envelope);
        }

        public override void Schedule(Action run)
        {
            Task.Factory.StartNew(run, CancellationToken.None, TaskCreationOptions.LongRunning,
                ActorTaskScheduler.Instance);
        }
    }

    public class AmbientState
    {
        public ActorRef Self { get; set; }
        public ActorRef Sender { get; set; }
        public object Message { get; set; }
    }



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