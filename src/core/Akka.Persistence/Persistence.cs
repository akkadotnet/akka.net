using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Persistence.Journal;

namespace Akka.Persistence
{
    public class PersistenceExtension : IExtension
    {
        private const string DefaultPluginDispatcherId = "akka.persistence.dispatchers.default-plugin-dispatcher";

        private readonly Config _config;
        private readonly ExtendedActorSystem _system;
        private readonly ActorRef _journal;
        private readonly ActorRef _snapshotStore;

        public PersistenceSettings Settings { get; private set; }

        public PersistenceExtension(ExtendedActorSystem system)
        {
            _system = system;
            _config = system.Settings.Config.WithFallback(Persistence.DefaultConfig()).GetConfig("akka.persistence");
            _journal = CreatePlugin("journal", type => typeof (AsyncWriteJournal).IsAssignableFrom(type)
                ? Dispatchers.DefaultDispatcherId
                : DefaultPluginDispatcherId);
            _snapshotStore = CreatePlugin("snapshot-store", _ => DefaultPluginDispatcherId);

            Settings = new PersistenceSettings(_system, _config);
        }

        public string PersistenceId(ActorRef actor)
        {
            return actor.Path.ToStringWithoutAddress();
        }

        public ActorRef SnapshotStoreFor(string persistenceId)
        {
            // currently always returns _snapshotStore, but in future it may return dedicated actor for each persistence id
            return _snapshotStore;
        }

        public ActorRef JournalFor(string persistenceId)
        {
            // currently always returns _journal, but in future it may return dedicated actor for each persistence id
            return _journal;
        }

        private ActorRef CreatePlugin(string type, Func<Type, string> dispatcherSelector)
        {
            var pluginConfigPath = _config.GetString(type + ".plugin");
            var pluginConfig = _system.Settings.Config.GetConfig(pluginConfigPath);
            var pluginTypeName = pluginConfig.GetString("class");
            var pluginType = Type.GetType(pluginTypeName);
            var pluginDispatcherId = pluginConfig.HasPath("plugin-dispatcher")
                ? pluginConfig.GetString("plugin-dispatcher")
                : dispatcherSelector(pluginType);
            return _system.SystemActorOf(Props.Create(pluginType).WithDispatcher(pluginDispatcherId), type);
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
                AutoUpdateInterval = config.GetMillisDuration("view.auto-update-interval");
                var repMax = config.GetLong("view.auto-update-replay-max");
                AutoUpdateReplayMax = repMax < 0 ? long.MaxValue : repMax;
            }

            public bool AutoUpdate { get; private set; }
            public TimeSpan AutoUpdateInterval { get; private set; }
            public long AutoUpdateReplayMax { get; private set; }
        }

        public GuaranteedDeliverySettings GuaranteedDelivery { get; set; }
        public class GuaranteedDeliverySettings
        {
            public GuaranteedDeliverySettings(Config config)
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
            GuaranteedDelivery = new GuaranteedDeliverySettings(config);
            Internal = new InternalSettings(config);
        }
    }
}