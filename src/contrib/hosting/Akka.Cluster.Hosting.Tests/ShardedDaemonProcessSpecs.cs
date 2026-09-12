using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Akka.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;


namespace Akka.Cluster.Hosting.Tests;

public class ShardedDaemonProcessSpecs: Akka.Hosting.TestKit.TestKit
{
    private sealed class Stop
    {
        public static Stop Instance { get; } = new();
        private Stop() { }
    }

    internal sealed class Started
    {
        public int Id { get; }
        public IActorRef SelfRef { get; }

        public Started(int id, IActorRef selfRef)
        {
            Id = id;
            SelfRef = selfRef;
        }
    }

    internal class MyDaemonActor : UntypedActor
    {
        private readonly int _id;
        private readonly IActorRef _probe;
        private readonly ILoggingAdapter _log;

        public MyDaemonActor(int id, IRequiredActor<TestProbe> probe)
        {
            _id = id;
            _probe = probe.ActorRef;
            _log = Context.GetLogger();
        }

        protected override void PreStart()
        {
            base.PreStart();
            _probe.Tell(new Started(_id, Context.Self));
            _log.Info("Actor {0} started", _id);
        }

        protected override void PostStop()
        {
            base.PostStop();
            _log.Info("Actor {0} stopped", _id);
        }

        protected override void OnReceive(object message)
        {
            switch (message)
            {
                case Stop:
                    Context.Stop(Self);
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }
    }

    internal enum ShardedDaemonRouter { }

    private Cluster _cluster = null!;
    
    public ShardedDaemonProcessSpecs(ITestOutputHelper output) : base(nameof(ShardedDaemonProcessSpecs), output)
    {
    }
    
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithRemoting(new RemoteOptions
            {
                Port = 0
            })
            .WithClustering()
            // Join cluster via WithActors (not AddStartup) so it runs before
            // WithShardedDaemonProcess, which depends on cluster formation.
            .WithActors(async (system, _) =>
            {
                var cluster = Cluster.Get(system);
                await cluster.JoinAsync(cluster.SelfAddress);
            })
            .WithShardedDaemonProcess<ShardedDaemonRouter>(
                name: "test", 
                numberOfInstances: 5, 
                entityPropsFactory: (_, _, resolver) => id => resolver.Props(typeof(MyDaemonActor), id),
                options: new ClusterDaemonOptions
                {
                    KeepAliveInterval = 500.Milliseconds()
                });
    }

    protected override async Task BeforeTestStart()
    {
        _cluster = Cluster.Get(Sys);
        
        await AwaitAssertAsync(() => Assert.Equal(MemberStatus.Up, _cluster.SelfMember.Status), 3.Seconds());
    }

    [Fact]
    public async Task ShardedDaemonProcess_must_start_N_actors_with_unique_ids()
    {
        var started = new List<Started>();
        foreach (var _ in Enumerable.Range(0, 5))
        {
            started.Add(await ExpectMsgAsync<Started>());
        }
        
        Assert.Equal(5, started.Count);
        Assert.Equal([0, 1, 2, 3, 4], started.Select(s => s.Id).OrderBy(s => s));
        await ExpectNoMsgAsync(1.Seconds());
    }

    [Fact]
    public async Task ShardedDaemonProcess_must_restart_actors_if_they_stop()
    {
        var startMessages = new List<Started>();
        foreach (var _ in Enumerable.Range(0, 5))
        {
            startMessages.Add(await ExpectMsgAsync<Started>());
        }
        
        Assert.Equal(5, startMessages.Count);
        Assert.Equal([0, 1, 2, 3, 4], startMessages.Select(s => s.Id).OrderBy(s => s));

        // Stop all entities
        foreach (var start in startMessages)
        {
            start.SelfRef.Tell(Stop.Instance);
        }

        startMessages.Clear();
        // periodic ping every 1s makes it restart
        foreach (var _ in Enumerable.Range(0, 5))
        {
            startMessages.Add(await ExpectMsgAsync<Started>());
        }
        
        Assert.Equal(5, startMessages.Count);
        Assert.Equal([0, 1, 2, 3, 4], startMessages.Select(s => s.Id).OrderBy(s => s));
    }
}

public class ShardedDaemonProcessFailureSpecs : Akka.Hosting.TestKit.TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder
            .WithRemoting()
            .WithClustering()
            // Join cluster via WithActors (not AddStartup) so it runs before
            // WithShardedDaemonProcess, which depends on cluster formation.
            .WithActors(async (system, _) =>
            {
                var cluster = Cluster.Get(system);
                await cluster.JoinAsync(cluster.SelfAddress);
            })
            .WithShardedDaemonProcess<ShardedDaemonProcessSpecs.ShardedDaemonRouter>(
                name: "test", 
                numberOfInstances: 5, 
                entityPropsFactory: (_, _, resolver) => id => resolver.Props(typeof(ShardedDaemonProcessSpecs.MyDaemonActor), id),
                options: new ClusterDaemonOptions
                {
                    KeepAliveInterval = 500.Milliseconds(),
                    Role = "DoNotExist"
                });
    }
    
    [Fact]
    public async Task ShardedDaemonProcess_must_not_run_if_the_role_does_not_match_node_role()
    {
        var registry = Host.Services.GetRequiredService<ActorRegistry>();
        Assert.False(registry.TryGet<ShardedDaemonProcessSpecs.ShardedDaemonRouter>(out _));

        await ExpectNoMsgAsync(1.Seconds());
    }
}
