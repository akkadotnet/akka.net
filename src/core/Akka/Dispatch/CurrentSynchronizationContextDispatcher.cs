//-----------------------------------------------------------------------
// <copyright file="CurrentSynchronizationContextDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Produces <see cref="ExecutorService"/> that dispatches messages on the current synchronization context,
    ///  e.g. WinForms or WPF GUI thread
    /// </summary>
    internal sealed class CurrentSynchronizationContextExecutorServiceFactory : ExecutorServiceConfigurator
    {
        /// <summary>
        /// Creates an executor service that uses the current synchronization context's task scheduler.
        /// </summary>
        /// <param name="id">The identifier assigned to the executor service.</param>
        /// <returns>An executor service that schedules work on the current synchronization context.</returns>
        public override ExecutorService Produce(string id)
        {
            return new TaskSchedulerExecutor(id, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// Creates the executor configurator with its configuration and dispatcher prerequisites.
        /// </summary>
        /// <param name="config">The configuration for the dispatcher.</param>
        /// <param name="prerequisites">The actor-system services required by the executor configurator.</param>
        public CurrentSynchronizationContextExecutorServiceFactory(Config config,
            IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }
    }

    /// <summary>
    /// Creates dispatchers that execute work through the synchronization context captured when the dispatcher is created.
    /// <remarks>
    /// Always returns a new instance.
    /// </remarks>
    /// </summary>
    internal sealed class CurrentSynchronizationContextDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly ExecutorServiceConfigurator _executorServiceConfigurator;

        /// <summary>
        /// Creates the configurator that reads settings for synchronization-context dispatchers.
        /// </summary>
        /// <param name="config">The configuration containing the dispatcher settings.</param>
        /// <param name="prerequisites">The actor-system services required by the dispatcher.</param>
        public CurrentSynchronizationContextDispatcherConfigurator(Config config,
            IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            _executorServiceConfigurator =
                new CurrentSynchronizationContextExecutorServiceFactory(config, prerequisites);
            // We don't bother trying to support any other type of executor here. PinnedDispatcher doesn't support them
        }

        /// <summary>
        /// Creates a dispatcher from the configured identifier, throughput, deadline, and shutdown timeout.
        /// </summary>
        /// <returns>A new dispatcher that captures the current synchronization context during construction.</returns>
        public override MessageDispatcher Dispatcher()
        {
            if (Config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<MessageDispatcher>();

            return new CurrentSynchronizationContextDispatcher(this, Config.GetString("id", null),
                Config.GetInt("throughput", 0),
                Config.GetTimeSpan("throughput-deadline-time", null).Ticks,
                _executorServiceConfigurator,
                Config.GetTimeSpan("shutdown-timeout", null));
        }
    }

    /// <summary>
    /// A dispatcher whose executor is created by the supplied factory. The standard configurator supplies a factory that schedules work on the current synchronization context.
    /// </summary>
    public sealed class CurrentSynchronizationContextDispatcher : Dispatcher
    {
        /// <summary>
        /// Creates a dispatcher using the supplied executor factory. The standard configurator supplies a factory bound to the current synchronization context.
        /// </summary>
        /// <param name="configurator">The configurator that created this dispatcher.</param>
        /// <param name="id">The dispatcher identifier.</param>
        /// <param name="throughput">The throughput value passed to the base dispatcher.</param>
        /// <param name="throughputDeadlineTime">The optional throughput-deadline duration in ticks passed to the base dispatcher.</param>
        /// <param name="executorServiceFactory">The factory used to create the dispatcher executor.</param>
        /// <param name="shutdownTimeout">The time to wait for executor shutdown.</param>
        public CurrentSynchronizationContextDispatcher(MessageDispatcherConfigurator configurator, string id,
            int throughput, long? throughputDeadlineTime, ExecutorServiceFactory executorServiceFactory,
            TimeSpan shutdownTimeout)
            : base(configurator, id, throughput, throughputDeadlineTime, executorServiceFactory, shutdownTimeout)
        {
            /*
             * Critical: in order for the CurrentSynchronizationContextExecutor to function properly, it can't be lazily 
             * initialized like all of the others. It has to be executed right away.
             */
            ExecuteTask(new NoTask());
        }

        sealed class NoTask : IRunnable
        {
            public void Run()
            {
            }

#if !NETSTANDARD
            public void Execute()
            {
                
            }
#endif
        }

        private volatile ActorCell _owner;

        /// <summary>
        /// Registers the owning actor cell, rejecting a different actor if this dispatcher already has an owner.
        /// </summary>
        /// <param name="actor">The actor cell to register with this dispatcher.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the registering <paramref name="actor"/> is not the <see cref="_owner">owner</see>.
        /// </exception>
        internal override void Register(ActorCell actor)
        {
            var current = _owner;
            if (current != null && actor != current)
                throw new InvalidOperationException($"Cannot register to anyone but {_owner}");
            _owner = actor;
            base.Register(actor);
        }

        /// <summary>
        /// Unregisters the actor cell and clears the dispatcher's owner reference.
        /// </summary>
        /// <param name="actor">The actor cell being unregistered.</param>
        internal override void Unregister(ActorCell actor)
        {
            base.Unregister(actor);
            _owner = null;
        }
    }
}
