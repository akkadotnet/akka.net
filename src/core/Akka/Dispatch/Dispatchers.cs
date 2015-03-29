﻿/**
 * Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
 * Original C# code written by Akka.NET project <http://getakka.net/>
 */
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Dispatch
{
    /// <summary>
    ///     Class ThreadPoolDispatcher.
    /// </summary>
    public class ThreadPoolDispatcher : MessageDispatcher
    {
        /// <summary>
        /// Takes a <see cref="MessageDispatcherConfigurator"/>
        /// </summary>
        public ThreadPoolDispatcher(MessageDispatcherConfigurator configurator) : base(configurator)
        {
        }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public override void Schedule(Action run)
        {
            var wc = new WaitCallback(_ => run());
            ThreadPool.UnsafeQueueUserWorkItem(wc, null);
            //ThreadPool.QueueUserWorkItem(wc, null);
        }
    }

    /// <summary>
    ///     Dispatcher that dispatches messages on the current synchronization context, e.g. WinForms or WPF GUI thread
    /// </summary>
    public class CurrentSynchronizationContextDispatcher : MessageDispatcher
    {
        /// <summary>
        ///     The scheduler
        /// </summary>
        private readonly TaskScheduler _scheduler;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CurrentSynchronizationContextDispatcher" /> class.
        /// </summary>
        public CurrentSynchronizationContextDispatcher(MessageDispatcherConfigurator configurator)
            : base(configurator)
        {
            _scheduler = TaskScheduler.FromCurrentSynchronizationContext();
        }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public override void Schedule(Action run)
        {
            var t = new Task(run);
            t.Start(_scheduler);
        }
    }

    /// <summary>
    ///     Class SingleThreadDispatcher.
    /// </summary>
    public class SingleThreadDispatcher : MessageDispatcher
    {
        /// <summary>
        ///     The queue
        /// </summary>
        private readonly BlockingCollection<Action> queue = new BlockingCollection<Action>();

        /// <summary>
        ///     The running
        /// </summary>
        private volatile bool running = true;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SingleThreadDispatcher" /> class.
        /// </summary>
        public SingleThreadDispatcher(MessageDispatcherConfigurator configurator)
            : base(configurator)
        {
            var thread = new Thread(_ =>
            {
                foreach (var next in queue.GetConsumingEnumerable())
                {
                    next();
                    if (!running) return;
                }
            });
            thread.Start(); //thread won't start automatically without this
        }

        /// <summary>
        ///     Schedules the specified run.
        /// </summary>
        /// <param name="run">The run.</param>
        public override void Schedule(Action run)
        {
            queue.Add(run);
        }
    }

    /// <summary>
    /// The registry of all <see cref="MessageDispatcher"/> instances available to this <see cref="ActorSystem"/>.
    /// </summary>
    public class Dispatchers
    {
        /// <summary>
        ///     The default dispatcher identifier, also the full key of the configuration of the default dispatcher.
        /// </summary>
        public readonly static string DefaultDispatcherId = "akka.actor.default-dispatcher";
        public readonly static string SynchronizedDispatcherId = "akka.actor.synchronized-dispatcher";

        private readonly ActorSystem _system;
        private CachingConfig _cachingConfig;
        private readonly MessageDispatcher _defaultGlobalDispatcher;

        /// <summary>
        /// The list of all configurators used to create <see cref="MessageDispatcher"/> instances.
        /// 
        /// Has to be thread-safe, as this collection can be accessed concurrently by many actors.
        /// </summary>
        private ConcurrentDictionary<string, MessageDispatcherConfigurator> _dispatcherConfigurators = new ConcurrentDictionary<string, MessageDispatcherConfigurator>();

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
        /// Checks that configuration provides a sectionfor the given dispatcher.
        /// This does not gaurantee that no <see cref="ConfigurationException"/> will be thrown
        /// when using the dispatcher, because the details can only be checked by trying to
        /// instantiate it, which might be undersirable when just checking.
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
        /// <exception cref="ConfigurationException">if the `id` property is missing from <see cref="cfg"/></exception>
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
        /// This enables dynamic addtition of dispatchers.
        /// 
        /// <remarks>
        /// A <see cref="MessageDispatcherConfigurator"/> for a certain id can only be registered once,
        /// i.e. it can not be replaced. It is safe to call this method multiple times, but only the
        /// first registration will be used.
        /// </remarks>
        /// </summary>
        /// <returns>This method returns <c>true</c> if the specified configurator was successfully regisetered.</returns>
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

        private MessageDispatcherConfigurator ConfiguratorFrom(Config cfg)
        {
            if(!cfg.HasPath("id")) throw new ConfigurationException(string.Format("Missing dispatcher `id` property in config: {0}", cfg.Root));

            var id = cfg.GetString("id");
            var type = cfg.GetString("type");
            var throughput = cfg.GetInt("throughput");
            var throughputDeadlineTime = cfg.GetTimeSpan("throughput-deadline-time").Ticks;


            MessageDispatcherConfigurator dispatcher;
            switch (type)
            {
                case "Dispatcher":
                    dispatcher = new ThreadPoolDispatcherConfigurator(cfg, Prerequisites);
                    break;
                case "TaskDispatcher":
                    dispatcher = new TaskDispatcherConfigurator(cfg, Prerequisites);
                    break;
                case "PinnedDispatcher":
                    dispatcher = new PinnedDispatcherConfigurator(cfg, Prerequisites);
                    break;
                case "ForkJoinDispatcher":
                    dispatcher = new ForkJoinDispatcherConfigurator(cfg, Prerequisites);
                    break;
                case "SynchronizedDispatcher":
                    dispatcher = new CurrentSynchronizationContextDispatcherConfigurator(cfg, Prerequisites);
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

            return new DispatcherConfigurator(dispatcher, id, throughput, throughputDeadlineTime);
        }
    }

    /// <summary>
    /// The cached <see cref="MessageDispatcher"/> factory that gets looked up via configuration
    /// inside <see cref="Dispatchers"/>
    /// </summary>
    class DispatcherConfigurator : MessageDispatcherConfigurator
    {
        public string Id { get; private set; }

        private readonly MessageDispatcherConfigurator _configurator;

        public DispatcherConfigurator(MessageDispatcherConfigurator configurator, string id, int throughput, long? throughputDeadlineTime)
            : base(configurator.Config, configurator.Prerequisites)
        {
            _configurator = configurator;
            ThroughputDeadlineTime = throughputDeadlineTime;
            Id = id;
            Throughput = throughput;
        }

        public int Throughput { get; private set; }

        public long? ThroughputDeadlineTime { get; private set; }
        public override MessageDispatcher Dispatcher()
        {
            var dispatcher = _configurator.Dispatcher();
            dispatcher.Id = Id;
            dispatcher.Throughput = Throughput;
            dispatcher.ThroughputDeadlineTime = ThroughputDeadlineTime > 0 ? ThroughputDeadlineTime : null;
            return dispatcher;
        }
    }
}