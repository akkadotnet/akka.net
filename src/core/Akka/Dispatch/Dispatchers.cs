//-----------------------------------------------------------------------
// <copyright file="Dispatchers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Helios.Concurrency;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Dispatch
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal abstract class ThreadPoolExecutorService : ExecutorService
    {
        // cache the delegate used for execution to prevent allocations		
        /// <summary>
        /// TBD
        /// </summary>
        protected static readonly WaitCallback Executor = t => { ((IRunnable)t).Run(); };

        /// <summary>
        /// TBD
        /// </summary>
        public override void Shutdown()
        {
            // do nothing. No cleanup required.
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        protected ThreadPoolExecutorService(string id) : base(id)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FullThreadPoolExecutorServiceImpl : ThreadPoolExecutorService
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="run">TBD</param>
        public override void Execute(IRunnable run)
        {
            ThreadPool.UnsafeQueueUserWorkItem(Executor, run);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        public FullThreadPoolExecutorServiceImpl(string id) : base(id)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class PartialTrustThreadPoolExecutorService : ThreadPoolExecutorService
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="run">TBD</param>
        public override void Execute(IRunnable run)
        {
            ThreadPool.QueueUserWorkItem(Executor, run);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        public PartialTrustThreadPoolExecutorService(string id) : base(id)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Used to power <see cref="ChannelExecutorConfigurator"/>
    /// </summary>
    internal sealed class FixedConcurrencyTaskScheduler : TaskScheduler
    {

        [ThreadStatic]
        private static bool _threadRunning = false;
        private ConcurrentQueue<Task> _tasks = new ConcurrentQueue<Task>();

        private int _readers = 0;

        public FixedConcurrencyTaskScheduler(int degreeOfParallelism)
        {
            MaximumConcurrencyLevel = degreeOfParallelism;
        }


        public override int MaximumConcurrencyLevel { get; }

        /// <summary>
        /// ONLY USED IN DEBUGGER - NO PERF IMPACT.
        /// </summary>
        protected override IEnumerable<Task> GetScheduledTasks()
        {
            return _tasks;
        }

        protected override bool TryDequeue(Task task)
        {
            return false;
        }

        protected override void QueueTask(Task task)
        {
            _tasks.Enqueue(task);
            if (_readers < MaximumConcurrencyLevel)
            {
                var initial = _readers;
                var newVale = _readers + 1;
                if (initial == Interlocked.CompareExchange(ref _readers, newVale, initial))
                {
                    // try to start a new worker
                    ThreadPool.UnsafeQueueUserWorkItem(_ => ReadChannel(), null);
                }
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // If this thread isn't already processing a task, we don't support inlining
            if (!_threadRunning) return false;
            return TryExecuteTask(task);
        }

        public void ReadChannel()
        {
            _threadRunning = true;
            try
            {
                while (_tasks.TryDequeue(out var runnable))
                {
                    base.TryExecuteTask(runnable);
                }
            }
            catch
            {
                // suppress exceptions
            }
            finally
            {
                Interlocked.Decrement(ref _readers);

                _threadRunning = false;
            }
        }
    }


    /// <summary>
    /// INTERNAL API
    /// 
    /// Executes its tasks using the <see cref="TaskScheduler"/>
    /// </summary>
    internal sealed class TaskSchedulerExecutor : ExecutorService
    {
        /// <summary>
        ///     The scheduler
        /// </summary>
        private TaskScheduler _scheduler;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="scheduler">TBD</param>
        public TaskSchedulerExecutor(string id, TaskScheduler scheduler) : base(id)
        {
            _scheduler = scheduler;
        }

        // cache the delegate used for execution to prevent allocations
        private static readonly Action<object> Executor = t => { ((IRunnable)t).Run(); };

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="run">TBD</param>
        public override void Execute(IRunnable run)
        {
            var t = new Task(Executor, run);
            t.Start(_scheduler);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Shutdown()
        {
            // clear the scheduler
            _scheduler = null;
        }
    }

    /// <summary>
    /// ForkJoinExecutorService - custom multi-threaded dispatcher that runs on top of a 
    /// <see cref="Helios.Concurrency.DedicatedThreadPool"/>, designed to be used for mission-critical actors
    /// that can't afford <see cref="ThreadPool"/> starvation.
    /// 
    /// Relevant configuration options:
    /// <code>
    ///     my-forkjoin-dispatcher {
    ///         type = ForkJoinDispatcher
    ///         throughput = 100
    ///         dedicated-thread-pool { #settings for Helios.DedicatedThreadPool
    ///             thread-count = 3 #number of threads
    ///             #deadlock-timeout = 3s #optional timeout for deadlock detection
    ///             threadtype = background #values can be "background" or "foreground"
    ///         }
    ///     }
    /// </code>
    /// </summary>
    internal sealed class ForkJoinExecutor : ExecutorService
    {
        private DedicatedThreadPool _dedicatedThreadPool;
        private byte _shuttingDown = 0;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="poolSettings">TBD</param>
        public ForkJoinExecutor(string id, DedicatedThreadPoolSettings poolSettings) : base(id)
        {
            _dedicatedThreadPool = new DedicatedThreadPool(poolSettings);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="run">TBD</param>
        /// <exception cref="RejectedExecutionException">
        /// This exception is thrown if this method is called during the shutdown of this executor.
        /// </exception>
        public override void Execute(IRunnable run)
        {
            if (Volatile.Read(ref _shuttingDown) == 1)
                throw new RejectedExecutionException("ForkJoinExecutor is shutting down");
            _dedicatedThreadPool.QueueUserWorkItem(run.Run);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Shutdown()
        {
            // shut down the dedicated threadpool and null it out
            Volatile.Write(ref _shuttingDown, 1);
            _dedicatedThreadPool?.Dispose();
            _dedicatedThreadPool = null;
        }
    }


    /// <summary>
    /// The registry of all <see cref="MessageDispatcher"/> instances available to this <see cref="ActorSystem"/>.
    ///
    /// Dispatchers are to be defined in configuration to allow for tuning
    /// for different environments. Use the <see cref="Dispatchers.Lookup"/> method to create
    /// a dispatcher as specified in configuration.
    ///
    /// A dispatcher config can also be an alias, in that case it is a config string value pointing
    /// to the actual dispatcher config.
    ///
    /// Look in `akka.actor.default-dispatcher` section of the reference.conf
    /// for documentation of dispatcher options.
    ///
    /// Not for user instantiation or extension
    /// </summary>
    public sealed class Dispatchers
    {
        /// <summary>
        ///     The default dispatcher identifier, also the full key of the configuration of the default dispatcher.
        /// </summary>
        public static readonly string DefaultDispatcherId = "akka.actor.default-dispatcher";

        ///<summary>
        /// The id of a default dispatcher to use for operations known to be blocking. Note that
        /// for optimal performance you will want to isolate different blocking resources
        /// on different thread pools.
        /// </summary>
        public static readonly string DefaultBlockingDispatcherId = "akka.actor.default-blocking-io-dispatcher";

        /// <summary>
        /// INTERNAL API
        /// </summary>
        internal static readonly string InternalDispatcherId = "akka.actor.internal-dispatcher";

        private const int MaxDispatcherAliasDepth = 20;

        /// <summary>
        ///     The identifier for synchronized dispatchers.
        /// </summary>
        public static readonly string SynchronizedDispatcherId = "akka.actor.synchronized-dispatcher";

        private readonly ActorSystem _system;
        private Config _cachingConfig;
        private readonly MessageDispatcher _defaultGlobalDispatcher;
        private readonly ILoggingAdapter _logger;

        /// <summary>
        /// The list of all configurators used to create <see cref="MessageDispatcher"/> instances.
        /// 
        /// Has to be thread-safe, as this collection can be accessed concurrently by many actors.
        /// </summary>
        private readonly ConcurrentDictionary<string, MessageDispatcherConfigurator> _dispatcherConfigurators = new ConcurrentDictionary<string, MessageDispatcherConfigurator>();

        /// <summary>Initializes a new instance of the <see cref="Dispatchers" /> class.</summary>
        /// <param name="system">The system.</param>
        /// <param name="prerequisites">The prerequisites required for some <see cref="MessageDispatcherConfigurator"/> instances.</param>
        public Dispatchers(ActorSystem system, IDispatcherPrerequisites prerequisites, ILoggingAdapter logger)
        {
            _system = system;
            Prerequisites = prerequisites;
            _cachingConfig = new CachingConfig(prerequisites.Settings.Config);
            _defaultGlobalDispatcher = Lookup(DefaultDispatcherId);
            _logger = logger;

            InternalDispatcher = Lookup(InternalDispatcherId);
        }

        /// <summary>Gets the one and only default dispatcher.</summary>
        public MessageDispatcher DefaultGlobalDispatcher
        {
            get { return _defaultGlobalDispatcher; }
        }

        internal MessageDispatcher InternalDispatcher { get; }

        /// <summary>
        /// The <see cref="Configuration.Config"/> for the default dispatcher.
        /// </summary>
        public Config DefaultDispatcherConfig
        {
            get
            {
                return
                    IdConfig(DefaultDispatcherId)
                        .WithFallback(Prerequisites.Settings.Config.GetConfig(DefaultDispatcherId));
            }
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// Used when a plugin like Akka.Persistence needs to be able to load dispatcher configurations to the chain.
        /// </summary>
        /// <param name="prerequisites">TBD</param>
        internal void ReloadPrerequisites(IDispatcherPrerequisites prerequisites)
        {
            Prerequisites = prerequisites;
            _cachingConfig = new CachingConfig(prerequisites.Settings.Config);
        }

        /// <summary>
        /// The prerequisites required for some <see cref="MessageDispatcherConfigurator"/> instances.
        /// </summary>
        public IDispatcherPrerequisites Prerequisites { get; private set; }

        /// <summary>
        /// Returns a dispatcher as specified in configuration. Please note that this
        /// method _may_ create and return a NEW dispatcher, _every_ call (depending on the `MessageDispatcherConfigurator`
        /// dispatcher config the id points to).
        ///
        /// A dispatcher id can also be an alias. In the case it is a string value in the config it is treated as the id
        /// of the actual dispatcher config to use. If several ids leading to the same actual dispatcher config is used only one
        /// instance is created. This means that for dispatchers you expect to be shared they will be.
        /// </summary>
        /// <param name="dispatcherName">TBD</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the specified dispatcher cannot be found in the configuration.
        /// </exception>
        /// <returns>TBD</returns>
        public MessageDispatcher Lookup(string dispatcherName)
        {
            return LookupConfigurator(dispatcherName).Dispatcher();
        }

        /// <summary>
        /// Checks that configuration provides a section for the given dispatcher.
        /// This does not guarantee that no <see cref="ConfigurationException"/> will be thrown
        /// when using the dispatcher, because the details can only be checked by trying to
        /// instantiate it, which might be undesirable when just checking.
        /// </summary>
        /// <param name="id">TBD</param>
        public bool HasDispatcher(string id)
        {
            return _dispatcherConfigurators.ContainsKey(id) || _cachingConfig.HasPath(id);
        }

        private MessageDispatcherConfigurator LookupConfigurator(string id)
        {
            var depth = 0;
            while (depth < MaxDispatcherAliasDepth)
            {
                if (_dispatcherConfigurators.TryGetValue(id, out var configurator))
                    return configurator;

                // It doesn't matter if we create a dispatcher configurator that isn't used due to concurrent lookup.
                // That shouldn't happen often and in case it does the actual ExecutorService isn't
                // created until used, i.e. cheap.
                if (_cachingConfig.HasPath(id))
                {
                    var valueAtPath = _cachingConfig.GetValue(id);
                    if (valueAtPath.IsString())
                    {
                        // a dispatcher key can be an alias of another dispatcher, if it is a string
                        // we treat that string value as the id of a dispatcher to lookup, it will be stored
                        // both under the actual id and the alias id in the 'dispatcherConfigurators' cache
                        var actualId = valueAtPath.GetString();
                        _logger.Debug($"Dispatcher id [{id}] is an alias, actual dispatcher will be [{actualId}]");
                        id = actualId;
                        depth++;
                        continue;
                    }

                    if (valueAtPath.IsObject())
                    {
                        var newConfigurator = ConfiguratorFrom(Config(id));
                        return _dispatcherConfigurators.TryAdd(id, newConfigurator) ? newConfigurator : _dispatcherConfigurators[id];
                    }
                    throw new ConfigurationException($"Expected either a dispatcher config or an alias at [{id}] but found [{valueAtPath}]");
                }
                throw new ConfigurationException($"Dispatcher {id} not configured.");
            }
            throw new ConfigurationException($"Could not find a concrete dispatcher config after following {MaxDispatcherAliasDepth} deep. Is there a circular reference in your config? Last followed Id was [{id}]");
        }

        /// <summary>
        /// INTERNAL API
        /// 
        /// Creates a dispatcher from a <see cref="Configuration.Config"/>. Internal test purpose only.
        /// <code>
        /// From(Config.GetConfig(id));
        /// </code>
        /// 
        /// The Config must also contain an `id` property, which is the identifier of the dispatcher.
        /// </summary>
        /// <param name="cfg">The provided configuration section.</param>
        /// <exception cref="ConfigurationException">
        /// This exception is thrown if the specified dispatcher cannot be found in <paramref name="cfg"/>.
        /// It can also be thrown if the dispatcher path or type cannot be resolved.
        /// </exception>
        /// <returns>An instance of the <see cref="MessageDispatcher"/>, if valid.</returns>
        internal MessageDispatcher From(Config cfg)
        {
            return ConfiguratorFrom(cfg).Dispatcher();
        }

        /// <summary>
        /// Register a <see cref="MessageDispatcherConfigurator"/> that will be used by <see cref="Lookup"/>
        /// and <see cref="HasDispatcher"/> instead of looking up the configurator from the system
        /// configuration.
        /// 
        /// This enables dynamic addition of dispatchers.
        /// 
        /// <remarks>
        /// A <see cref="MessageDispatcherConfigurator"/> for a certain id can only be registered once,
        /// i.e. it can not be replaced. It is safe to call this method multiple times, but only the
        /// first registration will be used.
        /// </remarks>
        /// </summary>
        /// <param name="id">TBD</param>
        /// <param name="configurator">TBD</param>
        /// <returns>This method returns <c>true</c> if the specified configurator was successfully registered.</returns>
        public bool RegisterConfigurator(string id, MessageDispatcherConfigurator configurator)
        {
            return _dispatcherConfigurators.TryAdd(id, configurator);
        }

        /// <summary>
        /// INTERNAL API
        /// </summary>
        private Config Config(string id)
        {
            return Config(id, Prerequisites.Settings.Config.GetConfig(id));
        }

        private Config Config(string id, Config appConfig)
        {
            var simpleName = id.Substring(id.LastIndexOf('.') + 1);
            return IdConfig(id)
                .WithFallback(appConfig)
                .WithFallback(ConfigurationFactory.ParseString(string.Format("name: {0}", simpleName)))
                .WithFallback(DefaultDispatcherConfig);
        }

        private Config IdConfig(string id)
        {
            return ConfigurationFactory.ParseString(string.Format("id: {0}", id));
        }


        private static readonly Config ForkJoinExecutorConfig = ConfigurationFactory.ParseString("executor=fork-join-executor");

        private static readonly Config CurrentSynchronizationContextExecutorConfig =
            ConfigurationFactory.ParseString(@"executor=current-context-executor");

        private static readonly Config TaskExecutorConfig = ConfigurationFactory.ParseString(@"executor=task-executor");
        private MessageDispatcherConfigurator ConfiguratorFrom(Config cfg)
        {
            if (cfg.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<MessageDispatcherConfigurator>();

            if (!cfg.HasPath("id"))
                throw new ConfigurationException($"Missing dispatcher `id` property in config: {cfg.Root}");

            var id = cfg.GetString("id", null);
            var type = cfg.GetString("type", null);


            MessageDispatcherConfigurator dispatcher;
            /*
             * Fallbacks are added here in order to preserve backwards compatibility with versions of AKka.NET prior to 1.1,
             * before the ExecutorService system was implemented
             */
            switch (type)
            {
                case "Dispatcher":
                    dispatcher = new DispatcherConfigurator(cfg, Prerequisites);
                    break;
                case "TaskDispatcher":
                    dispatcher = new DispatcherConfigurator(TaskExecutorConfig.WithFallback(cfg), Prerequisites);
                    break;
                case "PinnedDispatcher":
                    dispatcher = new PinnedDispatcherConfigurator(cfg, Prerequisites);
                    break;
                case "ForkJoinDispatcher":
                    dispatcher = new DispatcherConfigurator(ForkJoinExecutorConfig.WithFallback(cfg), Prerequisites);
                    break;
                case "SynchronizedDispatcher":
                    dispatcher = new CurrentSynchronizationContextDispatcherConfigurator(cfg, Prerequisites);
                    break;
                case null:
                    throw new ConfigurationException($"Could not resolve dispatcher for path {id}. type is null");
                default:
                    Type dispatcherType = Type.GetType(type);
                    if (dispatcherType == null)
                    {
                        throw new ConfigurationException($"Could not resolve dispatcher type {type} for path {id}");
                    }
                    dispatcher = (MessageDispatcherConfigurator)Activator.CreateInstance(dispatcherType, cfg, Prerequisites);
                    break;
            }

            return dispatcher;
        }
    }

    /// <summary>
    /// The cached <see cref="MessageDispatcher"/> factory that gets looked up via configuration
    /// inside <see cref="Dispatchers"/>
    /// </summary>
    public sealed class DispatcherConfigurator : MessageDispatcherConfigurator
    {
        private readonly MessageDispatcher _instance;

        /// <summary>
        /// Used to configure and produce <see cref="Dispatcher"/> instances for use with actors.
        /// </summary>
        /// <param name="config">The configuration for this dispatcher.</param>
        /// <param name="prerequisites">System prerequisites needed to run this dispatcher.</param>
        public DispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            // Need to see if a non-zero value is available for this setting
            TimeSpan deadlineTime = Config.GetTimeSpan("throughput-deadline-time", null);
            long? deadlineTimeTicks = null;
            if (deadlineTime.Ticks > 0)
                deadlineTimeTicks = deadlineTime.Ticks;

            if (Config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<DispatcherConfigurator>();

            _instance = new Dispatcher(this, Config.GetString("id"),
                Config.GetInt("throughput"),
                deadlineTimeTicks,
                ConfigureExecutor(),
                Config.GetTimeSpan("shutdown-timeout"));
        }


        /// <summary>
        /// Returns a <see cref="MessageDispatcherConfigurator.Dispatcher"/> instance.
        /// 
        /// Whether or not this <see cref="MessageDispatcherConfigurator"/> returns a new instance 
        /// or returns a reference to an existing instance is an implementation detail of the
        /// underlying implementation.
        /// </summary>
        /// <returns>TBD</returns>
        public override MessageDispatcher Dispatcher()
        {
            return _instance;
        }
    }
}

