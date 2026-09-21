using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Cluster.Hosting;

internal static class ClusterHealthCheckHelpers
{
    public static IReadOnlyDictionary<string, object> DumpClusterState(this ClusterEvent.CurrentClusterState state)
    {
        return new Dictionary<string, object>
        {
            {"cluster.members", state.Members.Count},
            {"cluster.unreachable", state.Unreachable.Count},
            {"cluster.leader", state.Leader.ToString()}
        };
    }
}

/// <summary>
/// Checks to see if we've joined a cluster and have been marked as <see cref="MemberStatus.Up"/>
/// or <see cref="MemberStatus.WeaklyUp"/>
/// </summary>
public sealed class AkkaClusterReadinessCheck : IAkkaHealthCheck
{
    /// <summary>
    /// Have we successfully joined the cluster?
    /// </summary>
    public bool WeHaveJoined { get; private set; }

    public DateTime BeganJoining { get; } = DateTime.UtcNow;

    public DateTime? FinishedJoining { get; private set; }

    public HealthCheckResult HealthyResult(DateTime finishedJoining) => HealthCheckResult.Healthy(
        $"Observed successful cluster join after [{finishedJoining - BeganJoining:g}] - actual join duration was probably faster, but this is how quickly the health check observed it.");

    public HealthCheckResult UnhealthyResult(DateTime now, HealthStatus failureStatus) =>
        new HealthCheckResult(failureStatus, $"Have not yet joined Akka.NET cluster [{now - BeganJoining:g}] elapsed");

    public Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (WeHaveJoined && FinishedJoining != null)
            return Task.FromResult(HealthyResult(FinishedJoining.Value));

        var cluster = Cluster.Get(context.ActorSystem);
        WeHaveJoined = cluster.SelfMember.Status is MemberStatus.Up or MemberStatus.WeaklyUp;

        if (WeHaveJoined)
        {
            FinishedJoining = DateTime.UtcNow;
            return Task.FromResult(HealthyResult(FinishedJoining.Value));
        }

        return Task.FromResult(UnhealthyResult(DateTime.UtcNow, context.Registration.FailureStatus));
    }
}