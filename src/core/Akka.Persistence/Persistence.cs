//-----------------------------------------------------------------------
// <copyright file="Persistence.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Util.Internal;
using System.Reflection;

namespace Akka.Persistence
{
    internal struct PluginHolder
    {
        public readonly IActorRef Ref;
        public readonly EventAdapters Adapters;
        public readonly Config Config;

        public PluginHolder(IActorRef @ref, EventAdapters adapters, Config config)
        {
            Ref = @ref;
            Adapters = adapters;
            Config = config;
        }
    }

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

        private readonly ConcurrentDictionary<string, Lazy<PluginHolder>> _pluginExtensionIds = new ConcurrentDictionary<string, Lazy<PluginHolder>>();

        private const string JournalFallbackConfigPath = "akka.persistence.journal-plugin-fallback";
        private const string SnapshotStoreFallbackConfigPath = "akka.persistence.snapshot-store-plugin-fallback";

        public PersistenceExtension(ExtendedActorSystem system)
        {
            _system = system;
            _system.Settings.InjectTopLevelFallback(Persistence.DefaultConfig());
            _config = system.Settings.Config.GetConfig("akka.persistence");

            _log = Logging.GetLogger(_system, this);

            _defaultJournalPluginId = new Lazy<string>(() =>
            {
                var configPath = _config.GetString("journal.plugin");
                if (string.IsNullOrEmpty(configPath)) throw new NullReferenceException("Default journal plugin is not configured");
                return configPath;
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            _defaultSnapshotPluginId = new Lazy<string>(() =>
            {
                var configPath = _config.GetString("snapshot-store.plugin");
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
                var configuratorTypeName = _config.GetString("internal-stash-overflow-strategy");
                var configuratorType = Type.GetType(configuratorTypeName);
                return ((IStashOverflowStrategyConfigurator) configuratorType).Create(_system.Settings.Config);
            });

            Settings = new PersistenceSettings(_system, _config);

            _config.GetStringList("journal.auto-start-journals").ForEach(id =>
            {
                if (_log.IsInfoEnabled)
                    _log.Info("Auto-starting journal plugin `{0}`", id);
                JournalFor(id);
            });

            _config.GetStringList("journal.auto-start-snapshot-stores").ForEach(id =>
            {
                if (_log.IsInfoEnabled)
                    _log.Info("Auto-starting snapshot store `{0}`", id);
                SnapshotStoreFor(id);
            });
        }

        public IStashOverflowStrategy DefaultInternalStashOverflowStrategy
        {
            get { return _defaultInternalStashOverflowStrategy.Value; }
        }

        public PersistenceSettings Settings { get; private set; }

        public string PersistenceId(IActorRef actor)
        {
            return actor.Path.ToStringWithoutAddress();
        }

        /// <summary>
        /// Returns an <see cref="EventAdapters"/> object which serves as a per-journal collection of bound event adapters. 
        /// If no adapters are registered for a given journal the EventAdapters object will simply return the identity adapter for each 
        /// class, otherwise the most specific adapter matching a given class will be returned.
        /// </summary>
        public EventAdapters AdaptersFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;

            return PluginHolderFor(configPath, JournalFallbackConfigPath).Adapters;
        }

        /// <summary>
        /// Looks up <see cref="EventAdapters"/> by journal plugin's ActorRef.
        /// </summary>
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
        internal Config JournalConfigFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;
            return PluginHolderFor(configPath, JournalFallbackConfigPath).Config;
        }

        /// <summary>
        /// Looks up the plugin config by plugin's ActorRef.
        /// </summary>
        internal Config ConfigFor(IActorRef journalPluginActor)
        {
            var extension = _pluginExtensionIds.Values
                .FirstOrDefault(e => e.Value.Ref.Equals(journalPluginActor));
            if (extension == null)
                throw new ArgumentException(string.Format("Unknown plugin actor {0}", journalPluginActor));

            return extension.Value.Config;
        }

        /// <summary>
        /// Returns a journal plugin actor identified by <paramref name="journalPluginId"/>.
        /// When empty, looks in `akka.persistence.journal.plugin` to find configuration entry path.
        /// When configured, uses <paramref name="journalPluginId"/> as absolute path to the journal configuration entry.
        /// Configuration entry must contain few required fields, such as `class`. See `persistence.conf`.
        /// </summary>
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
            var pluginTypeName = pluginConfig.GetString("class");
            if (string.IsNullOrEmpty(pluginTypeName))
                throw new ArgumentException(string.Format("Plugin class name must be defined in config property [{0}.class]", configPath));
            var pluginType = Type.GetType(pluginTypeName, true);
            var pluginDispatcherId = pluginConfig.GetString("plugin-dispatcher");
            object[] pluginActorArgs = pluginType.GetTypeInfo().GetConstructor(new[] {typeof (Config)}) != null ? new object[] {pluginConfig} : null;
            var pluginActorProps = new Props(pluginType, pluginActorArgs).WithDispatcher(pluginDispatcherId);

