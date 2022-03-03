//-----------------------------------------------------------------------
// <copyright file="PipeToSupport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
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
        /// <typeparam name="T">The type of result of the Task</typeparam>
        /// <param name="taskToPipe">The Task that result needs to be piped to an actor</param>
        /// <param name="recipient">The actor that will receive the Task result</param>
        /// <param name="sender">The IActorRef that will be used as the sender of the result. Defaults to <see cref="ActorRefs.Nobody"/> </param>
        /// <param name="success">A callback function that will be called on Task success. Defaults to <c>null</c> for no callback</param>
        /// <param name="failure">A callback function that will be called on Task failure. Defaults to <c>null</c> for no callback</param>
        /// <returns>A detached task</returns>
        public static Task PipeTo<T>(
            this Task<T> taskToPipe,
            ICanTell recipient,
            IActorRef sender = null,
            Func<T, object> success = null,
            Func<Exception, object> failure = null)
            => PipeTo(taskToPipe, recipient, false, sender, success, failure);
        
        /// <summary>
        /// Pipes the output of a Task directly to the <paramref name="recipient"/>'s mailbox once
        /// the task completes
        /// </summary>
        /// <typeparam name="T">The type of result of the Task</typeparam>
        /// <param name="taskToPipe">The Task that result needs to be piped to an actor</param>
        /// <param name="recipient">The actor that will receive the Task result</param>
        /// <param name="sender">The IActorRef that will be used as the sender of the result. Defaults to <see cref="ActorRefs.Nobody"/> </param>
        /// <param name="success">A callback function that will be called on Task success. Defaults to <c>null</c> for no callback</param>
        /// <param name="failure">A callback function that will be called on Task failure. Defaults to <c>null</c> for no callback</param>
        /// <param name="useConfigureAwait">Sets the <c>taskToPipe.ConfigureAwait(bool)</c> <c>bool</c> parameter</param>
        /// <returns>A detached task</returns>
        public static async Task PipeTo<T>(
            this Task<T> taskToPipe,
            ICanTell recipient,
            bool useConfigureAwait,
            IActorRef sender = null, 
            Func<T, object> success = null,
            Func<Exception, object> failure = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            
            try
            {
                var result = await taskToPipe.ConfigureAwait(useConfigureAwait); 
                
                recipient.Tell(success != null
                    ? success(result)
                    : result, sender);
            }
            catch (Exception ex)
            {
                recipient.Tell(failure != null
                    ? failure(ex)
                    : new Status.Failure(ex), sender);
            }
        }

        /// <summary>
        /// Pipes the output of a Task directly to the <paramref name="recipient"/>'s mailbox once
        /// the task completes.  As this task has no result, only exceptions will be piped to the <paramref name="recipient"/>
        /// </summary>
        /// <param name="taskToPipe">The Task that result needs to be piped to an actor</param>
        /// <param name="recipient">The actor that will receive the Task result</param>
        /// <param name="sender">The IActorRef that will be used as the sender of the result. Defaults to <see cref="ActorRefs.Nobody"/> </param>
        /// <param name="success">A callback function that will be called on Task success. Defaults to <c>null</c> for no callback</param>
        /// <param name="failure">A callback function that will be called on Task failure. Defaults to <c>null</c> for no callback</param>
        /// <returns>A detached task</returns>
        public static Task PipeTo(
            this Task taskToPipe,
            ICanTell recipient,
            IActorRef sender = null,
            Func<object> success = null,
            Func<Exception, object> failure = null)
            => PipeTo(taskToPipe, recipient, false, sender, success, failure);
        
        /// <summary>
        /// Pipes the output of a Task directly to the <paramref name="recipient"/>'s mailbox once
        /// the task completes.  As this task has no result, only exceptions will be piped to the <paramref name="recipient"/>
        /// </summary>
        /// <param name="taskToPipe">The Task that result needs to be piped to an actor</param>
        /// <param name="recipient">The actor that will receive the Task result</param>
        /// <param name="sender">The IActorRef that will be used as the sender of the result. Defaults to <see cref="ActorRefs.Nobody"/> </param>
        /// <param name="success">A callback function that will be called on Task success. Defaults to <c>null</c> for no callback</param>
        /// <param name="failure">A callback function that will be called on Task failure. Defaults to <c>null</c> for no callback</param>
        /// <param name="useConfigureAwait">Sets the <c>taskToPipe.ConfigureAwait(bool)</c> <c>bool</c> parameter</param>
        /// <returns>A detached task</returns>
        public static async Task PipeTo(
            this Task taskToPipe,
            ICanTell recipient,
            bool useConfigureAwait,
            IActorRef sender = null,
            Func<object> success = null,
            Func<Exception, object> failure = null)
        {
            sender = sender ?? ActorRefs.NoSender;
            
            try
            {
                await taskToPipe.ConfigureAwait(useConfigureAwait);

                if (success != null)
                {
                    recipient.Tell(success(), sender);
                }
            }
            catch (Exception ex)
            {
                recipient.Tell(failure != null
                    ? failure(ex)
                    : new Status.Failure(ex), sender);
            }
        }
    }
}

