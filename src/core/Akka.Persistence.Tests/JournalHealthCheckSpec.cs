// -----------------------------------------------------------------------
//  <copyright file="PersistenceHealthCheckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Akka.TestKit.Configs;
using Xunit;

namespace Akka.Persistence.Tests;

public class JournalHealthCheckSpec : PersistenceSpec
{
    private static Config HealthCheckConfig()
    {
        const string extraConfig = """

                                               akka.persistence.journal.failing-open {
                                                   class = "Akka.Persistence.Tests.FailingJournal, Akka.Persistence.Tests"
                                                   circuit-breaker {
                                                       max-failures = 1
                                                       call-timeout = 1s
                                                       reset-timeout = 10s
                                                   }
                                               }
                                               akka.persistence.journal.failing-half-open {
                                                   class = "Akka.Persistence.Tests.FailingJournal, Akka.Persistence.Tests"
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
            .WithFallback(Configuration("PersistenceHealthCheckSpec", extraConfig: extraConfig));
    }

    public JournalHealthCheckSpec(ITestOutputHelper output) : base(HealthCheckConfig(), output)
    {
    }

    [Theory]
    [InlineData(null)] // default plugin
    [InlineData("akka.persistence.journal.inmem")]
    public async Task JournalHealthCheck_should_default_to_Healthy(string? pluginId)
    {
        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var pluginHealth = await Extension.CheckJournalHealthAsync(pluginId, cts.Token);

        Assert.Equal(PersistenceHealthStatus.Healthy, pluginHealth.Status);
        Assert.NotNull(pluginHealth.Description);
    }

    [Fact]
    public async Task JournalHealthCheck_should_return_Degraded_when_CircuitBreaker_is_Open()
    {
        // Get the journal actor reference
        var journal = Extension.JournalFor("akka.persistence.journal.failing-open");

        // Trigger a failure to open the circuit breaker
        var writeMsg = new WriteMessages(new[] { new AtomicWrite(new Persistent("test", 1, "test-pid")) }.ToImmutableList(),
            TestActor, 1);
        journal.Tell(writeMsg, TestActor);

        // Advance time to let the write fail and circuit breaker open
        var testScheduler = (TestScheduler)Sys.Scheduler;
        testScheduler.Advance(TimeSpan.FromSeconds(2));

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var pluginHealth = await Extension.CheckJournalHealthAsync("akka.persistence.journal.failing-open", cts.Token);

        Assert.Equal(PersistenceHealthStatus.Degraded, pluginHealth.Status);
        Assert.Contains("Circuit breaker is open", pluginHealth.Description);
    }

    [Fact]
    public async Task JournalHealthCheck_should_return_Degraded_when_CircuitBreaker_is_HalfOpen()
    {
        // Get the journal actor reference
        var journal = Extension.JournalFor("akka.persistence.journal.failing-half-open");

        // Trigger a failure to open the circuit breaker
        var writeMsg = new WriteMessages(new[] { new AtomicWrite(new Persistent("test", 1, "test-pid")) }.ToImmutableList(),
            TestActor, 1);
        journal.Tell(writeMsg, TestActor);

        var testScheduler = (TestScheduler)Sys.Scheduler;

        // Advance time past call-timeout to let the write fail and circuit breaker open
        testScheduler.Advance(TimeSpan.FromSeconds(1));

        // Give the async operations time to complete
        await Task.Delay(100);

        // Advance time past reset-timeout to transition to half-open
        testScheduler.Advance(TimeSpan.FromSeconds(1));

        // Give the transition time to complete
        await Task.Delay(100);

        using var cts = new CancellationTokenSource(RemainingOrDefault);
        var pluginHealth = await Extension.CheckJournalHealthAsync("akka.persistence.journal.failing-half-open", cts.Token);

        Assert.Equal(PersistenceHealthStatus.Degraded, pluginHealth.Status);
        Assert.Contains("Circuit breaker is half-open", pluginHealth.Description);
    }
}

/// <summary>
/// Test journal that always fails writes to trigger circuit breaker
/// </summary>
public class FailingJournal : MemoryJournal
{
    protected override Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
    {
        throw new InvalidOperationException("Simulated journal write failure");
    }
}