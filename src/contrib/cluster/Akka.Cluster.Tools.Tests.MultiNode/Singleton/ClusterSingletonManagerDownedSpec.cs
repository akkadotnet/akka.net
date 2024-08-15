// -----------------------------------------------------------------------
//  <copyright file="ClusterSingletonManagerDownedSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton;

public class ClusterSingletonManagerDownedSpecConfig : MultiNodeConfig
{
    public ClusterSingletonManagerDownedSpecConfig()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
    }

    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }

    internal class EchoStarted
    {
        public static readonly EchoStarted Instance = new();

        private EchoStarted()
        {
        }
    }

    internal class EchoStopped
    {
        public static readonly EchoStopped Instance = new();

        private EchoStopped()
        {
        }
    }

    /// <summary>
    ///     The singleton actor
    /// </summary>
    internal class Echo : UntypedActor
    {
        private readonly IActorRef _testActorRef;

        public Echo(IActorRef testActorRef)
        {
            _testActorRef = testActorRef;
            _testActorRef.Tell(EchoStarted.Instance);
        }

        protected override void PostStop()
        {
            _testActorRef.Tell(EchoStopped.Instance);
        }

        public static Props Props(IActorRef testActorRef)
        {
            return Actor.Props.Create(() => new Echo(testActorRef));
        }

        protected override void OnReceive(object message)
        {
            Sender.Tell(message);
        }
    }
}

public class ClusterSingletonManagerDownedSpec : MultiNodeClusterSpec
{
    private readonly ClusterSingletonManagerDownedSpecConfig _config;
    private readonly Lazy<IActorRef> _echoProxy;

    public ClusterSingletonManagerDownedSpec() : this(new ClusterSingletonManagerDownedSpecConfig())
    {
    }

    protected ClusterSingletonManagerDownedSpec(ClusterSingletonManagerDownedSpecConfig config) : base(config,
        typeof(ClusterSingletonManagerDownedSpec))
    {
        _config = config;

        _echoProxy = new Lazy<IActorRef>(() => Watch(Sys.ActorOf(ClusterSingletonProxy.Props(
                "/user/echo",
                ClusterSingletonProxySettings.Create(Sys)),
            "echoProxy")));
    }

    protected override int InitialParticipantsValueFactory => Roles.Count;

    private void Join(RoleName from, RoleName to)
    {
        RunOn(() =>
        {
            Cluster.Join(Node(to).Address);
            CreateSingleton();
        }, from);
    }

    private IActorRef CreateSingleton()
    {
        return Sys.ActorOf(ClusterSingletonManager.Props(
                ClusterSingletonManagerDownedSpecConfig.Echo.Props(TestActor),
                PoisonPill.Instance,
                ClusterSingletonManagerSettings.Create(Sys)),
            "echo");
    }

    [MultiNodeFact]
    public void ClusterSingletonManagerDownedSpecs()
    {
        ClusterSingletonManager_downing_must_startup_3_node();
    }

    private void ClusterSingletonManager_downing_must_startup_3_node()
    {
        Join(_config.First, _config.First);
        Join(_config.Second, _config.First);
        Join(_config.Third, _config.First);

        Within(15.Seconds(),
            () => { AwaitAssert(() => Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(3)); });

        RunOn(() => { ExpectMsg(ClusterSingletonManagerDownedSpecConfig.EchoStarted.Instance); }, _config.First);

        EnterBarrier("started");
    }

    private void ClusterSingletonManager_downing_must_stop_instance_when_member_is_downed()
    {
        RunOn(() =>
        {
            TestConductor.Blackhole(_config.First, _config.Third, ThrottleTransportAdapter.Direction.Both).Wait();
            TestConductor.Blackhole(_config.Second, _config.Third, ThrottleTransportAdapter.Direction.Both).Wait();

            Within(15.Seconds(), () => { AwaitAssert(() => Cluster.State.Unreachable.Count.Should().Be(1)); });
        }, _config.First);

        EnterBarrier("blackhole-1");

        RunOn(() =>
        {
            // another blackhole so that second can't mark gossip as seen and thereby deferring shutdown of first
            TestConductor.Blackhole(_config.First, _config.Second, ThrottleTransportAdapter.Direction.Both).Wait();
            Cluster.Down(Node(_config.Second).Address);
            Cluster.Down(Cluster.SelfAddress);
            // singleton instance stopped, before failure detection of first-second
            ExpectMsg<ClusterSingletonManagerDownedSpecConfig.EchoStopped>(TimeSpan.FromSeconds(3));
        }, _config.First);

        EnterBarrier("stopped");
    }
}