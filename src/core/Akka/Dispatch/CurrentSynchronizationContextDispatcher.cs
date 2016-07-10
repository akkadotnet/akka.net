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
        public override ExecutorService Produce(string id)
        {
            return new TaskSchedulerExecutor(id, TaskScheduler.FromCurrentSynchronizationContext());
        }

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

        public CurrentSynchronizationContextDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {

            _executorServiceConfigurator = new CurrentSynchronizationContextExecutorServiceFactory(config, prerequisites);
            // We don't bother trying to support any other type of exectuor here. PinnedDispatcher doesn't support them
        }

        public override MessageDispatcher Dispatcher()
        {
            return new CurrentSynchronizationContextDispatcher(this, Config.GetString("id"),
                Config.GetInt("throughput"),
                Config.GetTimeSpan("throughput-deadline-time").Ticks,
                _executorServiceConfigurator,
                Config.GetTimeSpan("shutdown-timeout"));
        }
    }

    /// <summary>
    /// Behaves like a <see cref="PinnedDispatcher"/> and always executes using <see cref="CurrentSynchronizationContextExecutorServiceFactory"/>
    /// </summary>
    public sealed class CurrentSynchronizationContextDispatcher : Dispatcher
    {
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

        internal override void Register(ActorCell actor)
        {
            var current = _owner;
            if (current != null && actor != current) throw new InvalidOperationException("Cannot register to anyone but" + _owner);
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
