using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Hosting.HealthChecks;

/// <summary>
/// INTERNAL API
///
/// Adapter wrapper around the <see cref="IAkkaHealthCheck"/> to make it API-compatible with <see cref="IHealthCheck"/>
/// </summary>
internal sealed class HealthCheckAdapter : IHealthCheck
{
    private readonly IAkkaHealthCheck  _healthCheck;
    private readonly ActorSystem  _actorSystem;
    public HealthCheckAdapter(IAkkaHealthCheck healthCheck, ActorSystem actorSystem)
    {
        _healthCheck = healthCheck;
        _actorSystem = actorSystem;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = new CancellationToken())
    {
        var akkaHealthCheckContext = new AkkaHealthCheckContext(_actorSystem)
        {
            Registration = context.Registration
        };
        
        return _healthCheck.CheckHealthAsync(akkaHealthCheckContext, cancellationToken);
    }
}