            return system.SystemActorOf(pluginActorProps, pluginActorName);
        }

        private static EventAdapters CreateAdapters(ExtendedActorSystem system, string configPath)
        {
            var pluginConfig = system.Settings.Config.GetConfig(configPath);
            return EventAdapters.Create(system, pluginConfig);
        }

        private static PluginHolder NewPluginHolder(ExtendedActorSystem system, string configPath, string fallbackPath)
        {
            if (string.IsNullOrEmpty(configPath) || !system.Settings.Config.HasPath(configPath))
            {
                throw new ArgumentException("Persistence config is missing plugin config path for: " + configPath);
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
            public AtLeastOnceDeliverySettings(TimeSpan redeliverInterval, int redeliveryBurstLimit,
                int warnAfterNumberOfUnconfirmedAttempts, int maxUnconfirmedMessages)
            {
                RedeliverInterval = redeliverInterval;
                RedeliveryBurstLimit = redeliveryBurstLimit;
                WarnAfterNumberOfUnconfirmedAttempts = warnAfterNumberOfUnconfirmedAttempts;
                MaxUnconfirmedMessages = maxUnconfirmedMessages;
            }

            public AtLeastOnceDeliverySettings(Config config)
            {
                RedeliverInterval = config.GetTimeSpan("at-least-once-delivery.redeliver-interval");
                MaxUnconfirmedMessages = config.GetInt("at-least-once-delivery.max-unconfirmed-messages");
                WarnAfterNumberOfUnconfirmedAttempts = config.GetInt("at-least-once-delivery.warn-after-number-of-unconfirmed-attempts");
                RedeliveryBurstLimit = config.GetInt("at-least-once-delivery.redelivery-burst-limit");
            }

            /// <summary>
            ///     Interval between redelivery attempts.
            /// </summary>
            public TimeSpan RedeliverInterval { get; private set; }

            /// <summary>
            ///     Maximum number of unconfirmed messages, that this actor is allowed to hold in the memory. When this
            ///     number is exceed, <see cref="AtLeastOnceDeliverySemantic.Deliver" /> will throw
            ///     <see cref="MaxUnconfirmedMessagesExceededException" />
            ///     instead of accepting messages.
            /// </summary>
            public int MaxUnconfirmedMessages { get; private set; }

            /// <summary>
            ///     After this number of delivery attempts a <see cref="UnconfirmedWarning" /> message will be sent to
            ///     <see cref="ActorBase.Self" />.
            ///     The count is reset after restart.
            /// </summary>
            public int WarnAfterNumberOfUnconfirmedAttempts { get; private set; }
            /// <summary>
            ///     Maximum number of unconfirmed messages that will be sent at each redelivery burst. This is to help to
            ///     prevent overflowing amount of messages to be sent at once, for eg. when destination cannot be reached for a long
            ///     time.
            /// </summary>
            public int RedeliveryBurstLimit { get; private set; }


            public AtLeastOnceDeliverySettings WithRedeliverInterval(TimeSpan redeliverInterval)
            {
                return Copy(redeliverInterval);
            }

            public AtLeastOnceDeliverySettings WithMaxUnconfirmedMessages(int maxUnconfirmedMessages)
            {
                return Copy(null, null, null, maxUnconfirmedMessages);
            }

            public AtLeastOnceDeliverySettings WithRedeliveryBurstLimit(int redeliveryBurstLimit)
            {
                return Copy(null, redeliveryBurstLimit);
            }

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
            View = new ViewSettings(config);
            AtLeastOnceDelivery = new AtLeastOnceDeliverySettings(config);
            Internal = new InternalSettings(config);
        }
    }

    public interface IPersistenceRecovery
    {
        /// <summary>
        /// Called when the persistent actor is started for the first time.
        /// The returned <see cref="Akka.Persistence.Recovery"/> object defines how the actor
        /// will recover its persistent state behore handling the first incoming message.
        /// 
        /// To skip recovery completely return <see cref="Akka.Persistence.Recovery.None"/>.
        /// </summary>
        Recovery Recovery { get; }
    }

    public interface IPersistenceStash : IWithUnboundedStash
    {
        /// <summary>
        /// The returned <see cref="IStashOverflowStrategy"/> object determines how to handle the message
        /// failed to stash when the internal Stash capacity exceeded.
        /// </summary>
        IStashOverflowStrategy InternalStashOverflowStrategy { get; }
    }

    public interface IJournalPlugin
    {
        string JournalPath { get; }
        Config DefaultConfig { get; }
    }
}

