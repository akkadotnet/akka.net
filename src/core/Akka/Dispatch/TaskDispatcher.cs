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
    /// Task based dispatcher with async await support
    /// </summary>
    public class TaskDispatcher : MessageDispatcher
    {
        public override void Dispatch(ActorCell cell, Envelope envelope)
        {
            ActorTaskScheduler.SetCurrentState(cell.Self, envelope.Sender, envelope.Message);
            base.Dispatch(cell, envelope);
        }

        public override void Schedule(Action run)
        {
            ActorTaskScheduler.Schedule(run);        
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
        public static readonly TaskScheduler Instance = new ActorTaskScheduler();
        public static readonly TaskFactory TaskFactory = new TaskFactory(Instance);
        public static readonly object RootScheduler = new object();
        public static readonly string StateKey = "akka.state";

        public static void SetCurrentState(ActorRef self,ActorRef sender,object message)
        {
            CallContext.LogicalSetData(StateKey, new AmbientState
            {
                Sender = sender,
                Self = self,
                Message = message
            });
        }

        public static void Schedule(Action run)
        {
            TaskFactory.StartNew(_ => run(), RootScheduler);
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }

        protected override void QueueTask(Task task)
        {

            if (task.AsyncState == RootScheduler)
            {
                ThreadPool.UnsafeQueueUserWorkItem(_ => { TryExecuteTask(task); }, null);
            }
            else
            {
                var s = CallContext.LogicalGetData(StateKey) as AmbientState;

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