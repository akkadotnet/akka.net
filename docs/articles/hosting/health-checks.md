---
uid: hosting-health-checks
title: Microsoft.Extensions.Diagnostics.HealthChecks Integration
---

# Microsoft.Extensions.Diagnostics.HealthChecks Integration

We've recently deprecated [Akka.HealthChecks](https://github.com/petabridge/akkadotnet-healthcheck) in favor of a simpler, more configurable solution that is built directly into Akka.Hosting: `IAkkaHealthCheck` and `WithAkkaHealthCheck`:

```csharp
 builder
    .WithActorSystemLivenessCheck() // have to opt-in to the built-in health check
    .WithHealthCheck("FooActor alive", async (system, registry, cancellationToken) =>
{
    /*
     * N.B. CancellationToken is set by the call to MSFT.EXT.DIAGNOSTICS.HEALTHCHECK,
     * so that value could be "infinite" by default.
     *
     * Therefore, it might be a really, really good idea to guard this with a non-infinite
     * timeout via a LinkedCancellationToken here.
     */
    try
    {
        var fooActor = await registry.GetAsync<FooActor>(cancellationToken);

        try
        {
            var r = await fooActor.Ask<ActorIdentity>(new Identify("foo"), cancellationToken: cancellationToken);
            if (r.Subject.IsNobody())
                return HealthCheckResult.Unhealthy("FooActor was alive but is now dead");
        }
        catch (Exception e)
        {
            return HealthCheckResult.Degraded("FooActor found but non-responsive", e);
        }
    }
    catch (Exception e2)
    {
        return HealthCheckResult.Unhealthy("FooActor not found in registry", e2);
    }

    return HealthCheckResult.Healthy("fooActor found and responsive");
});
```

These health checks and any other you register using one of the `WithHealthCheck` overloads on the `AkkaConfigurationBuilder` will automatically be registered with the [`Microsoft.Extensions.Diagnostics.HealthCheckService`](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) and will be called just like any other ASP.NET Core, Entity Framework, etc health check.

## Dependency Injected Health Checks

As of version 1.5.51, Akka.Hosting supports dependency injection for health checks. You can create custom health check classes that implement `IAkkaHealthCheck` and have dependencies injected from the DI container:

```csharp
// Define a custom health check with DI support
public class MyHealthCheckWithDependencies : IAkkaHealthCheck
{
    private readonly ILogger<MyHealthCheckWithDependencies> _logger;
    private readonly IMyService _myService;

    public MyHealthCheckWithDependencies(
        ILogger<MyHealthCheckWithDependencies> logger,
        IMyService myService)
    {
        _logger = logger;
        _myService = myService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        AkkaHealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Running health check with DI");
            var isHealthy = await _myService.CheckServiceHealthAsync(cancellationToken);

            return isHealthy
                ? HealthCheckResult.Healthy("Service is healthy")
                : HealthCheckResult.Unhealthy("Service is not healthy");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return HealthCheckResult.Unhealthy($"Health check failed: {ex.Message}");
        }
    }
}

// Register the health check using the generic WithHealthCheck<T>() method
builder
    .WithActorSystemLivenessCheck()
    .WithHealthCheck<MyHealthCheckWithDependencies>(
        name: "MyServiceHealth",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "ready", "service" },
        timeout: TimeSpan.FromSeconds(5));
```

The health check type will be resolved from the DI container when the health check is executed, allowing you to leverage constructor injection for any dependencies your health check needs. The health check instance itself doesn't need to be registered in DI - Akka.Hosting will automatically resolve it using `ActivatorUtilities.GetServiceOrCreateInstance<T>()`.

## Built-in Health Checks

> [!NOTE]
> All Akka.NET health checks will be tagged with the `akka` tag, so they [can easily be filtered via the health check endpoints](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-9.0#filter-health-checks).

Akka.Hosting and its other packages ship with some built-in health checks:

* `WithActorSystemLivenessCheck()` - a liveness probe that will fail if the `ActorSystem` is terminated. Generally, Akka.Hosting will try to shut down your process anyway if the `ActorSystem` dies.
* `WithAkkaClusterReadinessCheck` - if you are an Akka.Cluster user, this health check will return `HealthStatus.Unhealthy` until you successfully join a cluster - that way you can stop load-balancers and other devices from routing traffic to this node until it has access to the cluster. This readiness check is also tagged with the `ready` tag for filtering purposes.
* **Akka.Persistence Health Checks** - verify that persistence plugins (journals and snapshot stores) are properly initialized and accessible. These health checks use the built-in Akka.Persistence health check APIs to validate plugin connectivity and functionality. Health checks are tagged with `akka`, `persistence`, and either `journal` or `snapshot-store` for filtering purposes.

### Configuring Persistence Health Checks

You can add health checks for your persistence plugins using the `.WithHealthCheck()` method when configuring journals and snapshot stores:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        // Journal with health check
        .WithJournal(
            new SqlServerJournalOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            journal => journal
                .AddWriteEventAdapter<MyAdapter>("adapter", new[] { typeof(MyEvent) })
                .WithHealthCheck(
                    unHealthyStatus: HealthStatus.Degraded,
                    name: "sql-journal"))

        // Snapshot store with health check
        .WithSnapshot(
            new SqlServerSnapshotOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            snapshot => snapshot
                .WithHealthCheck(
                    unHealthyStatus: HealthStatus.Degraded,
                    name: "sql-snapshot"));
});
```

You can also configure both journal and snapshot health checks together:

```csharp
builder.Services.AddAkka("MyActorSystem", configurationBuilder =>
{
    configurationBuilder
        .WithJournalAndSnapshot(
            new SqlServerJournalOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            new SqlServerSnapshotOptions
            {
                ConnectionString = "...",
                IsDefaultPlugin = true
            },
            journal => journal.WithHealthCheck(),
            snapshot => snapshot.WithHealthCheck());
});
```

The health checks will automatically:

* Verify the persistence plugin is configured correctly
* Test connectivity to the underlying storage (database, cloud storage, etc.)
* Report `Healthy` when the plugin is operational
* Report `Degraded` or `Unhealthy` (configurable) when issues are detected
