using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Hosting.HealthChecks;

/// <summary>
/// Checks to see if the <see cref="ActorSystem"/> is alive or not.
/// </summary>
public sealed class ActorSystemLivenessCheck : IAkkaHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (context.ActorSystem.WhenTerminated.IsCompleted)
        {
            return Task.FromResult(new HealthCheckResult(status: context.Registration.FailureStatus, description: "ActorSystem has terminated."));
        }

        return Task.FromResult(new HealthCheckResult(HealthStatus.Healthy, description: "ActorSystem is running."));
    }
}