﻿//-----------------------------------------------------------------------
// <copyright file="Persistence.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Persistence.Journal;

namespace Akka.Persistence
{
    internal struct PluginHolder
    {
        public readonly IActorRef Ref;
        public readonly EventAdapters Adapters;

        public PluginHolder(IActorRef @ref, EventAdapters adapters)
        {
            Ref = @ref;
            Adapters = adapters;
        }
    }

    public class PersistenceExtension : IExtension
    {
        private const string DefaultPluginDispatcherId = "akka.persistence.dispatchers.default-plugin-dispatcher";

        private readonly Config _config;
        private readonly ExtendedActorSystem _system;

        // both defaults are lazy, so that they don't need to be configured if they're not used
        private readonly Lazy<string> _defaultJournalPluginId;
        private readonly Lazy<string> _defaultSnapshotPluginId;

        private readonly ConcurrentDictionary<string, Lazy<PluginHolder>> _journalPluginExtensionIds = new ConcurrentDictionary<string, Lazy<PluginHolder>>();
        private readonly ConcurrentDictionary<string, Lazy<PluginHolder>> _snapshotPluginExtensionIds = new ConcurrentDictionary<string, Lazy<PluginHolder>>();

        public PersistenceExtension(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(Persistence.DefaultConfig());
            _config = system.Settings.Config.GetConfig("akka.persistence");

            _defaultJournalPluginId = new Lazy<string>(() =>
            {
                var configPath = _config.GetString("journal.plugin");
                if (string.IsNullOrEmpty(configPath)) throw new NullReferenceException("Default journal plugin is not configured");
                return configPath;
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            _defaultSnapshotPluginId = new Lazy<string>(() =>
            {
                var configPath = _config.GetString("snapshot-store.plugin");
                if (string.IsNullOrEmpty(configPath)) throw new NullReferenceException("Default snapshot-store plugin is not configured");
                return configPath;
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            Settings = new PersistenceSettings(_system, _config);
        }

        public PersistenceSettings Settings { get; private set; }

        public string PersistenceId(IActorRef actor)
        {
            return actor.Path.ToStringWithoutAddress();
        }

        /// <summary>
        /// Returns a snapshot store plugin actor identified by <paramref name="snapshotPluginId"/>. 
        /// When empty looks for default path under "akka.persistence.snapshot-store.plugin".
        /// </summary>
        public IActorRef SnapshotStoreFor(string snapshotPluginId)
        {
            var configPath = string.IsNullOrEmpty(snapshotPluginId) ? _defaultSnapshotPluginId.Value : snapshotPluginId;
            Lazy<PluginHolder> pluginContainer;
            if (!_snapshotPluginExtensionIds.TryGetValue(configPath, out pluginContainer))
            {
                var plugin = new Lazy<PluginHolder>(() => CreatePlugin(configPath, _ => DefaultPluginDispatcherId), LazyThreadSafetyMode.ExecutionAndPublication);
                pluginContainer = _snapshotPluginExtensionIds.AddOrUpdate(configPath, plugin, (key, old) => plugin);
            }

            return pluginContainer.Value.Ref;
        }

        /// <summary>
        /// Returns a journal plugin actor identified by <paramref name="journalPluginId"/>. 
        /// When empty looks for default path under "akka.persistence.journal.plugin".
        /// </summary>
        public IActorRef JournalFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;
            Lazy<PluginHolder> pluginContainer;
            if (!_journalPluginExtensionIds.TryGetValue(configPath, out pluginContainer))
            {
                var plugin = new Lazy<PluginHolder>(() => CreatePlugin(configPath, type =>
                    typeof (AsyncWriteJournal).IsAssignableFrom(type)
                        ? Dispatchers.DefaultDispatcherId
                        : DefaultPluginDispatcherId), 
                        LazyThreadSafetyMode.ExecutionAndPublication);
                pluginContainer = _journalPluginExtensionIds.AddOrUpdate(configPath, plugin, (key, old) => plugin);
            }

            return pluginContainer.Value.Ref;
        }

        /// <summary>
        /// Returns an <see cref="EventAdapters"/> object which serves as a per-journal collection of bound event adapters. 
        /// If no adapters are registered for a given journal the EventAdapters object will simply return the identity adapter for each 
        /// class, otherwise the most specific adapter matching a given class will be returned.
        /// </summary>
        /// <param name="journalPluginId"></param>
        /// <returns></returns>
        public EventAdapters AdaptersFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;
            Lazy<PluginHolder> pluginContainer;
            if (!_journalPluginExtensionIds.TryGetValue(configPath, out pluginContainer))
            {
                var plugin = new Lazy<PluginHolder>(() =>
                    CreatePlugin(configPath, type => typeof (AsyncWriteJournal).IsAssignableFrom(type)
                        ? Dispatchers.DefaultDispatcherId
                        : DefaultPluginDispatcherId), 
                        LazyThreadSafetyMode.ExecutionAndPublication);
                pluginContainer = _journalPluginExtensionIds.AddOrUpdate(configPath, plugin, (key, old) => plugin);
            }

            return pluginContainer.Value.Adapters;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="journalRef"></param>
        /// <returns></returns>
        internal EventAdapters AdaptersFor(IActorRef journalRef)
        {
            return _journalPluginExtensionIds.Values
                .Select(ext => Equals(ext.Value.Ref, journalRef) ? ext.Value.Adapters : null)
                .FirstOrDefault(r => r != null) 
                ?? IdentityEventAdapters.Instance;
        }

        private PluginHolder CreatePlugin(string configPath, Func<Type, string> dispatcherSelector)
        {
            if (string.IsNullOrEmpty(configPath) || !_system.Settings.Config.HasPath(configPath))
            {
                throw new ArgumentException("Persistence config is missing plugin config path for: " + configPath, "configPath");
            }

            var pluginConfig = _system.Settings.Config.GetConfig(configPath);
            var pluginTypeName = pluginConfig.GetString("class");
            var pluginType = Type.GetType(pluginTypeName, true);

            var shouldInjectConfig = pluginConfig.HasPath("inject-config") && pluginConfig.GetBoolean("inject-config");
            var pluginDispatcherId = pluginConfig.HasPath("plugin-dispatcher")
                ? pluginConfig.GetString("plugin-dispatcher")
                : dispatcherSelector(pluginType);
            var pluginActorArgs = shouldInjectConfig ? new object[] { pluginConfig } : null;
            var pluginActorProps = new Props(pluginType, pluginActorArgs).WithDispatcher(pluginDispatcherId);

            var pluginRef = _system.SystemActorOf(pluginActorProps, configPath);
            return new PluginHolder(pluginRef, CreateAdapters(configPath));
        }

        private EventAdapters CreateAdapters(string configPath)
        {
            var pluginConfig = _system.Settings.Config.GetConfig(configPath);
            return EventAdapters.Create(_system, pluginConfig);
        }
    }

    /// <summary>
    /// Persistence extension.
    /// </summary>
    public class Persistence : ExtensionIdProvider<PersistenceExtension>
    {
        public static readonly Persistence Instance = new Persistence();

        public override PersistenceExtension CreateExtension(ExtendedActorSystem system)
        {
            return new PersistenceExtension(system);
        }

        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf");
        }
    }

    /// <summary>
    /// Persistence configuration.
    /// </summary>
    public class PersistenceSettings : Settings
    {
        public JournalSettings Journal { get; private set; }
        public class JournalSettings
        {
            public JournalSettings(Config config)
            {
                MaxMessageBatchSize = config.GetInt("journal.max-message-batch-size");
                MaxConfirmationBatchSize = config.GetInt("journal.max-confirmation-batch-size");
                MaxDeletionBatchSize = config.GetInt("journal.max-deletion-batch-size");
            }

            public int MaxConfirmationBatchSize { get; private set; }

            public int MaxDeletionBatchSize { get; private set; }

            public int MaxMessageBatchSize { get; private set; }
        }

        public ViewSettings View { get; private set; }
        public class ViewSettings
        {
            public ViewSettings(Config config)
            {
                AutoUpdate = config.GetBoolean("view.auto-update");
                AutoUpdateInterval = config.GetTimeSpan("view.auto-update-interval");
                var repMax = config.GetLong("view.auto-update-replay-max");
                AutoUpdateReplayMax = repMax < 0 ? long.MaxValue : repMax;
            }

            public bool AutoUpdate { get; private set; }
            public TimeSpan AutoUpdateInterval { get; private set; }
            public long AutoUpdateReplayMax { get; private set; }
        }

        public AtLeastOnceDeliverySettings AtLeastOnceDelivery { get; set; }
        public class AtLeastOnceDeliverySettings
        {
            public AtLeastOnceDeliverySettings(Config config)
            {
                RedeliverInterval = config.GetTimeSpan("at-least-once-delivery.redeliver-interval");
                MaxUnconfirmedMessages = config.GetInt("at-least-once-delivery.max-unconfirmed-messages");
                UnconfirmedAttemptsToWarn = config.GetInt("at-least-once-delivery.warn-after-number-of-unconfirmed-attempts");
                RedeliveryBurstLimit = config.GetInt("at-least-once-delivery.redelivery-burst-limit");
            }

            public TimeSpan RedeliverInterval { get; private set; }
            public int MaxUnconfirmedMessages { get; private set; }
            public int UnconfirmedAttemptsToWarn { get; private set; }
            public int RedeliveryBurstLimit { get; private set; }
        }

        public InternalSettings Internal { get; private set; }
        public class InternalSettings
        {
            public InternalSettings(Config config)
            {
                PublishPluginCommands = config.HasPath("publish-plugin-commands") && config.GetBoolean("publish-plugin-commands");
                PublishConfirmations = config.HasPath("publish-confirmations") && config.GetBoolean("publish-confirmations");
            }

            public bool PublishPluginCommands { get; private set; }
            public bool PublishConfirmations { get; private set; }
        }

        public PersistenceSettings(ActorSystem system, Config config)
            : base(system, config)
        {
            Journal = new JournalSettings(config);
            View = new ViewSettings(config);
            AtLeastOnceDelivery = new AtLeastOnceDeliverySettings(config);
            Internal = new InternalSettings(config);
        }
    }
}

