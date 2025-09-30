// -----------------------------------------------------------------------
//  <copyright file="PersistenceHealthCheckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Tests;

public class PersistenceHealthCheckSpec : PersistenceSpec
{
    public PersistenceHealthCheckSpec(ITestOutputHelper output) : base(Configuration("PersistenceHealthCheckSpec"), output)
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
}