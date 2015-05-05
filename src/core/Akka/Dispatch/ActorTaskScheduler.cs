//-----------------------------------------------------------------------
// <copyright file="ActorTaskScheduler.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akka.Dispatch
{
    public class ActorTaskScheduler : TaskScheduler
    {
        public static readonly TaskScheduler Instance = new ActorTaskScheduler();
        public static readonly TaskFactory TaskFactory = new TaskFactory(Instance);
        private const string Faulted = "faulted";

        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return null;
        }

        protected override void QueueTask(Task task)
        {
            TryExecuteTask(task);
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (taskWasPreviouslyQueued)
                return false;

            return TryExecuteTask(task);
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
            // try to execute the task inline
            var task = action();

            if (task == null)
                return;

            if (task.Status == TaskStatus.Faulted)
                ExceptionDispatchInfo.Capture(task.Exception.InnerException).Throw();

            if (task.IsCompleted)
                return;

            // at this point, the task is running asyncrhnously, so
            // we capture the context, suspend the mailbox and resume
            // only when the task is finished

            var current = InternalCurrentActorCellKeeper.Current;
            var mailbox = current.Mailbox;

            mailbox.Suspend(MailboxSuspendStatus.AwaitingTask);

            Task.Factory.StartNew(async () =>
            {
                InternalCurrentActorCellKeeper.Current = current;

                try
                {
                    await task.ContinueWith(t =>
                    {
                        var exceptionInfo = ExceptionDispatchInfo.Capture(t.Exception.InnerException);
                        current.Self.Tell(new FailedTask(exceptionInfo));
                    },
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
                }
                finally
                {
                    mailbox.Resume(MailboxSuspendStatus.AwaitingTask);
                }
            },
                CancellationToken.None,
                TaskCreationOptions.None,
                Instance);
        }
    }
}

