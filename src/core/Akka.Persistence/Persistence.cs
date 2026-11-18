//-----------------------------------------------------------------------
// <copyright file="Persistence.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Annotations;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Persistence
{
    internal struct PluginHolder
    {
        public PluginHolder(IActorRef @ref, EventAdapters adapters, Config config, IActorRef recoveryPermitter)
        {
            Ref = @ref;
            Adapters = adapters;
            Config = config;
            RecoveryPermitter = recoveryPermitter;
        }

        public IActorRef Ref { get; }

        public EventAdapters Adapters { get; }

        public Config Config { get; }
        
        public IActorRef RecoveryPermitter { get; }
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
        private readonly PersistencePluginRegistry _registry;
        // all defaults are lazy, so that they don't need to be configured if they're not used
        private readonly Lazy<string> _defaultJournalPluginId;
        private readonly Lazy<string> _defaultSnapshotPluginId;
        private readonly Lazy<IStashOverflowStrategy> _defaultInternalStashOverflowStrategy;

        private readonly ConcurrentDictionary<string, Lazy<PluginHolder>> _pluginExtensionIds = new();

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
            _registry = PersistencePluginRegistry.For(_system);

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

                // reflection on the HOCON type name as always; with the switch off, the built-ins, then the guard
                if (AkkaFeatures.IsDynamicTypeLoadingSupported)
                    return CreateStashOverflowConfiguratorByReflection(configuratorTypeName).Create(_system.Settings.Config);

                if (BuiltInPersistencePlugins.TryCreateStashOverflowConfigurator(configuratorTypeName, out var builtIn))
                    return builtIn.Create(_system.Settings.Config);

                throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                    "akka.persistence.internal-stash-overflow-strategy",
                    configuratorTypeName,
                    "ThrowExceptionConfigurator or DiscardConfigurator, or override InternalStashOverflowStrategy on the persistent actor"));
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
        }

        /// <summary>
        /// Default overflow strategy used when an internal persistence stash exceeds its capacity.
        /// </summary>
        public IStashOverflowStrategy DefaultInternalStashOverflowStrategy => _defaultInternalStashOverflowStrategy.Value;

        /// <summary>
        /// The Akka.Persistence settings for the journal and snapshot store
        /// </summary>
        public PersistenceSettings Settings { get; }

        /// <summary>
        /// Returns the persistence identifier derived from an actor reference.
        /// </summary>
        /// <param name="actor">Actor whose path is used to derive the persistence identifier.</param>
        /// <returns>The actor path without its address.</returns>
        public string PersistenceId(IActorRef actor)
        {
            return actor.Path.ToStringWithoutAddress();
        }

        /// <summary>
        /// INTERNAL API: When starting many persistent actors at the same time the journal its data store is protected 
        /// from being overloaded by limiting number of recoveries that can be in progress at the same time.
        /// </summary>
        internal IActorRef RecoveryPermitterFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;
            return PluginHolderFor(configPath, JournalFallbackConfigPath).RecoveryPermitter;
        }

        /// <summary>
        /// Returns an <see cref="EventAdapters"/> object which serves as a per-journal collection of bound event adapters. 
        /// If no adapters are registered for a given journal the EventAdapters object will simply return the identity adapter for each 
        /// class, otherwise the most specific adapter matching a given class will be returned.
        /// </summary>
        /// <param name="journalPluginId">Configuration path of the journal plugin, or an empty string to use the default journal plugin.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>The event adapters configured for the journal plugin.</returns>
        public EventAdapters AdaptersFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;

            return PluginHolderFor(configPath, JournalFallbackConfigPath).Adapters;
        }

        /// <summary>
        /// Looks up <see cref="EventAdapters"/> by journal plugin's ActorRef.
        /// </summary>
        /// <param name="journalPluginActor">Actor reference of the journal plugin.</param>
        /// <returns>The event adapters registered for the journal plugin, or the identity adapters if the actor is not registered.</returns>
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
        /// <param name="journalPluginId">Configuration path of the journal plugin, or an empty string to use the default journal plugin.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>The effective configuration for the journal plugin.</returns>
        internal Config JournalConfigFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;
            return PluginHolderFor(configPath, JournalFallbackConfigPath).Config;
        }
        
        /// <summary>
        /// Returns the plugin config identified by <paramref name="snapshotPluginId"/>.
        /// When empty, looks in `akka.persistence.snapshot-store.plugin` to find configuration entry path.
        /// When configured, uses <paramref name="snapshotPluginId"/> as absolute path to the journal configuration entry.
        /// </summary>
        /// <param name="snapshotPluginId">Configuration path of the snapshot store plugin, or an empty string to use the default snapshot store plugin.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>The effective configuration for the snapshot store plugin.</returns>
        internal Config SnapshotStoreConfigFor(string snapshotPluginId)
        {
            var configPath = string.IsNullOrEmpty(snapshotPluginId) ? _defaultSnapshotPluginId.Value : snapshotPluginId;
            return PluginHolderFor(configPath, SnapshotStoreFallbackConfigPath).Config;
        }

        /// <summary>
        /// Looks up the plugin config by plugin's ActorRef.
        /// </summary>
        /// <param name="journalPluginActor">Actor reference of a registered persistence plugin.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="journalPluginActor"/> is unknown.
        /// </exception>
        /// <returns>The configuration associated with the plugin actor.</returns>
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
        /// <param name="journalPluginId">Configuration path of the journal plugin, or an empty string to use the default journal plugin.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>Actor reference of the journal plugin.</returns>
        [InternalStableApi]
        public IActorRef JournalFor(string journalPluginId)
        {
            var configPath = string.IsNullOrEmpty(journalPluginId) ? _defaultJournalPluginId.Value : journalPluginId;

            return PluginHolderFor(configPath, JournalFallbackConfigPath).Ref;
        }

        /// <summary>
        /// Shortcut for invoking journal health checks.
        /// </summary>
        /// <param name="journalPluginId">The HOCON id of the Akka.Persistence plugin./</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        /// <returns>A <see cref="PersistenceHealthCheckResult"/> with health status and possibly a descriptive message.</returns>
        public async Task<PersistenceHealthCheckResult> CheckJournalHealthAsync(string journalPluginId,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Settings.AskTimeout);
            
            var pluginRef = JournalFor(journalPluginId);
            var r = await pluginRef.Ask<JournalHealthCheckResponse>(new CheckJournalHealth(timeoutCts.Token), timeoutCts.Token);
            return r.Result;
        }

        /// <summary>
        /// Shortcut for invoking snapshot store health checks.
        /// </summary>
        /// <param name="snapshotStorePluginId">The HOCON id of the Akka.Persistence plugin.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        /// <returns>A <see cref="PersistenceHealthCheckResult"/> with health status and possibly a descriptive message.</returns>
        public async Task<PersistenceHealthCheckResult> CheckSnapshotStoreHealthAsync(string snapshotStorePluginId,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Settings.AskTimeout);
            
            var pluginRef = SnapshotStoreFor(snapshotStorePluginId);
            var r = await pluginRef.Ask<SnapshotStoreHealthCheckResponse>(new CheckSnapshotStoreHealth(timeoutCts.Token), timeoutCts.Token);
            return r.Result;
        }

        /// <summary>
        /// Returns a snapshot store plugin actor identified by <paramref name="snapshotPluginId"/>. 
        /// When empty, looks in `akka.persistence.snapshot-store.plugin` to find configuration entry path.
        /// When configured, uses <paramref name="snapshotPluginId"/> as absolute path to the snapshot store configuration entry.
        /// Configuration entry must contain few required fields, such as `class`. See `persistence.conf`.
        /// </summary>
        /// <param name="snapshotPluginId">Configuration path of the snapshot store plugin, or an empty string to use the default snapshot store plugin.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when either the plugin class name is undefined or the configuration path is missing.
        /// </exception>
        /// <returns>Actor reference of the snapshot store plugin.</returns>
        [InternalStableApi]
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

        private static IActorRef CreateRecoveryPermitter(ExtendedActorSystem system, string configPath, Config pluginConfig)
        {
            // backward compatibility
            // get the setting from the plugin path, if not found, default to the one defined in "akka.persistence"
            var maxPermits = pluginConfig.HasPath("max-concurrent-recoveries") 
                ? pluginConfig.GetInt("max-concurrent-recoveries")
                : system.Settings.Config.GetInt("akka.persistence.max-concurrent-recoveries");

            return system.SystemActorOf(RecoveryPermitter.Props(maxPermits), $"recoveryPermitter-{configPath}");
        }

        private IActorRef CreatePlugin(ExtendedActorSystem system, string configPath, Config pluginConfig, Props? registeredProps)
        {
            var pluginActorName = configPath;
            var pluginTypeName = pluginConfig.GetString("class", null);
            var pluginDispatcherId = pluginConfig.GetString("plugin-dispatcher", null);

            // Switch on: HOCON `class` decides as always, and a registration fills in when there is none.
            // Switch off: registration (by plugin id), built-in, guard (see AkkaFeatures).
            Props pluginProps;
            if (registeredProps is not null && (!AkkaFeatures.IsDynamicTypeLoadingSupported || string.IsNullOrEmpty(pluginTypeName)))
            {
                pluginProps = registeredProps;
            }
            else
            {
                if (string.IsNullOrEmpty(pluginTypeName))
                    throw new ArgumentException($"Plugin class name must be defined in config property [{configPath}.class]");

                if (AkkaFeatures.IsDynamicTypeLoadingSupported)
                    pluginProps = CreatePluginPropsByReflection(pluginTypeName, pluginConfig);
                else if (BuiltInPersistencePlugins.TryCreatePluginProps(pluginTypeName, pluginConfig, out var builtInProps))
                    pluginProps = builtInProps;
                else
                    throw new ConfigurationException(AkkaFeatures.NotBuiltIn(
                        $"{configPath}.class",
                        pluginTypeName,
                        "a plugin registered through Akka.Persistence.Hosting (WithJournal or WithSnapshot, with options derived from JournalOptions<TJournal> or SnapshotOptions<TSnapshotStore>)"));
            }

            //todo wrap in backoffsupervisor ?

            //supervisor-strategy is defined by default in the fallback configs. So we always expect to get a value here even if the user has not explicitly defined anything
            var configurator = SupervisorStrategyConfigurator.CreateConfigurator(
                pluginConfig.GetString("supervisor-strategy"), $"{configPath}.supervisor-strategy");

            var pluginActorProps = pluginProps.WithDispatcher(pluginDispatcherId).WithSupervisorStrategy(configurator.Create());

            return system.SystemActorOf(pluginActorProps, pluginActorName);
        }

        [RequiresUnreferencedCode("Loads a persistence plugin type named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static Props CreatePluginPropsByReflection(string pluginTypeName, Config pluginConfig)
        {
            var pluginType = Type.GetType(pluginTypeName, true);
            object[] pluginActorArgs = pluginType.GetConstructor(new[] { typeof(Config) }) != null ? new object[] { pluginConfig } : null;
            return new Props(pluginType, pluginActorArgs);
        }

        [RequiresUnreferencedCode("Loads a stash overflow configurator named in HOCON by name. The trimmer cannot tell which type that is, so it may have been trimmed away.")]
        private static IStashOverflowStrategyConfigurator CreateStashOverflowConfiguratorByReflection(string configuratorTypeName)
        {
            var configuratorType = Type.GetType(configuratorTypeName);
            if (configuratorType is null)
                throw new ConfigurationException(
                    $"Could not resolve internal-stash-overflow-strategy type [{configuratorTypeName}]. Ensure the type name is fully qualified.");
            return (IStashOverflowStrategyConfigurator)Activator.CreateInstance(configuratorType);
        }

        private EventAdapters CreateAdapters(ExtendedActorSystem system, string configPath, Config section, PersistencePluginDetails? registered, bool isJournal)
        {
            var adapters = isJournal ? _registry.EventAdaptersFor(configPath) : null;

            // a registered plugin, or a journal with adapters added by id, may have no HOCON section at all
            if (registered is null && (adapters is null || adapters.Count == 0) && section.IsNullOrEmpty())
                throw ConfigurationException.NullOrEmptyConfig<EventAdapters>(configPath);

            return EventAdapters.Create(system, section, configPath, adapters);
        }

        private PluginHolder NewPluginHolder(ExtendedActorSystem system, string configPath, string fallbackPath)
        {
            // a registration is keyed by plugin id, and a journal is not a snapshot store
            PersistencePluginDetails? registered = null;
            if (!string.IsNullOrEmpty(configPath))
            {
                if (fallbackPath == JournalFallbackConfigPath && _registry.TryGet<JournalDetails>(configPath, out var journal))
                    registered = journal;
                else if (fallbackPath == SnapshotStoreFallbackConfigPath && _registry.TryGet<SnapshotStoreDetails>(configPath, out var snapshotStore))
                    registered = snapshotStore;
            }

            var hasSection = !string.IsNullOrEmpty(configPath) && system.Settings.Config.HasPath(configPath);
            if (!hasSection && registered is null)
            {
                throw new ArgumentException($"Persistence config is missing plugin config path for: {configPath}");
            }

            // HOCON first, then the registration's default config, then the shared plugin fallback
            var section = hasSection ? system.Settings.Config.GetConfig(configPath) : Config.Empty;
            if (registered?.DefaultConfig is { } defaultConfig)
                section = section.WithFallback(defaultConfig);

            var config = section.WithFallback(system.Settings.Config.GetConfig(fallbackPath));

            // With reflection off the registration decides, but a HOCON `class` that names another type is a conflict, and
            // starting the registered type quietly would not be what the HOCON says. With reflection on, HOCON decides.
            if (!AkkaFeatures.IsDynamicTypeLoadingSupported && registered is not null && registered.NamesAnotherType(config.GetString("class", null)))
                throw new ConfigurationException(
                    $"[{configPath}.class] names [{config.GetString("class", null)}], but Akka.Persistence.Hosting registered [{registered.TypeName}] for this plugin and " +
                    "dynamic type loading is disabled. Remove the `class` override, or register options whose generic argument is the type it names.");

            var registeredProps = registered switch
            {
                JournalDetails j => j.CreateProps(config),
                SnapshotStoreDetails s => s.CreateProps(config),
                _ => null
            };
            var plugin = CreatePlugin(system, configPath, config, registeredProps);
            var adapters = CreateAdapters(system, configPath, section, registered, fallbackPath == JournalFallbackConfigPath);
            var recoveryPermitter = CreateRecoveryPermitter(system, configPath, config);

            return new PluginHolder(plugin, adapters, config, recoveryPermitter);
        }
    }

    /// <summary>
    /// Persistence extension.
    /// </summary>
    public class Persistence : ExtensionIdProvider<PersistenceExtension>
    {
        /// <summary>
        /// Persistence extension identifier used to access the persistence extension for an actor system.
        /// </summary>
        public static Persistence Instance { get; } = new();

        /// <summary>
        /// Creates the persistence extension for an actor system.
        /// </summary>
        /// <param name="system">Actor system that owns the extension.</param>
        /// <returns>The persistence extension initialized for <paramref name="system"/>.</returns>
        public override PersistenceExtension CreateExtension(ExtendedActorSystem system)
        {
            return new PersistenceExtension(system);
        }

        /// <summary>
        /// Returns the default Akka.Persistence configuration.
        /// </summary>
        /// <returns>The default persistence configuration embedded in the assembly.</returns>
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
        /// Settings that control persistent view updates.
        /// </summary>
        public ViewSettings View { get; }

        /// <summary>
        /// Configuration settings for persistent views.
        /// </summary>
        public sealed class ViewSettings
        {
            /// <summary>
            /// Initializes view settings from configuration.
            /// </summary>
            /// <param name="config">Configuration containing the view settings.</param>
            public ViewSettings(Config config)
            {
                AutoUpdate = config.GetBoolean("view.auto-update", false);
                AutoUpdateInterval = config.GetTimeSpan("view.auto-update-interval", null);
                var repMax = config.GetLong("view.auto-update-replay-max", 0);
                AutoUpdateReplayMax = repMax < 0 ? long.MaxValue : repMax;
            }

            /// <summary>
            /// Indicates whether persistent views update automatically.
            /// </summary>
            public bool AutoUpdate { get; }

            /// <summary>
            /// Interval between automatic persistent view updates.
            /// </summary>
            public TimeSpan AutoUpdateInterval { get; }

            /// <summary>
            /// Maximum number of events replayed during an automatic view update; a negative configured value is treated as unlimited.
            /// </summary>
            public long AutoUpdateReplayMax { get; }
        }

        /// <summary>
        /// Settings for at-least-once message delivery.
        /// </summary>
        public AtLeastOnceDeliverySettings AtLeastOnceDelivery { get; set; }

        /// <summary>
        /// Configuration settings for at-least-once message delivery.
        /// </summary>
        public sealed class AtLeastOnceDeliverySettings
        {
            /// <summary>
            /// Initializes at-least-once delivery settings.
            /// </summary>
            /// <param name="redeliverInterval">Interval between redelivery attempts.</param>
            /// <param name="redeliveryBurstLimit">Maximum number of unconfirmed messages sent in one redelivery burst.</param>
            /// <param name="warnAfterNumberOfUnconfirmedAttempts">Number of attempts after which an unconfirmed delivery warning is sent.</param>
            /// <param name="maxUnconfirmedMessages">Maximum number of unconfirmed messages retained by the actor.</param>
            public AtLeastOnceDeliverySettings(TimeSpan redeliverInterval, int redeliveryBurstLimit,
                int warnAfterNumberOfUnconfirmedAttempts, int maxUnconfirmedMessages)
            {
                RedeliverInterval = redeliverInterval;
                RedeliveryBurstLimit = redeliveryBurstLimit;
                WarnAfterNumberOfUnconfirmedAttempts = warnAfterNumberOfUnconfirmedAttempts;
                MaxUnconfirmedMessages = maxUnconfirmedMessages;
            }

            /// <summary>
            /// Initializes at-least-once delivery settings from configuration.
            /// </summary>
            /// <param name="config">Configuration containing the at-least-once delivery settings.</param>
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
            /// Returns a copy with a different redelivery interval.
            /// </summary>
            /// <param name="redeliverInterval">Interval between redelivery attempts.</param>
            /// <returns>A copy of these settings with the specified redelivery interval.</returns>
            public AtLeastOnceDeliverySettings WithRedeliverInterval(TimeSpan redeliverInterval)
            {
                return Copy(redeliverInterval);
            }

            /// <summary>
            /// Returns a copy with a different maximum number of unconfirmed messages.
            /// </summary>
            /// <param name="maxUnconfirmedMessages">Maximum number of unconfirmed messages retained by the actor.</param>
            /// <returns>A copy of these settings with the specified maximum.</returns>
            public AtLeastOnceDeliverySettings WithMaxUnconfirmedMessages(int maxUnconfirmedMessages)
            {
                return Copy(null, null, null, maxUnconfirmedMessages);
            }

            /// <summary>
            /// Returns a copy with a different redelivery burst limit.
            /// </summary>
            /// <param name="redeliveryBurstLimit">Maximum number of unconfirmed messages sent in one redelivery burst.</param>
            /// <returns>A copy of these settings with the specified burst limit.</returns>
            public AtLeastOnceDeliverySettings WithRedeliveryBurstLimit(int redeliveryBurstLimit)
            {
                return Copy(null, redeliveryBurstLimit);
            }

            /// <summary>
            /// Returns a copy with a different warning threshold for unconfirmed deliveries.
            /// </summary>
            /// <param name="unconfirmedAttemptsToWarn">Number of attempts after which an unconfirmed delivery warning is sent.</param>
            /// <returns>A copy of these settings with the specified warning threshold.</returns>
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
        /// Initializes persistence settings from the actor system and configuration.
        /// </summary>
        /// <param name="system">Actor system whose settings are being created.</param>
        /// <param name="config">Persistence configuration.</param>
        public PersistenceSettings(ActorSystem system, Config config)
            : base(system, config)
        {
            View = new ViewSettings(config);
            AtLeastOnceDelivery = new AtLeastOnceDeliverySettings(config);
            Internal = new InternalSettings(config);
        }
    }

    /// <summary>
    /// Provides a recovery strategy for a persistent actor.
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
    /// Provides an overflow strategy for a persistent actor internal stash.
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
    /// Provides the configuration path and default configuration for a journal plugin.
    /// </summary>
    public interface IJournalPlugin
    {
        /// <summary>
        /// Configuration path of the journal plugin.
        /// </summary>
        string JournalPath { get; }

        /// <summary>
        /// Default configuration for the journal plugin.
        /// </summary>
        Config DefaultConfig { get; }
    }
}
