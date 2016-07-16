//-----------------------------------------------------------------------
// <copyright file="ExecutorService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used by the <see cref="Dispatcher"/> to execute asynchronous invocations
    /// </summary>
    public abstract class ExecutorService
    {
        protected ExecutorService(string id)
        {
            Id = id;
        }

        /// <summary>
        /// The Id of the <see cref="MessageDispatcher"/> this executor is bound to
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// Queues or executes (depending on the implementation) the <see cref="IRunnable"/>
        /// </summary>
        /// <param name="run">The asynchronous task to be executed</param>
        /// <exception cref="RejectedExecutionException">Thrown when the service can't accept additional tasks.</exception>
        public abstract void Execute(IRunnable run);

        /// <summary>
        /// Terminates this <see cref="ExecutorService"/> instance.
        /// </summary>
        public abstract void Shutdown();
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Used to produce <see cref="ExecutorServiceFactory"/> instances for use inside <see cref="Dispatcher"/>s
    /// </summary>
    public abstract class ExecutorServiceFactory
    {
        public abstract ExecutorService Produce(string id);
    }

    /// <summary>
    /// Thrown when a <see cref="ExecutorService"/> implementation rejects
    /// </summary>
    public class RejectedExecutionException : AkkaException
    {
        public RejectedExecutionException(string message = null, Exception inner = null) : base(message, inner) { }
    }
}

