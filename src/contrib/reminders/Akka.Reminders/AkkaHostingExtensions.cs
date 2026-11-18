using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Event;
using Akka.Hosting;

namespace Akka.Reminders;

/// <summary>
/// Extension methods for configuring Akka.Reminders with Akka.Hosting.
/// </summary>
public static class AkkaHostingExtensions
{
    /// <summary>
    /// Registers the <see cref="Serialization.ReminderSerializer"/> for all
    /// <see cref="IReminderWireMessage"/> types using the Akka.Hosting serializer API.
    /// </summary>
    private static void RegisterReminderSerializer(AkkaConfigurationBuilder builder)
    {
        builder.WithCustomSerializer(
            "reminder-serializer",
            [typeof(IReminderWireMessage)],
            system => new Serialization.ReminderSerializer(system));
    }

    /// <summary>
    /// Adds the Akka.Reminders system to the actor system.
    /// </summary>
    /// <param name="builder">The Akka configuration builder.</param>
    /// <param name="role">The cluster role where the reminder scheduler singleton should run.</param>
    /// <param name="configure">Optional action to configure the reminders system using a fluent builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <example>
    /// <code>
    /// builder.WithReminders("reminder-host", reminders => reminders
    ///     .WithStorage(sys => new SqlReminderStorage(connectionString))
    ///     .WithSettings(new ReminderSettings
    ///     {
    ///         MaxSlippage = TimeSpan.FromSeconds(10)
    ///     }));
    /// </code>
    /// </example>
    public static AkkaConfigurationBuilder WithReminders(
        this AkkaConfigurationBuilder builder,
        string role,
        Action<ReminderConfigurationBuilder>? configure = null)
    {
        var reminderBuilder = new ReminderConfigurationBuilder();
        reminderBuilder.WithRole(role);
        configure?.Invoke(reminderBuilder);
        var setup = reminderBuilder.Build();

        // Register the reminder serializer for all IReminderWireMessage types.
        RegisterReminderSerializer(builder);

        // Add the setup to the actor system
        builder.AddSetup(setup);

        // Start the singleton manager (if this node has the role) and proxy as /system actors
        builder.WithActors((system, registry) =>
        {
            var extendedSystem = (ExtendedActorSystem)system;
            var log = Logging.GetLogger(system, typeof(AkkaHostingExtensions));

            // Check if this node has the required role to host the singleton
            var nodeRoles = system.Settings.Config.GetStringList("akka.cluster.roles");
            var canHostSingleton = string.IsNullOrEmpty(setup.Role) || nodeRoles.Contains(setup.Role);

            if (canHostSingleton)
            {
                // Create the storage and resolver instances
                var storage = setup.StorageFactory(system);
                var resolver = setup.ShardRegionResolverFactory(system);

                // Create singleton settings
                var singletonSettings = ClusterSingletonManagerSettings.Create(system);
                if (!string.IsNullOrEmpty(setup.Role))
                {
                    singletonSettings = singletonSettings.WithRole(setup.Role);
                }

                // Create and start the singleton manager as a /system actor
                var singletonProps = ClusterSingletonManager.Props(
                    singletonProps: Props.Create(() => new ReminderScheduler(setup.Settings, resolver, storage, system.Scheduler)),
                    terminationMessage: PoisonPill.Instance,
                    settings: singletonSettings);

                extendedSystem.SystemActorOf(singletonProps, "reminder-scheduler");
                log.Info("Reminder scheduler singleton manager started - this node can host the reminder scheduler.");
            }
            else
            {
                log.Info(
                    "Node does not have role '{0}' - reminder scheduler proxy will forward messages to singleton host. " +
                    "This node cannot host the reminder scheduler but can still schedule and cancel reminders.",
                    setup.Role);
            }

            // Create proxy settings
            // NOTE: The proxy is created on ALL nodes, but needs to know which role hosts the singleton
            // WithRole() here specifies where to FIND the singleton manager, not a restriction on the proxy itself
            var proxySettings = ClusterSingletonProxySettings.Create(system);
            if (!string.IsNullOrEmpty(setup.Role))
            {
                proxySettings = proxySettings.WithRole(setup.Role);
            }

            // Create and start the singleton proxy as a /system actor
            var proxyProps = ClusterSingletonProxy.Props(
                singletonManagerPath: "/system/reminder-scheduler",
                settings: proxySettings);

            var proxy = extendedSystem.SystemActorOf(proxyProps, "reminder-scheduler-proxy");

            // Register the proxy in the actor registry for easy access
            registry.Register<ReminderSchedulerProxy>(proxy);
        });

        return builder;
    }

    /// <summary>
    /// Adds a local (non-clustered) reminder system to the actor system, designed for testing scenarios.
    /// </summary>
    /// <param name="builder">The Akka configuration builder.</param>
    /// <param name="configure">Optional action to configure the local reminders system using a fluent builder.</param>
    /// <returns>The builder for method chaining.</returns>
    /// <remarks>
    /// This method creates a reminder scheduler as a regular actor instead of a ClusterSingleton,
    /// providing instant startup with no bootstrap delays. This is ideal for unit and integration tests.
    /// You must register shard regions using the configuration builder for reminders to be delivered.
    /// </remarks>
    /// <example>
    /// <code>
    /// // In a test
    /// var targetActor = testKit.CreateTestProbe("billing-actor");
    ///
    /// builder.WithLocalReminders(reminders => reminders
    ///     .WithInMemoryStorage()
    ///     .WithShardRegion("billing-shard", targetActor)
    ///     .WithSettings(new ReminderSettings
    ///     {
    ///         MaxSlippage = TimeSpan.FromMilliseconds(100),
    ///         MaxDeliveryAttempts = 3
    ///     }));
    /// </code>
    /// </example>
    public static AkkaConfigurationBuilder WithLocalReminders(
        this AkkaConfigurationBuilder builder,
        Action<LocalReminderConfigurationBuilder>? configure = null)
    {
        var localBuilder = new LocalReminderConfigurationBuilder();
        configure?.Invoke(localBuilder);

        // Register the reminder serializer for all IReminderWireMessage types.
        RegisterReminderSerializer(builder);

        builder.WithActors((system, registry) =>
        {
            var extendedSystem = (ExtendedActorSystem)system;

            // Create the storage and resolver instances from the local builder
            var storage = localBuilder.GetStorageFactory()(system);
            var resolver = localBuilder.GetResolver()(system);
            var settings = localBuilder.GetSettings();

            // Create the reminder scheduler as a regular /system actor (NOT a singleton)
            var schedulerProps = Props.Create(() => new ReminderScheduler(settings, resolver, storage, system.Scheduler));
            var scheduler = extendedSystem.SystemActorOf(schedulerProps, "reminder-scheduler");

            // Register the scheduler directly as the proxy (since there's no singleton indirection)
            registry.Register<ReminderSchedulerProxy>(scheduler);

            // Register the ReminderClient extension
            system.WithExtension<ReminderClientExtension, ReminderClientProvider>();
        });

        return builder;
    }
}

/// <summary>
/// Marker type for registering the reminder scheduler proxy in the actor registry.
/// </summary>
public sealed class ReminderSchedulerProxy { }
