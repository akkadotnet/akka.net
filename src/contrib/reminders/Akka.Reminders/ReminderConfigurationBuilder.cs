using Akka.Actor;
using Akka.Reminders.Sharding;
using Akka.Reminders.Storage;

namespace Akka.Reminders;

/// <summary>
/// Fluent builder for configuring the Akka.Reminders system.
/// </summary>
public sealed class ReminderConfigurationBuilder
{
    private Func<ActorSystem, IReminderStorage>? _storageFactory;
    private Func<ActorSystem, IShardRegionResolver>? _resolverFactory;
    private ReminderSettings _settings = new();
    private string? _role;

    /// <summary>
    /// Configures the storage backend using a factory function.
    /// </summary>
    /// <param name="factory">Factory function to create the storage instance.</param>
    /// <returns>This builder for method chaining.</returns>
    public ReminderConfigurationBuilder WithStorage(Func<ActorSystem, IReminderStorage> factory)
    {
        _storageFactory = factory;
        return this;
    }

    /// <summary>
    /// Configures the storage backend to use in-memory storage (this is the default if no storage is specified).
    /// </summary>
    /// <returns>This builder for method chaining.</returns>
    /// <remarks>
    /// In-memory storage is ideal for testing and development scenarios where persistence is not required.
    /// Note that in-memory storage is not distributed and state will be lost on restart.
    /// </remarks>
    public ReminderConfigurationBuilder WithInMemoryStorage()
    {
        return WithStorage(sys => new InMemoryReminderStorage());
    }

    /// <summary>
    /// Configures the shard region resolver using a factory function.
    /// </summary>
    /// <param name="factory">Factory function to create the resolver instance.</param>
    /// <returns>This builder for method chaining.</returns>
    public ReminderConfigurationBuilder WithResolver(Func<ActorSystem, IShardRegionResolver> factory)
    {
        _resolverFactory = factory;
        return this;
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
    public ReminderConfigurationBuilder WithReminderMessage<TMessage>() where TMessage : notnull
    {
        ReminderMessageTypes.Register<TMessage>();
        return this;
    }

    /// <summary>
    /// Configures the reminder scheduler settings.
    /// </summary>
    /// <param name="settings">The settings to use.</param>
    /// <returns>This builder for method chaining.</returns>
    public ReminderConfigurationBuilder WithSettings(ReminderSettings settings)
    {
        _settings = settings;
        return this;
    }

    /// <summary>
    /// Configures the reminder scheduler settings using an action.
    /// </summary>
    /// <param name="configure">Action to configure the settings.</param>
    /// <returns>This builder for method chaining.</returns>
    public ReminderConfigurationBuilder WithSettings(Action<ReminderSettings> configure)
    {
        var settings = new ReminderSettings();
        configure(settings);
        _settings = settings;
        return this;
    }

    /// <summary>
    /// Configures the cluster role where the reminder scheduler singleton should run.
    /// </summary>
    /// <param name="role">The cluster role name.</param>
    /// <returns>This builder for method chaining.</returns>
    public ReminderConfigurationBuilder WithRole(string role)
    {
        _role = role;
        return this;
    }

    /// <summary>
    /// Builds the internal <see cref="ReminderSetup"/> from the configured options.
    /// </summary>
    internal ReminderSetup Build()
    {
        var setup = new ReminderSetup();

        if (_storageFactory != null)
            setup = setup.WithStorage(_storageFactory);

        if (_resolverFactory != null)
            setup = setup.WithShardRegionResolver(_resolverFactory);

        setup = setup.WithSettings(_settings);

        if (_role != null)
            setup = setup.WithRole(_role);

        return setup;
    }
}
