//-----------------------------------------------------------------------
// <copyright file="ActorTaskScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    public class AmbientState
    {
        public IActorRef Self { get; set; }
        public IActorRef Sender { get; set; }
        public object Message { get; set; }
    }

    public class ActorTaskScheduler : TaskScheduler
    {
        public static readonly TaskScheduler Instance = new ActorTaskScheduler();
        public static readonly TaskFactory TaskFactory = new TaskFactory(Instance);
        public static readonly string StateKey = "akka.state";
        private const string Faulted = "faulted";
        private static readonly object Outer = new object();

        public static void SetCurrentState(IActorRef self, IActorRef sender, object message)
        {
            CallContext.LogicalSetData(StateKey, new AmbientState
            {
                Sender = sender,
                Self = self,
                Message = message
            });
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }

        protected override void QueueTask(Task task)
        {
            var s = CallContext.LogicalGetData(StateKey) as AmbientState;
            if (task.AsyncState == Outer || s == null)
            {
                TryExecuteTask(task);
                return;
            }

            //we get here if the task needs to be marshalled back to the mailbox
            //e.g. if previous task was an IO completion
            s = CallContext.LogicalGetData(StateKey) as AmbientState;

            s.Self.Tell(new CompleteTask(s, () =>
            {
                SetCurrentState(s.Self,s.Sender,s.Message);
                TryExecuteTask(task);
                if (task.IsFaulted)
                    Rethrow(task, null);

            }), ActorRefs.NoSender);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (taskWasPreviouslyQueued)
                return false;

            var s = CallContext.LogicalGetData(StateKey) as AmbientState;
            var cell = ActorCell.Current;

            //Is the current cell and the current state the same?
            if (cell != null &&
                s != null &&
                Equals(cell.Self, s.Self) &&
                Equals(cell.Sender, s.Sender) &&
                cell.CurrentMessage == s.Message)
            {
                var res = TryExecuteTask(task);
                return res;
            }

            return false;
        }

        public static void RunTask(Action action)
        {
            RunTask(() =>
            {
                action();
                return Task.FromResult(0);
            });
        }

        public static void RunTask(Func<Task> action)
        {
            var context = ActorCell.Current;
            var mailbox = context.Mailbox;

            //suspend the mailbox
            mailbox.Suspend(MailboxSuspendStatus.AwaitingTask);

            SetCurrentState(context.Self, context.Sender, null);

            //wrap our action inside a task, so that everything executing 
            //directly or indirectly from the action is executed on our task scheduler

            Task.Factory.StartNew(async _ =>
            {

                //start executing our action and potential promise style
                //tasks
                await action()
                    //we need to use ContinueWith so that any exception is
                    //thrown inside the actor context.
                    //this is needed for IO completion tasks that execute out of context                    
                    .ContinueWith(
                        Rethrow,
                        Faulted,
                        TaskContinuationOptions.None);

                //if mailbox was suspended, make sure we re-enable message processing again
                mailbox.Resume(MailboxSuspendStatus.AwaitingTask);
            },
                Outer,
                CancellationToken.None,
                TaskCreationOptions.None,
                Instance);
        }

        private static void Rethrow(Task x, object s)
        {
            //this just rethrows the exception the task contains
            x.Wait();
        }
    }
}

