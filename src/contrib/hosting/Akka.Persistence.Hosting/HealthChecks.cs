using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Akka.Persistence.Hosting;

internal static class HealthCheckExt
{
    public static HealthCheckResult ToHealthCheckResult(this PersistenceHealthCheckResult persistenceHealthStatus)
        => new(persistenceHealthStatus.Status.ToHealthStatus(), persistenceHealthStatus.Description,
            persistenceHealthStatus.Exception, persistenceHealthStatus.Data);
        
    public static HealthStatus ToHealthStatus(this PersistenceHealthStatus persistenceHealthStatus) => persistenceHealthStatus switch
    {
        PersistenceHealthStatus.Healthy => HealthStatus.Healthy,
        PersistenceHealthStatus.Degraded => HealthStatus.Degraded,
        _ => HealthStatus.Unhealthy
    };
}

/// <summary>
/// INTERNAL API
///
/// Leverages internal Akka.Persistence APIs to perform a health check on a journal.
/// </summary>
internal sealed class JournalHealthCheck : IAkkaHealthCheck
{
    private readonly string _journalPluginId;

    public JournalHealthCheck(string journalPluginId)
    {
        _journalPluginId = journalPluginId;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var persistence = Persistence.Instance.Apply(context.ActorSystem);
        var journalResult = await persistence.CheckJournalHealthAsync(_journalPluginId, cancellationToken);
        return journalResult.ToHealthCheckResult();
    }
}

/// <summary>
/// INTERNAL API
///
/// Leverages internal Akka.Persistence APIs to perform a health check on the snapshot store.
/// </summary>
internal sealed class SnapshotStoreHealthCheck : IAkkaHealthCheck
{
    private readonly string _snapshotStorePluginId;

    public SnapshotStoreHealthCheck(string snapshotStorePluginId)
    {
        _snapshotStorePluginId = snapshotStorePluginId;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var persistence = Persistence.Instance.Apply(context.ActorSystem);
        var ssResult = await persistence.CheckSnapshotStoreHealthAsync(_snapshotStorePluginId, cancellationToken);
        return ssResult.ToHealthCheckResult();
    }
}