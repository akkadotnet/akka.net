//-----------------------------------------------------------------------
// <copyright file="ShardedDaemonProcessProxySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace Akka.Cluster.Sharding.Tests;

public class ShardedDaemonProcessProxySpec : AkkaSpec
{
    private static Config GetConfig()
    {
        return ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = cluster
                akka.remote.dot-netty.tcp.port = 0
                akka.remote.dot-netty.tcp.hostname = localhost

                # ping often/start fast for test
                akka.cluster.sharded-daemon-process.keep-alive-interval = 1s
                akka.cluster.roles = [workers]
                akka.coordinated-shutdown.terminate-actor-system = off
                akka.coordinated-shutdown.run-by-actor-system-terminate = off")
            .WithFallback(ClusterSharding.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(DistributedData.DistributedData.DefaultConfig());
    }

    private readonly ActorSystem _proxySystem;

    public ShardedDaemonProcessProxySpec(ITestOutputHelper output)
        : base(GetConfig(), output: output)
    {
        _proxySystem = ActorSystem.Create(Sys.Name,
            ConfigurationFactory
                .ParseString("akka.cluster.roles=[proxy]").WithFallback(Sys.Settings.Config));
        InitializeLogger(_proxySystem, "PROXY");
    }
    
    private class EchoActor : ReceiveActor
    {
        public static Props EchoProps(int i) => Props.Create(() => new EchoActor());
        
        public EchoActor()
        {
            ReceiveAny(msg => Sender.Tell(msg));
        }
    }
    
    [Fact]
    public async Task ShardedDaemonProcessProxy_must_start_daemon_process_on_proxy()
    {
        // form a cluster with one node
        Cluster.Get(Sys).Join(Cluster.Get(Sys).SelfAddress);
        Cluster.Get(_proxySystem).Join(Cluster.Get(Sys).SelfAddress);
        
        // validate that we have a 2 node cluster with both members marked as up
        await AwaitAssertAsync(() =>
        {
            Cluster.Get(Sys).State.Members.Count(x => x.Status == MemberStatus.Up).Should().Be(2);
            Cluster.Get(_proxySystem).State.Members.Count(x => x.Status == MemberStatus.Up).Should().Be(2);
        });
        
        // <PushDaemon>
        // start the daemon process on the host
        var name = "daemonTest";
        var targetRole = "workers";
        var numWorkers = 10;
        var settings = ShardedDaemonProcessSettings.Create(Sys).WithRole(targetRole);
        IActorRef host = ShardedDaemonProcess.Get(Sys).Init(name, numWorkers, EchoActor.EchoProps, settings, PoisonPill.Instance);
        
        // ping some of the workers via the host
        for(var i = 0; i < numWorkers; i++)
        {
            var result = await host.Ask<int>(i);
            result.Should().Be(i);
        }
        // </PushDaemon>
        
        // <PushDaemonProxy>
        // start the proxy on the proxy system, which runs on a different role not capable of hosting workers
        IActorRef proxy = ShardedDaemonProcess.Get(_proxySystem).InitProxy(name, numWorkers, targetRole);
        
        // ping some of the workers via the proxy
        for(var i = 0; i < numWorkers; i++)
        {
            var result = await proxy.Ask<int>(i);
            result.Should().Be(i);
        }
        // </PushDaemonProxy>
    }

    protected override void AfterAll()
    {
        Shutdown(_proxySystem);
        base.AfterAll();
    }
}
