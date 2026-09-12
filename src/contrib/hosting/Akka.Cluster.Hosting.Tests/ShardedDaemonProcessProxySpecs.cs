using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace Akka.Cluster.Hosting.Tests;

public class ShardedDaemonProcessProxySpecs: Akka.Hosting.TestKit.TestKit
{
    private class EchoActor : ReceiveActor
    {
        public static Props EchoProps(int i) => Props.Create(() => new EchoActor());
        
        public EchoActor()
        {
            ReceiveAny(msg => Sender.Tell(msg));
        }
    }

    internal enum ShardedDaemonRouter { }

    public const int NumWorkers = 10;
    public const string Name = "daemonTest";
    public const string Role = "workers";
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithRemoting(new RemoteOptions
            {
                Port = 0
            })
            .WithClustering(new ClusterOptions
            {
                Roles = [Role]
            })
            // Join cluster via WithActors (not AddStartup) so it runs before
            // WithShardedDaemonProcess, which needs a formed cluster.
            .WithActors((system, _) =>
            {
                var cluster = Cluster.Get(system);
                cluster.Join(cluster.SelfAddress);
            })
            .WithShardedDaemonProcess<ShardedDaemonRouter>(
                name: Name,
                numberOfInstances: NumWorkers,
                entityPropsFactory: (_, _, _) => EchoActor.EchoProps,
                options: new ClusterDaemonOptions
                {
                    KeepAliveInterval = 500.Milliseconds(),
                    Role = Role,
                    HandoffStopMessage = PoisonPill.Instance
                });
    }

    public ShardedDaemonProcessProxySpecs(ITestOutputHelper output) : base(nameof(ShardedDaemonProcessProxySpecs), output)
    { }

    [Fact]
    public async Task ShardedDaemonProcessProxy_must_start_daemon_process_on_proxy()
    {
        // validate that we have a cluster
        await AwaitAssertAsync(() =>
        {
            Assert.Equal(1, Cluster.Get(Sys).State.Members.Count(x => x.Status == MemberStatus.Up));
        });
        
        // <PushDaemon>
        var host = await Host.Services.GetRequiredService<ActorRegistry>().GetAsync<ShardedDaemonRouter>();
        
        // ping some of the workers via the host
        for(var i = 0; i < NumWorkers; i++)
        {
            var result = await host.Ask<int>(i);
            Assert.Equal(i, result);
        }
        // </PushDaemon>
        
        // <PushDaemonProxy>
        // start the proxy on the proxy system, which runs on a different role not capable of hosting workers
        ProxySystem? proxySystem = null;
        try
        {
            proxySystem = new ProxySystem(Output, Sys);
            await proxySystem.InitializeAsync();
            
            // validate that we have a 2 node cluster with both members marked as up
            await AwaitAssertAsync(() =>
            {
                Assert.Equal(2, Cluster.Get(Sys).State.Members.Count(x => x.Status == MemberStatus.Up));
                Assert.Equal(2, Cluster.Get(proxySystem.Sys).State.Members.Count(x => x.Status == MemberStatus.Up));
            });
            
            var proxyRouter = await proxySystem.Host.Services
                .GetRequiredService<ActorRegistry>().GetAsync<ShardedDaemonRouter>();
            
            // ping some of the workers via the proxy
            for(var i = 0; i < NumWorkers; i++)
            {
                var result = await proxyRouter.Ask<int>(i);
                Assert.Equal(i, result);
            }
        }
        finally
        {
            proxySystem?.DisposeAsync();
        }
        // </PushDaemonProxy>
    }

}

public class ProxySystem: Akka.Hosting.TestKit.TestKit
{
    private readonly Cluster _remoteCluster;

    public ProxySystem(ITestOutputHelper? output, ActorSystem remoteSystem)
        : base(nameof(ShardedDaemonProcessProxySpecs), output)
    {
        _remoteCluster = Cluster.Get(remoteSystem);
    }
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithRemoting(new RemoteOptions
            {
                Port = 0
            })
            .WithClustering(new ClusterOptions
            {
                Roles = new[]{ "proxy" }
            })
            // Join cluster via WithActors (not AddStartup) so it runs before
            // WithShardedDaemonProcessProxy, which needs a formed cluster.
            .WithActors((system, _) =>
            {
                var cluster = Cluster.Get(system);
                cluster.Join(_remoteCluster.SelfAddress);
            })
            .WithShardedDaemonProcessProxy<ShardedDaemonProcessProxySpecs.ShardedDaemonRouter>(
                name: ShardedDaemonProcessProxySpecs.Name,
                numberOfInstances: ShardedDaemonProcessProxySpecs.NumWorkers,
                role: ShardedDaemonProcessProxySpecs.Role);
    }
}
