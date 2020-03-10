//-----------------------------------------------------------------------
// <copyright file="Persistence.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Util.Internal;

namespace Akka.Persistence
{
    internal struct PluginHolder
    {
        public PluginHolder(IActorRef @ref, EventAdapters adapters, Config config)
        {
            Ref = @ref;
            Adapters = adapters;
            Config = config;
        }

        public IActorRef Ref { get; }

        public EventAdapters Adapters { get; }

        public Config Config { get; }
    }

    /// <summary>
    /// Launches the Akka.Persistence runtime
    /// </summary>
    public class PersistenceExtension : IExtension
    {
        private const string NoSnapshotStorePluginId = "akka.persistence.no-snapshot-store";

        private readonly Config _config;
        private readonly ExtendedActorSystem _system;

        private readonly ILoggingAdapter _log;
        // all defaults are lazy, so that they don't need to be configured if they're not used
        private readonly Lazy<string> _defaultJournalPluginId;
        private readonly Lazy<string> _defaultSnapshotPluginId;
        private readonly Lazy<IStashOverflowStrategy> _defaultInternalStashOverflowStrategy;
        private readonly Lazy<IActorRef> _recoveryPermitter;

        private readonly ConcurrentDictionary<string, Lazy<PluginHolder>> _pluginExtensionIds = new ConcurrentDictionary<string, Lazy<PluginHolder>>();

        private const string JournalFallbackConfigPath = "akka.persistence.journal-plugin-fallback";
        private const string SnapshotStoreFallbackConfigPath = "akka.persistence.snapshot-store-plugin-fallback";

