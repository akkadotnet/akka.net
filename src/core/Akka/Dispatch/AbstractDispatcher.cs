using System;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Dispatch
{
    /// <summary>
    /// Contextual information that's useful for dispatchers
    /// </summary>
    public interface IDispatcherPrerequisites
    {
        /// <summary>
        /// The <see cref="EventStream"/> that belongs to the current <see cref="ActorSystem"/>.
        /// </summary>
        EventStream EventStream { get; }

        /// <summary>
        /// The <see cref="IScheduler"/> that belongs to the current <see cref="ActorSystem"/>.
        /// </summary>
        IScheduler Scheduler { get; }

        /// <summary>
        /// The <see cref="Settings"/> for the current <see cref="ActorSystem"/>.
        /// </summary>
        Settings Settings { get; }

        /// <summary>
        /// The list of registered <see cref="Mailboxes"/> for the current <see cref="ActorSystem"/>.
        /// </summary>
        Mailboxes Mailboxes { get; }
    }

    /// <summary>
    /// The default set of contextual data needed for <see cref="MessageDispatcherConfigurator"/>s
    /// </summary>
    public sealed class DefaultDispatcherPrerequisites : IDispatcherPrerequisites
    {
        /// <summary>
        /// Default constructor...
        /// </summary>
        public DefaultDispatcherPrerequisites(EventStream eventStream, IScheduler scheduler, Settings settings, Mailboxes mailboxes)
        {
            Mailboxes = mailboxes;
            Settings = settings;
            Scheduler = scheduler;
            EventStream = eventStream;
        }

        public EventStream EventStream { get; private set; }
        public IScheduler Scheduler { get; private set; }
        public Settings Settings { get; private set; }
        public Mailboxes Mailboxes { get; private set; }
    }


    /// <summary>
    /// Base class used for hooking new <see cref="MessageDispatcher"/> types into <see cref="Dispatchers"/>
    /// </summary>
    public abstract class MessageDispatcherConfigurator
    {
        /// <summary>
        /// Takes a <see cref="Config"/> object, usually passed in via <see cref="Settings.Config"/>
        /// </summary>
        protected MessageDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
        {
            Prerequisites = prerequisites;
            Config = config;
        }

        /// <summary>
        /// System-wide configuration
        /// </summary>
        public Config Config { get; private set; }

        /// <summary>
        /// The system prerequisites needed for this dispatcher to do its job
        /// </summary>
        public IDispatcherPrerequisites Prerequisites { get; private set; }

        /// <summary>
        /// Returns a <see cref="Dispatcher"/> instance.
        /// 
        /// Whether or not this <see cref="MessageDispatcherConfigurator"/> returns a new instance 
        /// or returns a reference to an existing instance is an implementation detail of the
        /// underlying implementation.
        /// </summary>
        /// <returns></returns>
        public abstract MessageDispatcher Dispatcher();
    }

    /// <summary>
    /// Used to create instances of the <see cref="ThreadPoolDispatcher"/>.
    /// 
    /// <remarks>
    /// Always returns the same instance, since the <see cref="ThreadPool"/> is global.
    /// This is also the default dispatcher for all actors.
    /// </remarks>
    /// </summary>
    class ThreadPoolDispatcherConfigurator : MessageDispatcherConfigurator
    {
        public ThreadPoolDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
            _instance = new ThreadPoolDispatcher(this);
        }

        //cached instance
        private readonly ThreadPoolDispatcher _instance;

        public override MessageDispatcher Dispatcher()
        {
            /*
             * Always want to return the same instance of the ThreadPoolDispatcher
             */
            return _instance;
        }
    }

    /// <summary>
    /// Used to create instances of the <see cref="TaskDispatcher"/>.
    /// 
    /// <remarks>
    /// Always returns the same instance.
    /// </remarks>
    /// </summary>
    class TaskDispatcherConfigurator : MessageDispatcherConfigurator
    {
        public TaskDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
            _instance = new TaskDispatcher(this);
        }

        private readonly TaskDispatcher _instance;

        public override MessageDispatcher Dispatcher()
        {
            return _instance;
        }
    }

    /// <summary>
    /// Used to create instances of the <see cref="SingleThreadDispatcher"/>. 
    /// <remarks>
    /// Always returns the same instance.
    /// </remarks>
    /// </summary>
    class PinnedDispatcherConfigurator : MessageDispatcherConfigurator
    {
        public PinnedDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
            _dispatcher = new SingleThreadDispatcher(this);
        }

        private readonly SingleThreadDispatcher _dispatcher;

        public override MessageDispatcher Dispatcher()
        {
            return _dispatcher;
        }
    }

    /// <summary>
    /// Used to create instances of the <see cref="CurrentSynchronizationContextDispatcher"/>.
    /// 
    /// <remarks>
    /// Always returns the a new instance.
    /// </remarks>
    /// </summary>
    class CurrentSynchronizationContextDispatcherConfigurator : MessageDispatcherConfigurator
    {
        public CurrentSynchronizationContextDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }

        public override MessageDispatcher Dispatcher()
        {
            return new CurrentSynchronizationContextDispatcher(this);
        }
    }

    /// <summary>
    /// Lookup list for different types of out-of-the-box <see cref="Dispatcher"/>s.
    /// </summary>
    public enum DispatcherType
    {
        Dispatcher,
        TaskDispatcher,
        PinnedDispatcher,
        SynchronizedDispatcher,
    }
    public static class DispatcherTypeMembers
    {
        public static string GetName(this DispatcherType self)
        {
            //TODO: switch case return string?
            return self.ToString();
        }
    }
    /// <summary>
    ///     Class MessageDispatcher.
    /// </summary>
    public abstract class MessageDispatcher
    {
        /// <summary>
        ///     The default throughput
        /// </summary>
        public const int DefaultThroughput = 100;

        /// <summary>
        /// The configurator used to configure this message dispatcher.
        /// </summary>
        public MessageDispatcherConfigurator Configurator { get; private set; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageDispatcher" /> class.
        /// </summary>
        protected MessageDispatcher(MessageDispatcherConfigurator configurator)
        {
            Configurator = configurator;
            Throughput = DefaultThroughput;
        }

        /// <summary>
        /// The ID for this dispatcher.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        ///     Gets or sets the throughput deadline time.
        /// </summary>
        /// <value>The throughput deadline time.</value>
        public long? ThroughputDeadlineTime { get; set; }

        /// <summary>
        ///     Gets or sets the throughput.
        /// </summary>
        /// <value>The throughput.</value>
        public int Throughput { get; set; }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public abstract void Schedule(Action run);

        /// <summary>
        /// Dispatches a user-defined message from a mailbox to an <see cref="ActorCell"/>        
        /// </summary>
        public virtual void Dispatch(ActorCell cell, Envelope envelope)
        {
            cell.Invoke(envelope);
        }

        /// <summary>
        /// Dispatches a <see cref="SystemMessage"/> from a mailbox to an <see cref="ActorCell"/>        
        /// </summary>
        public virtual void SystemDispatch(ActorCell cell, Envelope envelope)
        {
            cell.SystemInvoke(envelope);
        }
    }
}
