//-----------------------------------------------------------------------
// <copyright file="PinnedDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used to create instances of the <see cref="PinnedDispatcher"/>. 
    /// 
    /// Each actor created using the pinned dispatcher gets its own unique thread.
    /// <remarks>
    /// Always returns a new instance.
    /// </remarks>
    /// </summary>
    internal sealed class PinnedDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly ExecutorServiceConfigurator _executorServiceConfigurator;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        public PinnedDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            _executorServiceConfigurator = 
                new ForkJoinExecutorServiceFactory(
                    ForkJoinExecutorServiceFactory.SingleThreadDefault.WithFallback("id=" + config.GetString("id", null)), Prerequisites);
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
        /// TBD
        /// </summary>
        /// <param name="configurator">TBD</param>
        /// <param name="id">TBD</param>
        /// <param name="throughput">TBD</param>
        /// <param name="throughputDeadlineTime">TBD</param>
        /// <param name="executorServiceFactory">TBD</param>
        /// <param name="shutdownTimeout">TBD</param>
        public PinnedDispatcher(MessageDispatcherConfigurator configurator, 
            string id, int throughput, long? throughputDeadlineTime, 
            ExecutorServiceFactory executorServiceFactory, 
            TimeSpan shutdownTimeout) : base(configurator, id, throughput, throughputDeadlineTime, executorServiceFactory, shutdownTimeout)
        {
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
            if(current != null && actor != current) throw new InvalidOperationException($"Cannot register to anyone but {_owner}");
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
