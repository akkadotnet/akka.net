// -----------------------------------------------------------------------
//  <copyright file="OldChangedBufferSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Cluster.Tools.Tests.Singleton;

/// <summary>
/// Reproduction for https://github.com/akkadotnet/akka.net/issues/7196 - clearly, what we did
/// </summary>
public class OldChangedBufferSpecs : AkkaSpec
{
    private readonly ActorSystem _hostNodeV1;
    private readonly ActorSystem _hostNodeV2;

    private static Config OriginalNodeConfig() => """
                                                  
                                                                akka.loglevel = INFO
                                                                akka.actor.provider = "cluster"
                                                                akka.cluster.roles = [non-singleton]
                                                                akka.cluster.singleton.min-number-of-hand-over-retries = 5
                                                                akka.cluster.app-version = "1.0.0"
                                                                akka.remote {
                                                                  dot-netty.tcp {
                                                                    hostname = "127.0.0.1"
                                                                    port = 0
                                                                  }
                                                                }
                                                  """;

    private static Config V2NodeConfig(ActorSystem originalSys) => ConfigurationFactory.ParseString(
        "akka.cluster.app-version = \"1.0.2\"").WithFallback(originalSys.Settings.Config);

    public OldChangedBufferSpecs(ITestOutputHelper output) : base(OriginalNodeConfig(), output)
    {
        _hostNodeV1 = ActorSystem.Create(Sys.Name,
            ConfigurationFactory.ParseString("akka.cluster.roles = [singleton]").WithFallback(Sys.Settings.Config));
        InitializeLogger(_hostNodeV1);
        _hostNodeV2 = ActorSystem.Create(Sys.Name,
            ConfigurationFactory.ParseString("akka.cluster.roles = [singleton]").WithFallback(V2NodeConfig(Sys)));
        InitializeLogger(_hostNodeV2);
    }

    [Fact(DisplayName =
        "Singletons should not move to higher AppVersion nodes until after older incarnation is downed")]
    public async Task Bugfix7196Spec()
    {
        await JoinAsync(Sys, Sys); // have to do a self join first
        await JoinAsync(_hostNodeV1, Sys);

        var proxy = Sys.ActorOf(
            ClusterSingletonProxy.Props("user/echo",
                ClusterSingletonProxySettings.Create(Sys).WithRole("singleton")), "proxy3");

        // confirm that singleton is on _hostNodeV1
        await AssertSingletonHostedOn(proxy, _hostNodeV1);
        
        // have _hostNodeV2 join the cluster
        await JoinAsync(_hostNodeV2, Sys);
        
        // confirm that singleton is STILL on _hostNodeV1
        await AssertSingletonHostedOn(proxy, _hostNodeV1);
        
        // now, down the original node
        Cluster.Get(Sys).Leave(Cluster.Get(_hostNodeV1).SelfAddress);
        
        // validate that _hostNodeV1 is no longer in the cluster
        await WithinAsync(TimeSpan.FromSeconds(5), () =>
        {
            return AwaitAssertAsync(() =>
            {
                Cluster.Get(Sys).State.Members.Select(x => x.UniqueAddress).Should()
                    .NotContain(Cluster.Get(_hostNodeV1).SelfUniqueAddress);
            });
        });
        
        // validate that the singleton has moved to _hostNodeV2
        await AssertSingletonHostedOn(proxy, _hostNodeV2);
    }

    private async Task AssertSingletonHostedOn(IActorRef proxy, ActorSystem targetNode)
    {
        await WithinAsync(TimeSpan.FromSeconds(5), () =>
        {
            return AwaitAssertAsync(() =>
            {
                var probe = CreateTestProbe(Sys);
                proxy.Tell("hello", probe.Ref);
                probe.ExpectMsg<UniqueAddress>(TimeSpan.FromSeconds(1))
                    .Should()
                    .Be(Cluster.Get(targetNode).SelfUniqueAddress);
            });
        });
    }

    public async Task JoinAsync(ActorSystem from, ActorSystem to)
    {
        if (Cluster.Get(from).SelfRoles.Contains("singleton"))
        {
            from.ActorOf(ClusterSingletonManager.Props(Props.Create(() => new Singleton()),
                PoisonPill.Instance,
                ClusterSingletonManagerSettings.Create(from).WithRole("singleton")), "echo");
        }


        await WithinAsync(TimeSpan.FromSeconds(45), () =>
        {
            AwaitAssert(() =>
            {
                Cluster.Get(from).Join(Cluster.Get(to).SelfAddress);
                Cluster.Get(from).State.Members.Select(x => x.UniqueAddress).Should()
                    .Contain(Cluster.Get(from).SelfUniqueAddress);
                Cluster.Get(from)
                    .State.Members.Select(x => x.Status)
                    .ToImmutableHashSet()
                    .Should()
                    .Equal(ImmutableHashSet<MemberStatus>.Empty.Add(MemberStatus.Up));
            });
            return Task.CompletedTask;
        });
    }

    public class Singleton : ReceiveActor
    {
        public Singleton()
        {
            ReceiveAny(_ => { Sender.Tell(Cluster.Get(Context.System).SelfUniqueAddress); });
        }
    }

    protected override void AfterAll()
    {
        Shutdown(_hostNodeV1);
            Shutdown(_hostNodeV2);
        base.AfterAll();
    }
}