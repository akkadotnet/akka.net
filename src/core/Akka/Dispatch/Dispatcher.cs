//-----------------------------------------------------------------------
// <copyright file="Dispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Annotations;
using Akka.Event;
using Akka.Util;

namespace Akka.Dispatch
{
    /// <summary>
    /// The event-based <see cref="Dispatcher"/> binds a set of actors to a thread pool backed up
    /// by a thread-safe queue.
    /// 
    /// The preferred way of creating dispatchers is to define them in configuration and use the 
    /// <see cref="Dispatchers.Lookup"/> method.
    /// </summary>
    public class Dispatcher : MessageDispatcher
    {
        /// <summary>
        /// Used to create a default <see cref="Dispatcher"/>
        /// </summary>
        /// <param name="configurator">The configurator used.</param>
        /// <param name="id">The id of this dispatcher.</param>
        /// <param name="throughput">The throughput of this dispatcher.</param>
        /// <param name="throughputDeadlineTime">The deadline for completing N (where N = throughput) operations on the mailbox..</param>
        /// <param name="executorServiceFactory">The factory for producing the executor who will do the work.</param>
        /// <param name="shutdownTimeout">The graceful stop timeout period.</param>
        public Dispatcher(MessageDispatcherConfigurator configurator, string id, int throughput, long? throughputDeadlineTime,
            ExecutorServiceFactory executorServiceFactory, TimeSpan shutdownTimeout) : base(configurator)
        {
            _executorService = new LazyExecutorServiceDelegate(id, executorServiceFactory);
            Id = id;
            Throughput = throughput;
            ThroughputDeadlineTime = throughputDeadlineTime;
            ShutdownTimeout = shutdownTimeout;
        }

        private LazyExecutorServiceDelegate _executorService;
        private ExecutorService Executor => _executorService.Executor;

        class LazyExecutorServiceDelegate
        {
            private readonly ExecutorServiceFactory _executorServiceFactory;
            private readonly string _id;
            private readonly FastLazy<ExecutorService> _executor;

            public LazyExecutorServiceDelegate(string id, ExecutorServiceFactory executorServiceFactory)
            {
                _id = id;
                _executorServiceFactory = executorServiceFactory;
                _executor = new FastLazy<ExecutorService>(() => executorServiceFactory.Produce(_id));
            }

            public ExecutorService Executor => _executor.Value;

            public LazyExecutorServiceDelegate Clone()
            {
                return new LazyExecutorServiceDelegate(_id, _executorServiceFactory);
            }
        }

        /// <summary>
        /// Schedules the <see cref="IRunnable"/> to be executed.
        /// </summary>
        /// <param name="run">The asynchronous task we're going to run</param>
        protected override void ExecuteTask(IRunnable run)
        {
            try
            {
                Executor.Execute(run);
            }
            catch (RejectedExecutionException e)
            {
                try
                {
                    Executor.Execute(run);
                }
                catch (RejectedExecutionException)
                {
                    EventStream.Publish(new Error(e, GetType().FullName, GetType(), "Schedule(IRunnable) was rejected twice!"));
                    throw;
                }
            }
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// Called one time every time an actor is detached from this dispatcher and this dispatcher has no actors left attached
        /// </summary>
        /// <remarks>
        /// MUST BE IDEMPOTENT
        /// </remarks>
        [InternalApi]
        protected override void Shutdown()
        {
            var newDelegate = _executorService.Clone();
            var es = Interlocked.Exchange(ref _executorService, newDelegate);
            es.Executor.Shutdown();
        }
    }
}