        /// <summary>
        /// Creates a new Akka.Persistence extension.
        /// </summary>
        /// <param name="system">The ActorSystem that will be using Akka.Persistence</param>
        /// <exception cref="NullReferenceException">
        /// This exception is thrown when the default journal plugin, <c>journal.plugin</c> is not configured.
        /// </exception>
        /// <remarks>
        /// DO NOT CALL DIRECTLY. Will be instantiated automatically be Akka.Persistence actors.
        /// </remarks>
        public PersistenceExtension(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(Persistence.DefaultConfig());
            _config = system.Settings.Config.GetConfig("akka.persistence");
            if (_config.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<PersistenceExtension>("akka.persistence");

            _log = Logging.GetLogger(_system, this);

            _defaultJournalPluginId = new Lazy<string>(() =>
            {
                var configPath = _config.GetString("journal.plugin", null);
                if (string.IsNullOrEmpty(configPath)) throw new NullReferenceException("Default journal plugin is not configured");
                return configPath;
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            _defaultSnapshotPluginId = new Lazy<string>(() =>
            {
                var configPath = _config.GetString("snapshot-store.plugin", null);
                if (string.IsNullOrEmpty(configPath))
                {
                    if (_log.IsWarningEnabled)
                        _log.Warning("No default snapshot store configured! " +
                            "To configure a default snapshot-store plugin set the `akka.persistence.snapshot-store.plugin` key. " +
                            "For details see 'persistence.conf'");
                    return NoSnapshotStorePluginId;
                }
                return configPath;
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            _defaultInternalStashOverflowStrategy = new Lazy<IStashOverflowStrategy>(() =>
            {
                var configuratorTypeName = _config.GetString("internal-stash-overflow-strategy", null);
                var configuratorType = Type.GetType(configuratorTypeName);
                return ((IStashOverflowStrategyConfigurator)Activator.CreateInstance(configuratorType)).Create(_system.Settings.Config);
            });

            Settings = new PersistenceSettings(_system, _config);

            _config.GetStringList("journal.auto-start-journals", new string[] { }).ForEach(id =>
            {
                if (_log.IsInfoEnabled)
                    _log.Info("Auto-starting journal plugin `{0}`", id);
                JournalFor(id);
            });

            _config.GetStringList("snapshot-store.auto-start-snapshot-stores", new string[] { }).ForEach(id =>
            {
                if (_log.IsInfoEnabled)
                    _log.Info("Auto-starting snapshot store `{0}`", id);
                SnapshotStoreFor(id);
            });

            _recoveryPermitter = new Lazy<IActorRef>(() =>
            {
                var maxPermits = _config.GetInt("max-concurrent-recoveries", 0);
                return _system.SystemActorOf(Akka.Persistence.RecoveryPermitter.Props(maxPermits), "recoveryPermitter");
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IStashOverflowStrategy DefaultInternalStashOverflowStrategy => _defaultInternalStashOverflowStrategy.Value;

        /// <summary>
        /// The Akka.Persistence settings for the journal and snapshot store
        /// </summary>
        public PersistenceSettings Settings { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        /// <returns>TBD</returns>
        public string PersistenceId(IActorRef actor)
        {
            return actor.Path.ToStringWithoutAddress();
        }

        /// <summary>
        /// INTERNAL API: When starting many persistent actors at the same time the journal its data store is protected 
        /// from being overloaded by limiting number of recoveries that can be in progress at the same time.
        /// </summary>
        internal IActorRef RecoveryPermitter()
        {
            return _recoveryPermitter.Value;
        }

        /// <summary>
        /// Returns an <see cref="EventAdapters"/> object which serves as a per-journal collection of bound event adapters. 
        /// If no adapters are registered for a given journal the EventAdapters object will simply return the identity adapter for each 
        /// class, otherwise the most specific adapter matching a given class will be returned.
        /// </summary>
        /// <param name="journalPluginId">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>TBD</returns>
        public EventAdapters AdaptersFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;

            return PluginHolderFor(configPath, JournalFallbackConfigPath).Adapters;
        }

        /// <summary>
        /// Looks up <see cref="EventAdapters"/> by journal plugin's ActorRef.
        /// </summary>
        /// <param name="journalPluginActor">TBD</param>
        /// <returns>TBD</returns>
        internal EventAdapters AdaptersFor(IActorRef journalPluginActor)
        {
            var extension = _pluginExtensionIds.Values
                .FirstOrDefault(e => e.Value.Ref.Equals(journalPluginActor));

            return extension != null ? extension.Value.Adapters : IdentityEventAdapters.Instance;
        }

        /// <summary>
        /// Returns the plugin config identified by <paramref name="journalPluginId"/>.
        /// When empty, looks in `akka.persistence.journal.plugin` to find the configuration entry path.
        /// When configured, uses <paramref name="journalPluginId"/> as absolute path to the journal configuration entry.
        /// </summary>
        /// <param name="journalPluginId">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>TBD</returns>
        internal Config JournalConfigFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;
            return PluginHolderFor(configPath, JournalFallbackConfigPath).Config;
        }

        /// <summary>
        /// Looks up the plugin config by plugin's ActorRef.
        /// </summary>
        /// <param name="journalPluginActor">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="journalPluginActor"/> is unknown.
        /// </exception>
        /// <returns>TBD</returns>
        internal Config ConfigFor(IActorRef journalPluginActor)
        {
            var extension = _pluginExtensionIds.Values
                .FirstOrDefault(e => e.Value.Ref.Equals(journalPluginActor));
            if (extension == null)
                throw new ArgumentException($"Unknown plugin actor {journalPluginActor}");

            return extension.Value.Config;
        }

        /// <summary>
        /// Returns a journal plugin actor identified by <paramref name="journalPluginId"/>.
        /// When empty, looks in `akka.persistence.journal.plugin` to find configuration entry path.
        /// When configured, uses <paramref name="journalPluginId"/> as absolute path to the journal configuration entry.
        /// Configuration entry must contain few required fields, such as `class`. See `persistence.conf`.
        /// </summary>
        /// <param name="journalPluginId">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>TBD</returns>
        public IActorRef JournalFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;

            return PluginHolderFor(configPath, JournalFallbackConfigPath).Ref;
        }

        /// <summary>
        /// Returns a snapshot store plugin actor identified by <paramref name="snapshotPluginId"/>. 
        /// When empty, looks in `akka.persistence.snapshot-store.plugin` to find configuration entry path.
        /// When configured, uses <paramref name="snapshotPluginId"/> as absolute path to the snapshot store configuration entry.
        /// Configuration entry must contain few required fields, such as `class`. See `persistence.conf`.
        /// </summary>
        /// <param name="snapshotPluginId">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>TBD</returns>
        public IActorRef SnapshotStoreFor(string snapshotPluginId)
        {
            var configPath = string.IsNullOrEmpty(snapshotPluginId) ? _defaultSnapshotPluginId.Value : snapshotPluginId;

            return PluginHolderFor(configPath, SnapshotStoreFallbackConfigPath).Ref;
        }


        private PluginHolder PluginHolderFor(string configPath, string fallbackPath)
        {
            var pluginContainer = _pluginExtensionIds.GetOrAdd(configPath,
                cp =>
                    new Lazy<PluginHolder>(() => NewPluginHolder(_system, cp, fallbackPath),
                        LazyThreadSafetyMode.ExecutionAndPublication));

            return pluginContainer.Value;
        }

        private static IActorRef CreatePlugin(ExtendedActorSystem system, string configPath, Config pluginConfig)
        {
            var pluginActorName = configPath;
            var pluginTypeName = pluginConfig.GetString("class", null);
            if (string.IsNullOrEmpty(pluginTypeName))
                throw new ArgumentException($"Plugin class name must be defined in config property [{configPath}.class]");
            var pluginType = Type.GetType(pluginTypeName, true);
            var pluginDispatcherId = pluginConfig.GetString("plugin-dispatcher", null);
            object[] pluginActorArgs = pluginType.GetConstructor(new[] { typeof(Config) }) != null ? new object[] { pluginConfig } : null;
            var pluginActorProps = new Props(pluginType, pluginActorArgs).WithDispatcher(pluginDispatcherId);

            return system.SystemActorOf(pluginActorProps, pluginActorName);
        }

        private static EventAdapters CreateAdapters(ExtendedActorSystem system, string configPath)
        {
            var pluginConfig = system.Settings.Config.GetConfig(configPath);
            if (pluginConfig.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<EventAdapters>(configPath);

            return EventAdapters.Create(system, pluginConfig);
        }

        private static PluginHolder NewPluginHolder(ExtendedActorSystem system, string configPath, string fallbackPath)
        {
            if (string.IsNullOrEmpty(configPath) || !system.Settings.Config.HasPath(configPath))
            {
                throw new ArgumentException($"Persistence config is missing plugin config path for: {configPath}");
            }

            var config = system.Settings.Config.GetConfig(configPath).WithFallback(system.Settings.Config.GetConfig(fallbackPath));
            var plugin = CreatePlugin(system, configPath, config);
            var adapters = CreateAdapters(system, configPath);

            return new PluginHolder(plugin, adapters, config);
        }
    }

    /// <summary>
    /// Persistence extension.
    /// </summary>
    public class Persistence : ExtensionIdProvider<PersistenceExtension>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static Persistence Instance { get; } = new Persistence();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override PersistenceExtension CreateExtension(ExtendedActorSystem system)
        {
            return new PersistenceExtension(system);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<Persistence>("Akka.Persistence.persistence.conf");
        }
    }

    /// <summary>
    /// Persistence configuration.
    /// </summary>
    public sealed class PersistenceSettings : Settings
    {
        /// <summary>
        /// TBD
        /// </summary>
        public ViewSettings View { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ViewSettings
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="config">TBD</param>
            public ViewSettings(Config config)
            {
                AutoUpdate = config.GetBoolean("view.auto-update", false);
                AutoUpdateInterval = config.GetTimeSpan("view.auto-update-interval", null);
                var repMax = config.GetLong("view.auto-update-replay-max", 0);
                AutoUpdateReplayMax = repMax < 0 ? long.MaxValue : repMax;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public bool AutoUpdate { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public TimeSpan AutoUpdateInterval { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public long AutoUpdateReplayMax { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public AtLeastOnceDeliverySettings AtLeastOnceDelivery { get; set; }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class AtLeastOnceDeliverySettings
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="redeliverInterval">TBD</param>
            /// <param name="redeliveryBurstLimit">TBD</param>
            /// <param name="warnAfterNumberOfUnconfirmedAttempts">TBD</param>
            /// <param name="maxUnconfirmedMessages">TBD</param>
            public AtLeastOnceDeliverySettings(TimeSpan redeliverInterval, int redeliveryBurstLimit,
                int warnAfterNumberOfUnconfirmedAttempts, int maxUnconfirmedMessages)
            {
                RedeliverInterval = redeliverInterval;
                RedeliveryBurstLimit = redeliveryBurstLimit;
                WarnAfterNumberOfUnconfirmedAttempts = warnAfterNumberOfUnconfirmedAttempts;
                MaxUnconfirmedMessages = maxUnconfirmedMessages;
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="config">TBD</param>
            public AtLeastOnceDeliverySettings(Config config)
            {
                RedeliverInterval = config.GetTimeSpan("at-least-once-delivery.redeliver-interval", null);
                MaxUnconfirmedMessages = config.GetInt("at-least-once-delivery.max-unconfirmed-messages", 0);
                WarnAfterNumberOfUnconfirmedAttempts = config.GetInt("at-least-once-delivery.warn-after-number-of-unconfirmed-attempts", 0);
                RedeliveryBurstLimit = config.GetInt("at-least-once-delivery.redelivery-burst-limit", 0);
            }

            /// <summary>
            ///     Interval between redelivery attempts.
            /// </summary>
            public TimeSpan RedeliverInterval { get; }

            /// <summary>
            ///     Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory. When this
            ///     number is exceed, <see cref="AtLeastOnceDeliverySemantic.Deliver" /> will throw
            ///     <see cref="MaxUnconfirmedMessagesExceededException" />
            ///     instead of accepting messages.
            /// </summary>
            public int MaxUnconfirmedMessages { get; }

            /// <summary>
            ///     After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
            ///     <see cref="ActorBase.Self" />.
            ///     The count is reset after restart.
            /// </summary>
            public int WarnAfterNumberOfUnconfirmedAttempts { get; }

            /// <summary>
            ///     Maximum number of unconfirmed messages that will be sent at each redelivery burst. This is to help to
            ///     prevent overflowing amount of messages to be sent at once, for eg. when destination cannot be reached for a long
            ///     time.
            /// </summary>
            public int RedeliveryBurstLimit { get; }


            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="redeliverInterval">TBD</param>
            /// <returns>TBD</returns>
            public AtLeastOnceDeliverySettings WithRedeliverInterval(TimeSpan redeliverInterval)
            {
                return Copy(redeliverInterval);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="maxUnconfirmedMessages">TBD</param>
            /// <returns>TBD</returns>
            public AtLeastOnceDeliverySettings WithMaxUnconfirmedMessages(int maxUnconfirmedMessages)
            {
                return Copy(null, null, null, maxUnconfirmedMessages);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="redeliveryBurstLimit">TBD</param>
            /// <returns>TBD</returns>
            public AtLeastOnceDeliverySettings WithRedeliveryBurstLimit(int redeliveryBurstLimit)
            {
                return Copy(null, redeliveryBurstLimit);
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="unconfirmedAttemptsToWarn">TBD</param>
            /// <returns>TBD</returns>
            public AtLeastOnceDeliverySettings WithUnconfirmedAttemptsToWarn(int unconfirmedAttemptsToWarn)
            {
                return Copy(null, null, unconfirmedAttemptsToWarn);
            }

            private AtLeastOnceDeliverySettings Copy(TimeSpan? redeliverInterval = null, int? redeliveryBurstLimit = null,
                int? unconfirmedAttemptsToWarn = null, int? maxUnconfirmedMessages = null)
            {
                return new AtLeastOnceDeliverySettings(redeliverInterval ?? RedeliverInterval,
                    redeliveryBurstLimit ?? RedeliveryBurstLimit, unconfirmedAttemptsToWarn ?? WarnAfterNumberOfUnconfirmedAttempts,
                    maxUnconfirmedMessages ?? MaxUnconfirmedMessages);
            }
        }

        public InternalSettings Internal { get; }

        public sealed class InternalSettings
        {
            public InternalSettings(Config config)
            {
                PublishPluginCommands = config.HasPath("publish-plugin-commands") && config.GetBoolean("publish-plugin-commands", false);
                PublishConfirmations = config.HasPath("publish-confirmations") && config.GetBoolean("publish-confirmations", false);
            }

            public bool PublishPluginCommands { get; }

            public bool PublishConfirmations { get; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="config">TBD</param>
        public PersistenceSettings(ActorSystem system, Config config)
            : base(system, config)
        {
            View = new ViewSettings(config);
            AtLeastOnceDelivery = new AtLeastOnceDeliverySettings(config);
            Internal = new InternalSettings(config);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IPersistenceRecovery
    {
        /// <summary>
        /// Called when the persistent actor is started for the first time.
        /// The returned <see cref="Akka.Persistence.Recovery"/> object defines how the actor
        /// will recover its persistent state before handling the first incoming message.
        /// 
        /// To skip recovery completely return <see cref="Akka.Persistence.Recovery.None"/>.
        /// </summary>
        Recovery Recovery { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IPersistenceStash : IWithUnboundedStash
    {
        /// <summary>
        /// The returned <see cref="IStashOverflowStrategy"/> object determines how to handle the message
        /// failed to stash when the internal Stash capacity exceeded.
        /// </summary>
        IStashOverflowStrategy InternalStashOverflowStrategy { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface IJournalPlugin
    {
        /// <summary>
        /// TBD
        /// </summary>
        string JournalPath { get; }

        /// <summary>
        /// TBD
        /// </summary>
        Config DefaultConfig { get; }
    }
}
