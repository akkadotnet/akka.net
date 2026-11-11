using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Cluster.Sharding;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;

namespace Akka.Reminders;

/// <summary>
/// Configuration setup for the Akka.Reminders system.
/// </summary>
public sealed class ReminderSetup : Setup
{
    /// <summary>
    /// Creates a new <see cref="ReminderSetup"/> with default settings.
    /// </summary>
    public ReminderSetup()
    {
        // Default to in-memory implementations
        StorageFactory = system => new InMemoryReminderStorage();
        ShardRegionResolverFactory = system => new DefaultShardRegionResolver(system);
        Settings = new ReminderSettings();
    }

    /// <summary>
    /// Factory function to create the <see cref="IReminderStorage"/> implementation.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="InMemoryReminderStorage"/>.
    /// </remarks>
    public Func<ActorSystem, IReminderStorage> StorageFactory { get; init; }

    /// <summary>
    /// Factory function to create the <see cref="IShardRegionResolver"/> implementation.
    /// </summary>
    /// <remarks>
    /// Defaults to <see cref="DefaultShardRegionResolver"/>.
    /// </remarks>
    public Func<ActorSystem, IShardRegionResolver> ShardRegionResolverFactory { get; init; }

    /// <summary>
    /// Settings for the reminder scheduler.
    /// </summary>
    public ReminderSettings Settings { get; init; }

    /// <summary>
    /// The cluster role where the reminder scheduler singleton should run.
    /// If null or empty, the singleton will run on any node.
    /// </summary>
    /// <remarks>
    /// It's recommended to specify a role to control where the singleton runs,
    /// especially in production environments.
    /// </remarks>
    public string? Role { get; init; }

    /// <summary>
    /// Creates a copy of this setup with the specified storage factory.
    /// </summary>
    public ReminderSetup WithStorage(Func<ActorSystem, IReminderStorage> storageFactory)
    {
        return new ReminderSetup
        {
            StorageFactory = storageFactory,
            ShardRegionResolverFactory = ShardRegionResolverFactory,
            Settings = Settings,
            Role = Role
        };
    }

    /// <summary>
    /// Creates a copy of this setup with the specified shard region resolver factory.
    /// </summary>
    public ReminderSetup WithShardRegionResolver(Func<ActorSystem, IShardRegionResolver> resolverFactory)
    {
        return new ReminderSetup
        {
            StorageFactory = StorageFactory,
            ShardRegionResolverFactory = resolverFactory,
            Settings = Settings,
            Role = Role
        };
    }

    /// <summary>
    /// Creates a copy of this setup with the specified settings.
    /// </summary>
    public ReminderSetup WithSettings(ReminderSettings settings)
    {
        settings.Validate();
        return new ReminderSetup
        {
            StorageFactory = StorageFactory,
            ShardRegionResolverFactory = ShardRegionResolverFactory,
            Settings = settings,
            Role = Role
        };
    }

    /// <summary>
    /// Creates a copy of this setup with the specified role.
    /// </summary>
    public ReminderSetup WithRole(string role)
    {
        return new ReminderSetup
        {
            StorageFactory = StorageFactory,
            ShardRegionResolverFactory = ShardRegionResolverFactory,
            Settings = Settings,
            Role = role
        };
    }
}
