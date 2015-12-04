//-----------------------------------------------------------------------
// <copyright file="ActorTaskScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Dispatch
{
    public class ActorTaskScheduler : TaskScheduler
    {
        private readonly ActorCell _actorCell;

        internal ActorTaskScheduler(ActorCell actorCell)
        {
            _actorCell = actorCell;
        }

        public override int MaximumConcurrencyLevel
        {
            get { return 1; }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }

        protected override void QueueTask(Task task)
        {
            if ((task.CreationOptions & TaskCreationOptions.LongRunning) == TaskCreationOptions.LongRunning)
            {
                // Executing a LongRunning task in an ActorTaskScheduler is bad practice, it will potentially
                // hang the actor and starve the ThreadPool

                // The best thing we can do here is force a rescheduling to at least not execute the task inline.
                ScheduleTask(task);
                return;
            }

            // Schedule the task execution, run inline if we are already in the actor context.
            if (ActorCell.Current == _actorCell)
            {
                TryExecuteTask(task);
            }
            else
            {
                ScheduleTask(task);
            }
        }

        private void ScheduleTask(Task task)
        {
            _actorCell.Self.Tell(new ActorTaskSchedulerMessage(this, task), ActorRefs.NoSender);
        }

        internal void ExecuteTask(Task task)
        {
            TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // Prevent inline execution, it will execute inline anyway in QueueTask if we
            // are already in the actor context.
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

        public static void RunTask(Func<Task> asyncAction)
        {
            var context = ActorCell.Current;

            if (context == null)
                throw new InvalidOperationException("RunTask must be call from an actor context.");

            var mailbox = context.Mailbox;

            //suspend the mailbox
            mailbox.Suspend(MailboxSuspendStatus.AwaitingTask);

            ActorTaskScheduler actorScheduler = context.TaskScheduler;

            Task<Task>.Factory.StartNew(() => asyncAction(), CancellationToken.None, TaskCreationOptions.None, actorScheduler)
                              .Unwrap()
                              .ContinueWith(parent =>
                              {
                                  Exception exception = GetTaskException(parent);

                                  if (exception == null)
                                  {
                                      mailbox.Resume(MailboxSuspendStatus.AwaitingTask);
                                      context.CheckReceiveTimeout();
                                  }
                                  else
                                  {
                                      context.Self.Tell(new ActorTaskSchedulerMessage(exception), ActorRefs.NoSender);
                                  }

                              }, actorScheduler);
        }

        private static Exception GetTaskException(Task task)
        {
            switch (task.Status)
            {
                case TaskStatus.Canceled:
                    return new TaskCanceledException();

                case TaskStatus.Faulted:
                    return TryUnwrapAggregateException(task.Exception);
            }

            return null;
        }

        private static Exception TryUnwrapAggregateException(AggregateException aggregateException)
        {
            if (aggregateException == null)
                return null;

            if (aggregateException.InnerExceptions.Count == 1)
                return aggregateException.InnerExceptions[0];

            return aggregateException;
        }
    }
}

