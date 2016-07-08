//-----------------------------------------------------------------------
// <copyright file="Dispatchers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Helios.Concurrency;

namespace Akka.Dispatch
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal abstract class ThreadPoolExecutorService : ExecutorService
    {
        // cache the delegate used for execution to prevent allocations
        protected static readonly WaitCallback Executor = t => { ((IRunnable)t).Run(); };

        public override void Shutdown()
        {
            // do nothing. No cleanup required.
        }

        protected ThreadPoolExecutorService(string id) : base(id)
        {
        }
    }

#if UNSAFE_THREADING
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FullThreadPoolExecutorServiceImpl : ThreadPoolExecutorService
    {
        public override void Execute(IRunnable run)
        {
            ThreadPool.UnsafeQueueUserWorkItem(Executor, run);
        }

        public FullThreadPoolExecutorServiceImpl(string id) : base(id)
        {
        }
    }
#endif

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class PartialTrustThreadPoolExecutorService : ThreadPoolExecutorService
    {
        public override void Execute(IRunnable run)
        {
            ThreadPool.QueueUserWorkItem(Executor, run);
        }

        public PartialTrustThreadPoolExecutorService(string id) : base(id)
        {
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

        public TaskSchedulerExecutor(string id, TaskScheduler scheduler) : base(id)
        {
            _scheduler = scheduler;
        }

        // cache the delegate used for execution to prevent allocations
        private static readonly Action<object> Executor = t => { ((IRunnable)t).Run(); };

        public override void Execute(IRunnable run)
        {
            var t = new Task(Executor, run);
            t.Start(_scheduler);
        }

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
    ///     my-forkjoin-dispatcher{
    ///             type = ForkJoinDispatcher
    ///	            throughput = 100
    ///	            dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
    ///		            thread-count = 3 #number of threads
    ///		            #deadlock-timeout = 3s #optional timeout for deadlock detection
    ///		            threadtype = background #values can be "background" or "foreground"
    ///	            }
    ///     }
    /// </code>
    /// </summary>
    internal sealed class ForkJoinExecutor : ExecutorService
    {
        private DedicatedThreadPool _dedicatedThreadPool;
        private byte _shuttingDown = 0;

        public ForkJoinExecutor(string id, DedicatedThreadPoolSettings poolSettings) : base(id)
        {
            _dedicatedThreadPool = new DedicatedThreadPool(poolSettings);
        }

        public override void Execute(IRunnable run)
        {
            if (Volatile.Read(ref _shuttingDown) == 1)
                throw new RejectedExecutionException("ForkJoinExecutor is shutting down");
            _dedicatedThreadPool.QueueUserWorkItem(run.Run);
        }

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
    /// </summary>
    public sealed class Dispatchers
    {
        /// <summary>
        ///     The default dispatcher identifier, also the full key of the configuration of the default dispatcher.
        /// </summary>
        public static readonly string DefaultDispatcherId = "akka.actor.default-dispatcher";

        /// <summary>
        ///     The identifier for synchronized dispatchers.
        /// </summary>
        public static readonly string SynchronizedDispatcherId = "akka.actor.synchronized-dispatcher";

        private readonly ActorSystem _system;
        private CachingConfig _cachingConfig;
        private readonly MessageDispatcher _defaultGlobalDispatcher;

        /// <summary>
        /// The list of all configurators used to create <see cref="MessageDispatcher"/> instances.
        /// 
        /// Has to be thread-safe, as this collection can be accessed concurrently by many actors.
        /// </summary>
        private readonly ConcurrentDictionary<string, MessageDispatcherConfigurator> _dispatcherConfigurators = new ConcurrentDictionary<string, MessageDispatcherConfigurator>();

        /// <summary>Initializes a new instance of the <see cref="Dispatchers" /> class.</summary>
        /// <param name="system">The system.</param>
        /// <param name="prerequisites">The prerequisites required for some <see cref="MessageDispatcherConfigurator"/> instances.</param>
        public Dispatchers(ActorSystem system, IDispatcherPrerequisites prerequisites)
        {
            _system = system;
            Prerequisites = prerequisites;
            _cachingConfig = new CachingConfig(prerequisites.Settings.Config);
            _defaultGlobalDispatcher = Lookup(DefaultDispatcherId);
        }

        /// <summary>Gets the one and only default dispatcher.</summary>
        public MessageDispatcher DefaultGlobalDispatcher
        {
            get { return _defaultGlobalDispatcher; }
        }

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
        /// Returns a dispatcher as specified in configuration. Please note that this method _MAY_
        /// create and return a new dispatcher on _EVERY_ call.
        /// </summary>
        /// <exception cref="ConfigurationException">If the specified dispatcher cannot be found in configuration.</exception>
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
        public bool HasDispatcher(string id)
        {
            return _dispatcherConfigurators.ContainsKey(id) || _cachingConfig.HasPath(id);
        }

        private MessageDispatcherConfigurator LookupConfigurator(string id)
        {
            MessageDispatcherConfigurator configurator;
            if (!_dispatcherConfigurators.TryGetValue(id, out configurator))
            {
                // It doesn't matter if we create a dispatcher configurator that isn't used due to concurrent lookup.
                // That shouldn't happen often and in case it does the actual ExecutorService isn't
                // created until used, i.e. cheap.
                MessageDispatcherConfigurator newConfigurator;
                if (_cachingConfig.HasPath(id))
                {
                    newConfigurator = ConfiguratorFrom(Config(id));
                }
                else
                {
                    throw new ConfigurationException(string.Format("Dispatcher {0} not configured.", id));
                }

                return _dispatcherConfigurators.TryAdd(id, newConfigurator) ? newConfigurator : _dispatcherConfigurators[id];
            }

            return configurator;
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
        /// <returns>An instance of the <see cref="MessageDispatcher"/>, if valid.</returns>
        /// <exception cref="ConfigurationException">if the `id` property is missing from <paramref name="cfg"/></exception>
        /// <exception cref="NotSupportedException">thrown if the dispatcher path or type cannot be resolved.</exception>
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
            if (!cfg.HasPath("id")) throw new ConfigurationException(string.Format("Missing dispatcher `id` property in config: {0}", cfg.Root));

            var id = cfg.GetString("id");
            var type = cfg.GetString("type");


            MessageDispatcherConfigurator dispatcher;
            /*
             * Fallbacks are added here in order to preserve backwards compatiblity with versions of AKka.NET prior to 1.1,
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
                    dispatcher = new DispatcherConfigurator(CurrentSynchronizationContextExecutorConfig.WithFallback(cfg), Prerequisites);
                    break;
                case null:
                    throw new ConfigurationException("Could not resolve dispatcher for path " + id + ". type is null");
                default:
                    Type dispatcherType = Type.GetType(type);
                    if (dispatcherType == null)
                    {
                        throw new ConfigurationException("Could not resolve dispatcher type " + type + " for path " + id);
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
        /// <param name="prerequisites">System pre-reqs needed to run this dispatcher.</param>
        public DispatcherConfigurator(Config config, IDispatcherPrerequisites prerequisites)
            : base(config, prerequisites)
        {
            // Need to see if a non-zero value is available for this setting
            TimeSpan deadlineTime = config.GetTimeSpan("throughput-deadline-time");
            long? deadlineTimeTicks = null;
            if (deadlineTime.Ticks > 0)
                deadlineTimeTicks = deadlineTime.Ticks;

            _instance = new Dispatcher(this, config.GetString("id"), 
                config.GetInt("throughput"),
                deadlineTimeTicks,
                ConfigureExecutor(),
                config.GetTimeSpan("shutdown-timeout"));
        }


        /// <summary>
        /// Returns a <see cref="MessageDispatcherConfigurator.Dispatcher"/> instance.
        /// 
        /// Whether or not this <see cref="MessageDispatcherConfigurator"/> returns a new instance 
        /// or returns a reference to an existing instance is an implementation detail of the
        /// underlying implementation.
        /// </summary>
        /// <returns></returns>
        public override MessageDispatcher Dispatcher()
        {
            return _instance;
        }
    }
}

