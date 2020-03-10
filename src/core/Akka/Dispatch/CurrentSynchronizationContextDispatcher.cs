//-----------------------------------------------------------------------
// <copyright file="CurrentSynchronizationContextDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public override ExecutorService Produce(string id)
        {
            return new TaskSchedulerExecutor(id, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        public CurrentSynchronizationContextExecutorServiceFactory(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }
    }

    /// <summary>
    /// Used to create instances of the <see cref="PinnedDispatcher"/>. 
    /// 
    /// Each actor created using the pinned dispatcher gets its own unique thread.
    /// <remarks>
    /// Always returns a new instance.
    /// </remarks>
    /// </summary>
    internal sealed class CurrentSynchronizationContextDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly ExecutorServiceConfigurator _executorServiceConfigurator;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        public CurrentSynchronizationContextDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {

            _executorServiceConfigurator = new CurrentSynchronizationContextExecutorServiceFactory(config, prerequisites);
            // We don't bother trying to support any other type of executor here. PinnedDispatcher doesn't support them
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
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
    /// Behaves like a <see cref="PinnedDispatcher"/> and always executes using <see cref="CurrentSynchronizationContextExecutorServiceFactory"/>
    /// </summary>
    public sealed class CurrentSynchronizationContextDispatcher : Dispatcher
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configurator">TBD</param>
        /// <param name="id">TBD</param>
        /// <param name="throughput">TBD</param>
        /// <param name="throughputDeadlineTime">TBD</param>
        /// <param name="executorServiceFactory">TBD</param>
        /// <param name="shutdownTimeout">TBD</param>
        public CurrentSynchronizationContextDispatcher(MessageDispatcherConfigurator configurator, string id, int throughput, long? throughputDeadlineTime, ExecutorServiceFactory executorServiceFactory, TimeSpan shutdownTimeout) 
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
        }

        private volatile ActorCell _owner;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the registering <paramref name="actor"/> is not the <see cref="_owner">owner</see>.
        /// </exception>
        internal override void Register(ActorCell actor)
        {
            var current = _owner;
            if (current != null && actor != current) throw new InvalidOperationException($"Cannot register to anyone but {_owner}");
            _owner = actor;
            base.Register(actor);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        internal override void Unregister(ActorCell actor)
        {
            base.Unregister(actor);
            _owner = null;
        }
    }
}

