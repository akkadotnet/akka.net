//-----------------------------------------------------------------------
// <copyright file="PinnedDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Creates a new <see cref="PinnedDispatcher"/> for each request. Each dispatcher is bound to one actor and uses its own single-thread executor.
    /// <remarks>
    /// Always returns a new instance.
    /// </remarks>
    /// </summary>
    internal sealed class PinnedDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly ExecutorServiceConfigurator _executorServiceConfigurator;

        /// <summary>
        /// Creates a configurator for a pinned dispatcher using a single-thread executor.
        /// </summary>
        /// <param name="config">The configuration containing the pinned dispatcher settings.</param>
        /// <param name="prerequisites">The actor-system services required by the dispatcher.</param>
        public PinnedDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            _executorServiceConfigurator = 
                new ForkJoinExecutorServiceFactory(
                    ForkJoinExecutorServiceFactory.SingleThreadDefault.WithFallback("id=" + config.GetString("id", null)), Prerequisites);
            // We don't bother trying to support any other type of executor here. PinnedDispatcher doesn't support them
        }

        /// <summary>
        /// Creates a new pinned dispatcher from the configured identifier and scheduling settings.
        /// </summary>
        /// <returns>A dispatcher instance dedicated to one actor.</returns>
        public override MessageDispatcher Dispatcher()
        {
            if (Config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<MessageDispatcher>();

            return new PinnedDispatcher(this, Config.GetString("id", null),
                Config.GetInt("throughput", 0),
                Config.GetTimeSpan("throughput-deadline-time", null).Ticks,
                _executorServiceConfigurator,
                Config.GetTimeSpan("shutdown-timeout", null));
        }
    }

    /// <summary>
    /// Dedicates a unique thread for each actor passed in as reference. Served through its <see cref="IMessageQueue"/>.
    /// 
    /// The preferred way of creating dispatcher is to define them in configuration and then use the <see cref="Dispatchers.Lookup"/>
    /// method.
    /// </summary>
    public sealed class PinnedDispatcher : Dispatcher
    {
        /// <summary>
        /// Creates a pinned dispatcher with its dispatcher and executor settings.
        /// </summary>
        /// <param name="configurator">The configurator that created this dispatcher.</param>
        /// <param name="id">The dispatcher identifier.</param>
        /// <param name="throughput">The maximum number of messages processed in one mailbox run.</param>
        /// <param name="throughputDeadlineTime">The optional time budget, in ticks, for processing one mailbox run.</param>
        /// <param name="executorServiceFactory">The factory for the single-thread executor used by this dispatcher.</param>
        /// <param name="shutdownTimeout">The time to wait for executor shutdown.</param>
        public PinnedDispatcher(MessageDispatcherConfigurator configurator, 
            string id, int throughput, long? throughputDeadlineTime, 
            ExecutorServiceFactory executorServiceFactory, 
            TimeSpan shutdownTimeout) : base(configurator, id, throughput, throughputDeadlineTime, executorServiceFactory, shutdownTimeout)
        {
        }

        private volatile ActorCell _owner;

        /// <summary>
        /// Registers the dispatcher owner, rejecting registration by a different actor cell.
        /// </summary>
        /// <param name="actor">The actor cell to register with this dispatcher.</param>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown if the registering <paramref name="actor"/> is not the <see cref="_owner">owner</see>.
        /// </exception>
        internal override void Register(ActorCell actor)
        {
            var current = _owner;
            if(current != null && actor != current) throw new InvalidOperationException($"Cannot register to anyone but {_owner}");
            _owner = actor;
            base.Register(actor);
        }

        /// <summary>
        /// Unregisters an actor cell and clears the dispatcher owner reference.
        /// </summary>
        /// <param name="actor">The actor cell being unregistered.</param>
        internal override void Unregister(ActorCell actor)
        {
            base.Unregister(actor);
            _owner = null;
        }
    }
}
