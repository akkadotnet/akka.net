//-----------------------------------------------------------------------
// <copyright file="PinnedDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.MessageQueues;
using Akka.Event;
using Helios.Concurrency;

namespace Akka.Dispatch
{
    /// <summary>
    /// Used to create instances of the <see cref="SingleThreadDispatcher"/>. 
    /// 
    /// Each actor created using the pinned dispatcher gets its own unique thread.
    /// <remarks>
    /// Always returns a new instance.
    /// </remarks>
    /// </summary>
    class PinnedDispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly ExecutorServiceConfigurator _executorServiceConfigurator;

        public PinnedDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            
            _executorServiceConfigurator = new ForkJoinExecutorServiceFactory(ForkJoinExecutorServiceFactory.SingleThreadDefault.WithFallback("id=" + config.GetString("id")), Prerequisites);
            // We don't bother trying to support any other type of exectuor here. PinnedDispatcher doesn't support them
        }

        public override MessageDispatcher Dispatcher()
        {
            return new PinnedDispatcher(this, Config.GetString("id"),
                Config.GetInt("throughput"),
                Config.GetTimeSpan("throughput-deadline-time").Ticks,
                _executorServiceConfigurator,
                Config.GetTimeSpan("shutdown-timeout"));
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
        public PinnedDispatcher(MessageDispatcherConfigurator configurator, 
            string id, int throughput, long? throughputDeadlineTime, 
            ExecutorServiceFactory executorServiceFactory, 
            TimeSpan shutdownTimeout) : base(configurator, id, throughput, throughputDeadlineTime, executorServiceFactory, shutdownTimeout)
        {
        }

        private volatile ActorCell _owner;

        internal override void Register(ActorCell actor)
        {
            var current = _owner;
            if(current != null && actor != current) throw new InvalidOperationException("Cannot register to anyone but" + _owner);
            _owner = actor;
            base.Register(actor);
        }

        internal override void Unregister(ActorCell actor)
        {
            base.Unregister(actor);
            _owner = null;
        }
    }
}