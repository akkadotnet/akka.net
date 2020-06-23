//-----------------------------------------------------------------------
// <copyright file="AbstractDispatcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Akka.Util.Internal;
using Helios.Concurrency;

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
        /// <param name="eventStream">TBD</param>
        /// <param name="scheduler">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="mailboxes">TBD</param>
        public DefaultDispatcherPrerequisites(EventStream eventStream, IScheduler scheduler, Settings settings, Mailboxes mailboxes)
        {
            Mailboxes = mailboxes;
            Settings = settings;
            Scheduler = scheduler;
            EventStream = eventStream;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public EventStream EventStream { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public IScheduler Scheduler { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Settings Settings { get; private set; }
        /// <summary>
        /// TBD
        /// </summary>
        public Mailboxes Mailboxes { get; private set; }
    }

    /// <summary>
    /// INTERNAL API - used to configure the executor used by the <see cref="Dispatcher"/>
    /// </summary>
    [InternalApi]
    public abstract class ExecutorServiceConfigurator : ExecutorServiceFactory
    {
        /// <summary>
        /// A configurable factory for producing <see cref="ExecutorService"/> instances.
        /// </summary>
        /// <param name="config">The configuration for this type of executor.</param>
        /// <param name="prerequisites">The prerequesites for this type of executor.</param>
        protected ExecutorServiceConfigurator(Config config, IDispatcherPrerequisites prerequisites)
        {
            Config = config;
            Prerequisites = prerequisites;
        }

        /// <summary>
        /// Executor-specific configuration
        /// </summary>
        public Config Config { get; private set; }

        /// <summary>
        /// The system prerequisites needed for this dispatcher to do its job
        /// </summary>
        public IDispatcherPrerequisites Prerequisites { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// 
    /// Produces <see cref="ExecutorService"/> that dispatches messages using the default <see cref="TaskScheduler"/>.
    /// </summary>
    internal sealed class DefaultTaskSchedulerExecutorConfigurator : ExecutorServiceConfigurator
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public override ExecutorService Produce(string id)
        {
            return new TaskSchedulerExecutor(id, TaskScheduler.Default);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        public DefaultTaskSchedulerExecutorConfigurator(Config config, IDispatcherPrerequisites prerequisites) 
            : base(config, prerequisites)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ForkJoinExecutorServiceFactory : ExecutorServiceConfigurator
    {
        /// <summary>
        /// Used by <see cref="PinnedDispatcher"/>
        /// </summary>
        public static readonly Config SingleThreadDefault = ConfigurationFactory.ParseString(@"
            dedicated-thread-pool{
                thread-count = 1 
                threadtype = background 
            }
        ");

        private readonly DedicatedThreadPoolSettings _threadPoolConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ForkJoinExecutorServiceFactory"/> class.
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if either 'dedicated-thread-pool' OR 'fork-join-executor' is not defined in <paramref name="config"/>.
        /// </exception>
        public ForkJoinExecutorServiceFactory(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            _threadPoolConfiguration = ConfigureSettings(Config);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public override ExecutorService Produce(string id)
        {
            return new ForkJoinExecutor(id, _threadPoolConfiguration);
        }

        private static DedicatedThreadPoolSettings ConfigureSettings(Config config)
        {
            var dtp = config.GetConfig("dedicated-thread-pool");
            var fje = config.GetConfig("fork-join-executor");
            if (dtp.IsNullOrEmpty() && fje.IsNullOrEmpty()) throw new ConfigurationException(
                $"must define section 'dedicated-thread-pool' OR 'fork-join-executor' for fork-join-executor {config.GetString("id", "unknown")}");

            if (!dtp.IsNullOrEmpty())
            {
                var settings = new DedicatedThreadPoolSettings(
                    dtp.GetInt("thread-count"),
                    DedicatedThreadPoolConfigHelpers.ConfigureThreadType(
                        dtp.GetString("threadtype", ThreadType.Background.ToString())),
                    config.GetString("id"),
                    DedicatedThreadPoolConfigHelpers.GetSafeDeadlockTimeout(dtp));
                return settings;
            }
            else
            {
                var settings = new DedicatedThreadPoolSettings(
                    ThreadPoolConfig.ScaledPoolSize(
                        fje.GetInt("parallelism-min"), 
                        1.0, 
                        fje.GetInt("parallelism-max")),
                        name:config.GetString("id"));
                return settings;
            }
            
        }
    }


    /// <summary>
    /// Factory for producing <see cref="ThreadPoolExecutorService"/> types.
    /// </summary>
    internal sealed class ThreadPoolExecutorServiceFactory : ExecutorServiceConfigurator
    {
        private static readonly bool IsFullTrusted = AppDomain.CurrentDomain.IsFullyTrusted;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <returns>TBD</returns>
        public override ExecutorService Produce(string id)
        {
            if (IsFullTrusted)
                return new FullThreadPoolExecutorServiceImpl(id);

            return new PartialTrustThreadPoolExecutorService(id);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        public ThreadPoolExecutorServiceFactory(Config config, IDispatcherPrerequisites prerequisites) : base(config, prerequisites)
        {
        }
    }

    /// <summary>
    /// Base class used for hooking new <see cref="MessageDispatcher"/> types into <see cref="Dispatchers"/>
    /// </summary>
    public abstract class MessageDispatcherConfigurator
    {
        /// <summary>
        /// Takes a <see cref="Config"/> object, usually passed in via <see cref="Settings.Config"/>
        /// </summary>
        /// <param name="config">TBD</param>
        /// <param name="prerequisites">TBD</param>
        protected MessageDispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
        {
            Prerequisites = prerequisites;
            Config = new CachingConfig(config);
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
        /// <returns>TBD</returns>
        public abstract MessageDispatcher Dispatcher();

        /// <summary>
        /// Configures the <see cref="ExecutorServiceConfigurator"/> that will be used inside this dispatcher.
        /// </summary>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if a custom ExecutorServiceConfiguration implementation cannot be loaded.
        /// </exception>
        /// <returns>The requested <see cref="ExecutorServiceConfigurator"/> instance.</returns>
        protected ExecutorServiceConfigurator ConfigureExecutor()
        {
            var executor = Config.GetString("executor", null);
            switch (executor)
            {
                case null:
                case "":
                case "default-executor":
                case "thread-pool-executor":
                    return new ThreadPoolExecutorServiceFactory(Config, Prerequisites);
                case "fork-join-executor":
                    return new ForkJoinExecutorServiceFactory(Config, Prerequisites);
                case "current-context-executor":
                    return new CurrentSynchronizationContextExecutorServiceFactory(Config, Prerequisites);
                case "task-executor":
                    return new DefaultTaskSchedulerExecutorConfigurator(Config, Prerequisites);
                default:
                    Type executorConfiguratorType = Type.GetType(executor);
                    if (executorConfiguratorType == null)
                    {
                        throw new ConfigurationException(
                            $"Could not resolve executor service configurator type {executor} for path {Config.GetString("id", "unknown")}");
                    }
                    var args = new object[] { Config, Prerequisites };
                    return (ExecutorServiceConfigurator)Activator.CreateInstance(executorConfiguratorType, args);
            }
        }

    }

    /// <summary>
    /// Class responsible for pushing messages from an actor's mailbox into its
    /// receive methods. Comes in many different flavors.
    /// </summary>
    public abstract class MessageDispatcher
    {
        private const int Unscheduled = 0;
        private const int Scheduled = 1;
        private const int Rescheduled = 2;

        /* dispatcher debugging helpers */
        private const bool DebugDispatcher = false; // IMPORTANT: make this a compile-time constant so compiler will elide debug code in production
        /// <summary>
        /// TBD
        /// </summary>
        internal static readonly Lazy<Index<MessageDispatcher, IInternalActorRef>> Actors = new Lazy<Index<MessageDispatcher, IInternalActorRef>>(() => new Index<MessageDispatcher, IInternalActorRef>(), LazyThreadSafetyMode.PublicationOnly);

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// INTERNAL API - Debugging purposes only! Should be elided by compiler in release builds.
        /// </summary>
        internal static void PrintActors()
        {
            if (DebugDispatcher)
            {
                foreach (var dispatcher in Actors.Value.Keys)
                {
                    var a = Actors.Value[dispatcher];
                    Console.WriteLine("{0} inhabitants {1}", dispatcher, dispatcher.Inhabitants);
                    foreach (var actor in a)
                    {
                        var status = actor.IsTerminated ? "(terminated)" : "(active)";
                        var messages = actor is ActorRefWithCell
                            ? " " + actor.AsInstanceOf<ActorRefWithCell>().Underlying.NumberOfMessages + " messages"
                            : " " + actor.GetType();
                        var parent = ", parent:" + actor.Parent;
                        Console.WriteLine(" -> " + actor + status + messages + parent);
                    }
                }
            }
        }
#pragma warning restore CS0162

        /// <summary>
        ///     The default throughput
        /// </summary>
        public const int DefaultThroughput = 100;

        /// <summary>
        /// The configurator used to configure this message dispatcher.
        /// </summary>
        public MessageDispatcherConfigurator Configurator { get; private set; }

        private long _inhabitantsDoNotCallMeDirectly;
        private int _shutdownScheduleDoNotCallMeDirectly;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MessageDispatcher" /> class.
        /// </summary>
        /// <param name="configurator">TBD</param>
        protected MessageDispatcher(MessageDispatcherConfigurator configurator)
        {
            Configurator = configurator;
            Throughput = DefaultThroughput;
            _shutdownAction = new ShutdownAction(this);
        }

        /// <summary>
        /// The <see cref="EventStream"/> for this dispatcher's actor system
        /// </summary>
        public EventStream EventStream => Configurator.Prerequisites.EventStream;

        /// <summary>
        /// The list of available <see cref="Mailboxes"/> for this dispatcher's actor system
        /// </summary>
        public Mailboxes Mailboxes => Configurator.Prerequisites.Mailboxes;

        /// <summary>
        /// The ID for this dispatcher.
        /// </summary>
        public string Id { get; protected set; }

        /// <summary>
        ///     Gets or sets the throughput deadline time.
        /// </summary>
        /// <value>The throughput deadline time.</value>
        public long? ThroughputDeadlineTime { get; protected set; }

        /// <summary>
        ///     Gets or sets the throughput.
        /// </summary>
        /// <value>The throughput.</value>
        public int Throughput { get; set; }

        /// <summary>
        /// INTERNAL API
        /// 
        /// When the dispatcher no longer has any actors registered, the <see cref="ShutdownTimeout"/> determines
        /// how long it will wait until it shuts itself down, defaulting to your Akka.NET config's 'akka.actor.default-dispatcher.shutdown-timeout'
        /// or the system default specified.
        /// </summary>
        [InternalApi]
        public TimeSpan ShutdownTimeout { get; protected set; }

        /// <summary>
        /// The number of actors attached to this <see cref="MessageDispatcher"/>
        /// </summary>
        protected long Inhabitants => Volatile.Read(ref _inhabitantsDoNotCallMeDirectly);

        private long AddInhabitants(long add)
        {
            // Interlocked.Add returns the NEW value, not the previous one - which is why this line is different from the JVM
            var ret = Interlocked.Add(ref _inhabitantsDoNotCallMeDirectly, add);
            if (ret < 0)
            {
                // We haven't succeeded in decreasing the inhabitants yet but the simple fact that we're trying to
                // go below zero means that there is an imbalance and we might as well throw the exception
                var e = new InvalidOperationException("ACTOR SYSTEM CORRUPTED!!! A dispatcher can't have less than 0 inhabitants!");
                ReportFailure(e);
                throw e;
            }
            return ret;
        }

        private int ShutdownSchedule => Volatile.Read(ref _shutdownScheduleDoNotCallMeDirectly);

        private bool UpdateShutdownSchedule(int expected, int update)
        {
            return Interlocked.CompareExchange(ref _shutdownScheduleDoNotCallMeDirectly, update, expected) == expected;
        }

        /// <summary>
        /// Used for executing arbitrary <see cref="IRunnable"/>s on this <see cref="MessageDispatcher"/>.
        /// 
        /// Not used for mailbox runs.
        /// </summary>
        private sealed class UnbatchedExecute : IRunnable
        {
            private MessageDispatcher _dispatcher;
            private IRunnable _runnable;

            public UnbatchedExecute(MessageDispatcher dispatcher, IRunnable runnable)
            {
                _dispatcher = dispatcher;
                _runnable = runnable;
            }

            public void Run()
            {
                try
                {
                    _runnable.Run();
                }
                finally
                {
                    if (_dispatcher.AddInhabitants(-1L) == 0)
                        _dispatcher.IfSensibleToDoSoThenScheduleShutdown();
                    _dispatcher = null;
                    _runnable = null;
                }
            }
        }

        /// <summary>
        ///     Schedules the specified delegate.
        /// </summary>
        /// <param name="run">The run.</param>
        public void Schedule(Action run)
        {
            Schedule(new ActionRunnable(run));
        }

        /// <summary>
        /// Schedules an arbitrary task to run.
        /// </summary>
        /// <param name="run">The run.</param>
        public void Schedule(IRunnable run)
        {
            AddInhabitants(1L);
            ExecuteTask(new UnbatchedExecute(this, run));
        }

        /// <summary>
        /// Schedules the <see cref="IRunnable"/> to be executed.
        /// </summary>
        /// <param name="run">The asynchronous task we're going to run</param>
        protected abstract void ExecuteTask(IRunnable run);

        /// <summary>
        /// Publish failure that occurred while inside the running <see cref="MessageDispatcher"/>
        /// to the <see cref="EventStream"/>.
        /// </summary>
        /// <param name="ex">The failure to publish.</param>
        protected void ReportFailure(Exception ex)
        {
            //todo: LogEventException handling
            EventStream.Publish(new Error(ex, GetType().FullName, GetType(), ex.Message));
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
        protected abstract void Shutdown();

        private readonly ShutdownAction _shutdownAction;
        sealed class ShutdownAction : IRunnable
        {
            private readonly MessageDispatcher _dispatcher;

            public ShutdownAction(MessageDispatcher dispatcher)
            {
                _dispatcher = dispatcher;
            }

            public void Run()
            {
                var sched = _dispatcher.ShutdownSchedule;
                if (sched == Scheduled)
                {
                    try
                    {
                        if (_dispatcher.Inhabitants == 0) _dispatcher.Shutdown(); // Warning, racy
                    }
                    finally
                    {
                        while (!_dispatcher.UpdateShutdownSchedule(_dispatcher.ShutdownSchedule, Unscheduled)) { }
                    }
                }
                else if (sched == Rescheduled)
                {
                    if (_dispatcher.UpdateShutdownSchedule(Rescheduled, Scheduled)) _dispatcher.ScheduleShutdownAction();
                    else Run();
                }
            }
        }

        private void IfSensibleToDoSoThenScheduleShutdown()
        {
            // Don't shutdown if we have inhabitants
            if (Inhabitants > 0) return;

            var sched = ShutdownSchedule;
            if (sched == Unscheduled)
            {
                if (UpdateShutdownSchedule(Unscheduled, Scheduled)) ScheduleShutdownAction();
                else IfSensibleToDoSoThenScheduleShutdown();
            }
            if (sched == Scheduled)
            {
                if (UpdateShutdownSchedule(Scheduled, Rescheduled)) { }
                else IfSensibleToDoSoThenScheduleShutdown();
            }

            // don't care about rescheduled
        }

        private void ScheduleShutdownAction()
        {
            try
            {
                Configurator.Prerequisites.Scheduler.Advanced.ScheduleOnce(ShutdownTimeout, () =>
                {
                    try
                    {
                        _shutdownAction.Run();
                    }
                    catch (Exception ex)
                    {
                        ReportFailure(ex);
                    }
                });
            }
            catch (SchedulerException) // Scheduler has been shut down already. Need to just cleanup synchronously then.
            {
                Shutdown();
            }
        }

        /// <summary>
        /// Creates and returns a <see cref="Mailbox"/> for the given actor.
        /// </summary>
        /// <param name="cell">Cell of the actor.</param>
        /// <param name="mailboxType">The mailbox configurator.</param>
        /// <returns>The configured <see cref="Mailbox"/> for this actor.</returns>
        internal Mailbox CreateMailbox(ActorCell cell, MailboxType mailboxType)
        {
            return new Mailbox(mailboxType.Create(cell.Self, cell.System));
        }

        /// <summary>
        /// Dispatches a user-defined message from a mailbox to an <see cref="ActorCell"/>
        /// </summary>
        /// <param name="cell">TBD</param>
        /// <param name="envelope">TBD</param>
        public virtual void Dispatch(ActorCell cell, Envelope envelope)
        {
            var mbox = cell.Mailbox;
            mbox.Enqueue(cell.Self, envelope);
            RegisterForExecution(mbox, true, false);
        }

        /// <summary>
        /// Dispatches a <see cref="SystemMessage"/> from a mailbox to an <see cref="ActorCell"/>
        /// </summary>
        /// <param name="cell">TBD</param>
        /// <param name="message">TBD</param>
        public virtual void SystemDispatch(ActorCell cell, SystemMessage message)
        {
            var mbox = cell.Mailbox;
            mbox.SystemEnqueue(cell.Self, message);
            RegisterForExecution(mbox, false, true);
        }

        /// <summary>
        /// Attaches the dispatcher to the <see cref="ActorCell"/>
        /// 
        /// <remarks>
        /// Practically, doesn't do very much right now - dispatchers aren't responsible for creating
        /// mailboxes in Akka.NET
        /// </remarks>
        /// </summary>
        /// <param name="cell">The ActorCell belonging to the actor who's attaching to this dispatcher.</param>
        public virtual void Attach(ActorCell cell)
        {
            Register(cell);
            RegisterForExecution(cell.Mailbox, false, true);
        }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// INTERNAL API 
        /// 
        /// If you override it, you must still call the base method. But only ever once. See <see cref="Attach"/> for only invocation.
        /// </summary>
        /// <param name="actor">The actor we're registering</param>
        internal virtual void Register(ActorCell actor)
        {
            if (DebugDispatcher) Actors.Value.Put(this, (IInternalActorRef)actor.Self);
            AddInhabitants(1);
        }
#pragma warning restore CS0162

        /// <summary>
        /// INTERNAL API
        /// 
        /// Suggest to register the provided mailbox for execution
        /// </summary>
        /// <param name="mbox">The mailbox</param>
        /// <param name="hasMessageHint">Do we have any messages?</param>
        /// <param name="hasSystemMessageHint">Do we have any system messages?</param>
        /// <returns><c>true</c> if the <see cref="Mailbox"/> was scheduled for execution, otherwise <c>false</c>.</returns>
        internal bool RegisterForExecution(Mailbox mbox, bool hasMessageHint, bool hasSystemMessageHint)
        {
            if (mbox.CanBeScheduledForExecution(hasMessageHint, hasSystemMessageHint)) //This needs to be here to ensure thread safety and no races
            {
                if (mbox.SetAsScheduled())
                {
                    ExecuteTask(mbox);
                    return true;
                }
                return false;
            }
            return false;
        }

        /// <summary>
        /// Detaches the dispatcher to the <see cref="ActorCell"/>
        /// 
        /// <remarks>
        /// Only really used in dispatchers with 1:1 relationship with dispatcher.
        /// </remarks>
        /// </summary>
        /// <param name="cell">The ActorCell belonging to the actor who's detaching from this dispatcher.</param>
        public virtual void Detach(ActorCell cell)
        {
            try
            {
                Unregister(cell);
            }
            finally
            {
                IfSensibleToDoSoThenScheduleShutdown();
            }
        }

#pragma warning disable CS0162 // Disabled since the flag can be set while debugging
        /// <summary>
        /// INTERNAL API
        /// 
        /// If you override it, you must call it. But only ever once. See <see cref="Detach"/> for the only invocation
        /// </summary>
        /// <param name="actor">The actor who is unregistering</param>
        internal virtual void Unregister(ActorCell actor)
        {
            if (DebugDispatcher) Actors.Value.Remove(this, (IInternalActorRef)actor.Self);
            AddInhabitants(-1);
            var mailbox = actor.SwapMailbox(Mailboxes.DeadLetterMailbox);
            mailbox.BecomeClosed();
            mailbox.CleanUp();
        }
#pragma warning restore CS0162

        /// <summary>
        /// After the call to this method, the dispatcher mustn't begin any new message processing for the specified reference 
        /// </summary>
        /// <param name="actorCell">The cell of the actor whose mailbox will be suspended.</param>
        internal virtual void Suspend(ActorCell actorCell)
        {
            var mbox = actorCell.Mailbox;
            if (mbox.Actor == actorCell && mbox.Dispatcher == this) //make sure everything is referring to the same instance
            {
                mbox.Suspend();
            }
        }

        /// <summary>
        /// After the call to this method, the dispatcher must begin any new message processing for the specified reference
        /// </summary>
        /// <param name="actorCell">The cell of the actor whose mailbox will be resumed.</param>
        internal virtual void Resume(ActorCell actorCell)
        {
            var mbox = actorCell.Mailbox;
            if (mbox.Actor == actorCell && mbox.Dispatcher == this && mbox.Resume()) //make sure everything is referring to the same instance
            {
                RegisterForExecution(mbox, false, false); // force the mailbox to re-run after resume
            }
        }
    }
}

