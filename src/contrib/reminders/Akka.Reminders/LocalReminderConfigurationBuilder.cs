using Akka.Actor;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;

namespace Akka.Reminders;

/// <summary>
/// Configuration builder for local (non-clustered) reminder functionality, designed for testing scenarios.
/// </summary>
/// <remarks>
/// This builder configures reminders without requiring a cluster or ClusterSingleton, making it ideal
/// for unit and integration tests. The reminder scheduler runs as a regular actor instead of a singleton,
/// providing instant startup with no bootstrap delays.
/// </remarks>
public sealed class LocalReminderConfigurationBuilder
{
    private readonly Dictionary<string, IActorRef> _shardRegions = new();
    private Func<ActorSystem, IReminderStorage>? _storageFactory;
    private Func<ActorSystem, IShardRegionResolver>? _resolver;
    private ReminderSettings _settings = new();

    /// <summary>
    /// Registers a shard region that reminders can be delivered to during testing.
    /// </summary>
    /// <param name="regionName">The name of the shard region (must match the ShardRegionName in reminder entities).</param>
    /// <param name="region">The actor reference that will receive reminder messages (typically a TestProbe or test actor).</param>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithShardRegion(string regionName, IActorRef region)
    {
        _shardRegions[regionName] = region;
        return this;
    }

    /// <summary>
    /// Registers multiple shard regions at once.
    /// </summary>
    /// <param name="regions">A dictionary mapping shard region names to actor references.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithShardRegions(IReadOnlyDictionary<string, IActorRef> regions)
    {
        foreach (var (name, region) in regions)
        {
            _shardRegions[name] = region;
        }
        return this;
    }

    /// <summary>
    /// Configures a custom shard region resolver for advanced testing scenarios.
    /// </summary>
    /// <param name="resolver">The custom resolver implementation.</param>
    /// <returns>This builder instance for method chaining.</returns>
    /// <remarks>
    /// If you use this method, any shard regions registered via <see cref="WithShardRegion"/> or
    /// <see cref="WithShardRegions"/> will be ignored.
    /// </remarks>
    public LocalReminderConfigurationBuilder WithResolver(Func<ActorSystem, IShardRegionResolver> resolver)
    {
        _resolver = resolver;
        return this;
    }

    /// <summary>
    /// Configures the storage backend using a factory function.
    /// </summary>
    /// <param name="factory">A function that creates the storage implementation given an ActorSystem.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithStorage(Func<ActorSystem, IReminderStorage> factory)
    {
        _storageFactory = factory;
        return this;
    }

    /// <summary>
    /// Configures the storage backend to use a specific instance.
    /// </summary>
    /// <param name="storage">The storage implementation to use.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithStorage(IReminderStorage storage)
    {
        _storageFactory = _ => storage;
        return this;
    }

    /// <summary>
    /// Configures the storage backend to use a specific type.
    /// </summary>
    /// <typeparam name="TStorage">The type of storage implementation (must have a parameterless constructor).</typeparam>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithStorage<TStorage>() where TStorage : IReminderStorage, new()
    {
        _storageFactory = _ => new TStorage();
        return this;
    }

    /// <summary>
    /// Explicitly configures in-memory storage (this is the default if no storage is specified).
    /// </summary>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithInMemoryStorage()
    {
        return WithStorage<InMemoryReminderStorage>();
    }

    /// <summary>
    /// Registers <typeparamref name="TMessage"/> as a reminder payload type, so the scheduler can build
    /// <see cref="ReminderEnvelope{T}"/> for it without reflection. Required under Native AOT for every
    /// message type you schedule, including reminders restored from storage after a restart.
    /// Optional, and harmless, under the JIT.
    /// </summary>
    /// <typeparam name="TMessage">The exact runtime type of the message you schedule.</typeparam>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// Native AOT also needs an Akka.NET serializer for <typeparamref name="TMessage"/> when you use
    /// durable storage; register one with <c>WithCustomSerializer</c>. See <see cref="ReminderMessageTypes"/>.
    /// </remarks>
    public LocalReminderConfigurationBuilder WithReminderMessage<TMessage>() where TMessage : notnull
    {
        ReminderMessageTypes.Register<TMessage>();
        return this;
    }

    /// <summary>
    /// Configures the reminder settings.
    /// </summary>
    /// <param name="settings">The reminder settings to use.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithSettings(ReminderSettings settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>
    /// Configures the reminder settings using a configuration action.
    /// </summary>
    /// <param name="configure">An action that modifies the reminder settings.</param>
    /// <returns>This builder instance for method chaining.</returns>
    public LocalReminderConfigurationBuilder WithSettings(Action<ReminderSettings> configure)
    {
        configure(_settings);
        return this;
    }

    internal Func<ActorSystem, IReminderStorage> GetStorageFactory()
    {
        return _storageFactory ?? (_ => new InMemoryReminderStorage());
    }

    internal Func<ActorSystem,IShardRegionResolver> GetResolver()
    {
        return _resolver ?? (_ => new TestShardRegionResolver(_shardRegions));
    }

    internal ReminderSettings GetSettings()
    {
        return _settings;
    }
}
