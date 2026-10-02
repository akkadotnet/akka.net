using System.Collections.Generic;
using System.Linq;
using Akka.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Persistence.Hosting;

/// <summary>
/// Used to help build snapshot store configurations
/// </summary>
public sealed class AkkaPersistenceSnapshotBuilder
{
    internal readonly string SnapshotStoreId;
    internal readonly AkkaConfigurationBuilder Builder;
    internal readonly HashSet<AkkaHealthCheckRegistration> HealthCheckRegistrations = [];

    /// <summary>
    /// The <see cref="SnapshotOptions"/> instance used to configure this snapshot store.
    /// This property allows extension methods to access snapshot store configuration details
    /// (such as connection strings) without requiring them as explicit parameters.
    /// </summary>
    public SnapshotOptions? Options { get; }

    public AkkaPersistenceSnapshotBuilder(string snapshotStoreId, AkkaConfigurationBuilder builder)
    {
        SnapshotStoreId = snapshotStoreId;
        Builder = builder;
        Options = null;
    }

    /// <summary>
    /// Constructor that accepts snapshot options for improved extension method ergonomics.
    /// </summary>
    /// <param name="snapshotStoreId">The snapshot store identifier</param>
    /// <param name="builder">The Akka configuration builder</param>
    /// <param name="options">The snapshot options instance</param>
    public AkkaPersistenceSnapshotBuilder(string snapshotStoreId, AkkaConfigurationBuilder builder, SnapshotOptions options)
    {
        SnapshotStoreId = snapshotStoreId;
        Builder = builder;
        Options = options;
    }

    /// <summary>
    /// Uses the built-in snapshot store health check on the Akka.Persistence.SnapshotStore.
    /// </summary>
    /// <param name="unHealthyStatus">Default status to return when the plugin reports <see cref="PersistenceHealthStatus.Unhealthy"/>
    /// or <see cref="PersistenceHealthStatus.Degraded"/>. Defaults to degraded.</param>
    /// <param name="name">Optional name to add to the health check.</param>
    /// <param name="tags">Custom tags for the health check. If null, defaults to ["akka", "persistence", "snapshot-store"].</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public AkkaPersistenceSnapshotBuilder WithHealthCheck(HealthStatus unHealthyStatus = HealthStatus.Degraded,
        string? name = null,
        IEnumerable<string>? tags = null)
    {
        var registration = AddDefaultHealthCheck(name, unHealthyStatus, tags);
        HealthCheckRegistrations.Add(registration);
        return this;
    }

    /// <summary>
    /// For Akka.Persistence plugins that have custom health checks (see https://github.com/akkadotnet/Akka.Hosting/issues/678)
    /// </summary>
    /// <param name="registration">The custom health check registration.</param>
    /// <returns>The current builder instance for method chaining.</returns>
    public AkkaPersistenceSnapshotBuilder WithCustomHealthCheck(AkkaHealthCheckRegistration registration)
    {
        HealthCheckRegistrations.Add(registration);
        return this;
    }

    /// <summary>
    /// Backward-compatible overload for external plugins that use the 2-parameter version.
    /// </summary>
    internal AkkaHealthCheckRegistration AddHealthCheck(string? name, HealthStatus unHealthyStatus)
    {
        return AddDefaultHealthCheck(name, unHealthyStatus, tags: null);
    }

    internal AkkaHealthCheckRegistration AddDefaultHealthCheck(string? name, HealthStatus unHealthyStatus, IEnumerable<string>? tags)
    {
        var pluginId = $"akka.persistence.snapshot-store.{SnapshotStoreId}";
        var healthCheckTags = tags?.ToList() ?? ["akka", "persistence", "snapshot-store"];
        var registration = new AkkaHealthCheckRegistration(
            name ?? pluginId,
            new SnapshotStoreHealthCheck(pluginId),
            unHealthyStatus,
            healthCheckTags);
        return registration;
    }

    /// <summary>
    /// INTERNAL API - Registers health checks if configured.
    /// </summary>
    internal void Build()
    {
        // add the health checks if specified
        foreach(var hc in HealthCheckRegistrations)
            Builder.WithHealthCheck(hc);
    }
}