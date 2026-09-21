using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Hosting.HealthChecks;

/// <summary>
/// INTERNAL API
///
/// Delegate-based health check implementation. Lowest ceremony API for defining health checks on the
/// <see cref="AkkaConfigurationBuilder"/>.
/// </summary>
internal sealed class DelegateHealthCheck : IAkkaHealthCheck
{
    private readonly Func<ActorSystem, ActorRegistry, CancellationToken, Task<HealthCheckResult>> _healthCheckFunc;

    public DelegateHealthCheck(Func<ActorSystem, ActorRegistry, CancellationToken, Task<HealthCheckResult>> healthCheckFunc)
    {
        _healthCheckFunc = healthCheckFunc ?? throw new ArgumentNullException(nameof(healthCheckFunc));
    }

    public Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return _healthCheckFunc(context.ActorSystem, context.ActorRegistry, cancellationToken);
    }
}