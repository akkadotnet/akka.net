using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Akka.TestKit.Xunit.Internals;
using Microsoft.Extensions.Hosting;
using Xunit;


namespace Akka.Cluster.Hosting.Tests;

public static class TestHelper
{
    
    public static void ConfigureHost(this AkkaConfigurationBuilder builder, 
        Action<AkkaConfigurationBuilder> specBuilder, 
        ClusterOptions options, TaskCompletionSource tcs, ITestOutputHelper output)
    {
        builder
            .WithRemoting("localhost", 0)
            .WithClustering(options)
            .WithActors((system, registry) =>
            {
                var extSystem = (ExtendedActorSystem)system;
                var logger = extSystem.SystemActorOf(Props.Create(() => new TestOutputLogger(output)));
                logger.Tell(new InitializeLogger(system.EventStream));
            })
            // Use WithActors (not AddStartup) so cluster join runs as an _actorStarter
            // before any cluster-dependent starters like WithShardRegion registered by specBuilder.
            .WithActors(async (system, registry) =>
            {
                var cluster = Cluster.Get(system);
                cluster.RegisterOnMemberUp(tcs.SetResult);
                if (options.SeedNodes == null || options.SeedNodes.Length == 0)
                {
                    var myAddress = cluster.SelfAddress;
                    await cluster.JoinAsync(myAddress);
                }
            });
        specBuilder(builder);
    }

    public static async Task<IHost> CreateHost(Action<AkkaConfigurationBuilder> specBuilder, ClusterOptions options, ITestOutputHelper output)
    {
        var tcs = new TaskCompletionSource();

        var host = new HostBuilder()
            .ConfigureServices(collection =>
            {
                collection.AddAkka("TestSys", (configurationBuilder, provider) =>
                {
                   configurationBuilder.ConfigureHost(specBuilder, options, tcs, output);
                });
            }).Build();

        try
        {
            // Use a generous startup timeout — must not be so tight that it triggers
            // host.StopAsync (and CoordinatedShutdown) while startup is still in progress.
            using var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await host.StartAsync(startupCts.Token);

            // Separate timeout for cluster formation (happens after host startup completes). A second
            // node joining a seed has been seen to need ~30 s on a starved 2-vCPU CI agent.
            using var clusterCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await tcs.Task.WaitAsync(clusterCts.Token);

            return host;
        }
        catch
        {
            // the caller never gets the host, so stop it here or its ActorSystem keeps gossiping and
            // heartbeating through every later test in the assembly
            await StopAndDisposeAsync(host, output);
            throw;
        }
    }

    private static async Task StopAndDisposeAsync(IHost host, ITestOutputHelper output)
    {
        try
        {
            using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await host.StopAsync(stopCts.Token);
        }
        catch (Exception ex)
        {
            // best effort: the failure that brought us here is the one worth reporting
            output.WriteLine($"Stopping a host that failed to start threw: {ex}");
        }
        finally
        {
            host.Dispose();
        }
    }
    
    public static TimeSpan Seconds(this double value)
        => TimeSpan.FromSeconds(value);
    
    public static TimeSpan Seconds(this int value)
        => TimeSpan.FromSeconds(value);
    
    public static TimeSpan Milliseconds(this double value)
        => TimeSpan.FromMilliseconds(value);
    
    public static TimeSpan Milliseconds(this int value)
        => TimeSpan.FromMilliseconds(value);

    public static void CollectionEquals<T>(this IEnumerable<T> list1, IEnumerable<T> list2)
    {
        Assert.Equal(list1.OrderBy(a => a), list2.OrderBy(a => a));
    }
}