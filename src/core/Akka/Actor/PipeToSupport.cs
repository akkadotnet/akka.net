//-----------------------------------------------------------------------
// <copyright file="PipeToSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;

namespace Akka.Actor
{
    /// <summary>
    /// Creates the PipeTo pattern for automatically sending the results of completed tasks
    /// into the inbox of a designated Actor
    /// </summary>
    public static class PipeToSupport
    {
        /// <summary>
        /// Pipes the output of a Task directly to the <paramref name="recipient"/>'s mailbox once
        /// the task completes
        /// </summary>
        public static Task PipeTo<T>(this Task<T> taskToPipe, ICanTell recipient, IActorRef sender = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            return taskToPipe.ContinueWith(tresult =>
            {
                if (tresult.IsCanceled || tresult.IsFaulted)
                    recipient.Tell(new Status.Failure(tresult.Exception), sender);
                else if (tresult.IsCompleted)
                    recipient.Tell(tresult.Result, sender);
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }

        /// <summary>
        /// Pipes the output of a Task directly to the <paramref name="recipient"/>'s mailbox once
        /// the task completes.  As this task has no result, only exceptions will be piped to the <paramref name="recipient"/>
        /// </summary>
        public static Task PipeTo(this Task taskToPipe, ICanTell recipient, IActorRef sender = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            return taskToPipe.ContinueWith(tresult =>
            {
                if (tresult.IsCanceled || tresult.IsFaulted)
                    recipient.Tell(new Status.Failure(tresult.Exception), sender);
            }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }
    }
}

