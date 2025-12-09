// -----------------------------------------------------------------------
//  <copyright file="SnapshotStoreHealthCheckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using Akka.TestKit;
using Akka.TestKit.Configs;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests;

public class SnapshotStoreHealthCheckSpec : PersistenceSpec
{
    private static Config HealthCheckConfig()
    {
        const string extraConfig = """

                                               akka.persistence.snapshot-store.failing-open {
                                                   class = "Akka.Persistence.Tests.FailingSnapshotStore, Akka.Persistence.Tests"
                                                   circuit-breaker {
                                                       max-failures = 1
                                                       call-timeout = 1s
                                                       reset-timeout = 10s
                                                   }
                                               }
                                               akka.persistence.snapshot-store.failing-half-open {
                                                   class = "Akka.Persistence.Tests.FailingSnapshotStore, Akka.Persistence.Tests"
                                                   circuit-breaker {
                                                       max-failures = 1
                                                       call-timeout = 1s
                                                       reset-timeout = 1s
                                                   }
                                               }
                                               # Disable message serialization for circuit breaker tests to avoid serialization issues
                                               akka.actor.serialize-messages = off

                                   """;
        return TestConfigs.TestSchedulerConfig
            .WithFallback(Configuration("SnapshotStoreHealthCheckSpec", extraConfig: extraConfig));
    }

    public SnapshotStoreHealthCheckSpec(ITestOutputHelper output) : base(HealthCheckConfig(), output)
    {
    }

    [Theory]
    [InlineData(null)] // default plugin
    [InlineData("akka.persistence.snapshot-store.inmem")]
    public async Task SnapshotStoreHealthCheck_should_default_to_Healthy(string? pluginId)
    {
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var pluginHealth = await Extension.CheckSnapshotStoreHealthAsync(pluginId, cts.Token);

        Assert.Equal(PersistenceHealthStatus.Healthy, pluginHealth.Status);
        Assert.NotNull(pluginHealth.Description);
    }

    [Fact]
    public async Task SnapshotStoreHealthCheck_should_return_Degraded_when_CircuitBreaker_is_Open()
    {
        // Get the snapshot store actor reference
        var snapshotStore = Extension.SnapshotStoreFor("akka.persistence.snapshot-store.failing-open");

        // Trigger a failure to open the circuit breaker
        var saveMsg = new SaveSnapshot(new SnapshotMetadata("test-pid", 1, DateTime.UtcNow), "test-snapshot");
        snapshotStore.Tell(saveMsg, TestActor);

        // Advance time to let the save fail and circuit breaker open
        var testScheduler = (TestScheduler)Sys.Scheduler;
        testScheduler.Advance(TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var pluginHealth = await Extension.CheckSnapshotStoreHealthAsync("akka.persistence.snapshot-store.failing-open", cts.Token);

        Assert.Equal(PersistenceHealthStatus.Degraded, pluginHealth.Status);
        Assert.Contains("Circuit breaker is open", pluginHealth.Description);
    }

    [Fact]
    public async Task SnapshotStoreHealthCheck_should_return_Degraded_when_CircuitBreaker_is_HalfOpen()
    {
        // Get the snapshot store actor reference
        var snapshotStore = Extension.SnapshotStoreFor("akka.persistence.snapshot-store.failing-half-open");

        // Trigger a failure to open the circuit breaker
        var saveMsg = new SaveSnapshot(new SnapshotMetadata("test-pid", 1, DateTime.UtcNow), "test-snapshot");
        snapshotStore.Tell(saveMsg, TestActor);

        var testScheduler = (TestScheduler)Sys.Scheduler;

        // Advance time past call-timeout to let the save fail and circuit breaker open
        testScheduler.Advance(TimeSpan.FromSeconds(1));

        // Give the async operations time to complete
        await Task.Delay(100);

        // Advance time past reset-timeout to transition to half-open
        testScheduler.Advance(TimeSpan.FromSeconds(1));

        // Give the transition time to complete
        await Task.Delay(100);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var pluginHealth = await Extension.CheckSnapshotStoreHealthAsync("akka.persistence.snapshot-store.failing-half-open", cts.Token);

        Assert.Equal(PersistenceHealthStatus.Degraded, pluginHealth.Status);
        Assert.Contains("Circuit breaker is half-open", pluginHealth.Description);
    }
}

/// <summary>
/// Test snapshot store that always fails saves to trigger circuit breaker
/// </summary>
public class FailingSnapshotStore : LocalSnapshotStore
{
    protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated snapshot store save failure");
    }
}