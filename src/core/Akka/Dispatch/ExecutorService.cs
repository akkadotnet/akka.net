//-----------------------------------------------------------------------
// <copyright file="ExecutorService.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Annotations;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used by the <see cref="Dispatcher"/> to execute asynchronous invocations
    /// </summary>
    public abstract class ExecutorService
    {
        /// <summary>
        /// Initializes the executor service with the identifier of its dispatcher.
        /// </summary>
        /// <param name="id">The identifier of the dispatcher that uses this executor.</param>
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
    [InternalApi]
    public abstract class ExecutorServiceFactory
    {
        /// <summary>
        /// Creates an executor service for a dispatcher.
        /// </summary>
        /// <param name="id">The identifier of the dispatcher that will use the executor.</param>
        /// <returns>The executor service instance for that dispatcher.</returns>
        public abstract ExecutorService Produce(string id);
    }

    /// <summary>
    /// Thrown when a <see cref="ExecutorService"/> implementation rejects
    /// </summary>
    public class RejectedExecutionException : AkkaException
    {
        /// <summary>
        /// Creates an exception indicating that an executor service rejected work.
        /// </summary>
        /// <param name="message">The message describing why execution was rejected.</param>
        /// <param name="inner">The exception that caused the rejection, if any.</param>
        public RejectedExecutionException(string message = null, Exception inner = null) : base(message, inner) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="RejectedExecutionException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected RejectedExecutionException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
