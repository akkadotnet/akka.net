//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Event;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using FluentAssertions;
using FluentAssertions.Extensions;
using System.Threading.Tasks;

namespace Akka.Cluster.Tools.Tests.MultiNode.PublishSubscribe;

public class DistributedPubSubRestartSpecConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }
    public RoleName Third { get; }

    public DistributedPubSubRestartSpecConfig()
    {
        First = Role("first");
        Second = Role("second");
        Third = Role("third");

        CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = INFO
                akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
                akka.cluster.pub-sub.gossip-interval = 500ms
                akka.remote.log-remote-lifecycle-events = off
                akka.cluster.auto-down-unreachable-after = off
            ").WithFallback(DistributedPubSub.DefaultConfig());

        TestTransport = true;
    }

    internal class Shutdown : ReceiveActor
    {
        public Shutdown()
        {
            Context.GetLogger().Info("Shutdown actor started on {0}", Context.System.Name);
            Receive<string>(str => str.Equals("shutdown"), _ =>
            {
                Context.System.Terminate();
            });
        }
    }
}

public class DistributedPubSubRestartSpec : MultiNodeClusterSpec
{
    private readonly DistributedPubSubRestartSpecConfig _config;

    public DistributedPubSubRestartSpec() : this(new DistributedPubSubRestartSpecConfig())
    {
    }

    protected DistributedPubSubRestartSpec(DistributedPubSubRestartSpecConfig config) : base(config, typeof(DistributedPubSubRestartSpec))
    {
        _config = config;
    }

    [MultiNodeFact]
    public async Task DistributedPubSubRestartSpecs()
    {
        await A_Cluster_with_DistributedPubSub_must_startup_3_node_cluster();
        await A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address();
    }

    public async Task A_Cluster_with_DistributedPubSub_must_startup_3_node_cluster()
    {
        await WithinAsync(15.Seconds(), async () =>
        {
            await JoinAsync(_config.First, _config.First);
            await JoinAsync(_config.Second, _config.First);
            await JoinAsync(_config.Third, _config.First);
            await EnterBarrierAsync("after-1");
        });
    }

    public async Task A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address()
    {
        await WithinAsync(30.Seconds(), async () =>
        {
            Mediator.Tell(new Subscribe("topic1", TestActor));
            ExpectMsg<SubscribeAck>();
            await CountAsync(3);

            RunOn(() =>
            {
                Mediator.Tell(new Publish("topic1", "msg1"));
            }, _config.First);
            await EnterBarrierAsync("pub-msg1");

            await ExpectMsgAsync("msg1");
            await EnterBarrierAsync("got-msg1");

            // All nodes capture baseline DeltaCount before node-specific logic
            Mediator.Tell(DeltaCount.Instance);
            var oldDeltaCount = await ExpectMsgAsync<long>();
            await EnterBarrierAsync("old-delta-count");

            await RunOnAsync(async () =>
            {
                await EnterBarrierAsync("end");

                Mediator.Tell(DeltaCount.Instance);
                var deltaCount = await ExpectMsgAsync<long>();
                deltaCount.Should().Be(oldDeltaCount);
            }, _config.Second);

            await RunOnAsync(async () =>
            {
                var thirdAddress = (await NodeAsync(_config.Third)).Address;
                await TestConductor.Shutdown(_config.Third).WaitAsync(30.Seconds());

                await WithinAsync(20.Seconds(), async () =>
                {
                    await AwaitAssertAsync(async () =>
                    {
                        Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell(new Identify(null));
                        (await ExpectMsgAsync<ActorIdentity>(1.Seconds())).Subject.Should().NotBeNull();
                    });
                });

                Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell("shutdown");

                await EnterBarrierAsync("end");

                Mediator.Tell(DeltaCount.Instance);
                var deltaCount = await ExpectMsgAsync<long>();
                deltaCount.Should().Be(oldDeltaCount);
            }, _config.First);

            await RunOnAsync(async () =>
            {
                var node3Address = Cluster.Get(Sys).SelfAddress;
                await Sys.WhenTerminated.WaitAsync(30.Seconds());
                var newSystem = ActorSystem.Create(
                    Sys.Name,
                    ConfigurationFactory
                        .ParseString($"akka.remote.dot-netty.tcp.port={node3Address.Port}")
                        .WithFallback(Sys.Settings.Config));

                try
                {
                    // don't join the old cluster
                    await Cluster.Get(newSystem).JoinAsync(Cluster.Get(newSystem).SelfAddress);
                    var newMediator = DistributedPubSub.Get(newSystem).Mediator;
                    var probe = CreateTestProbe(newSystem);
                    newMediator.Tell(new Subscribe("topic2", probe.Ref), probe.Ref);
                    await probe.ExpectMsgAsync<SubscribeAck>();

                    // let them gossip, but Delta should not be exchanged
                    await probe.ExpectNoMsgAsync(5.Seconds());
                    newMediator.Tell(DeltaCount.Instance, probe.Ref);
                    await probe.ExpectMsgAsync(0L);

                    newSystem.Log.Info("Shutdown actor started on {0}",node3Address);
                    newSystem.ActorOf<DistributedPubSubRestartSpecConfig.Shutdown>("shutdown");
                    await newSystem.WhenTerminated.WaitAsync(30.Seconds());
                }
                finally
                {
                    await newSystem.Terminate().WaitAsync(30.Seconds());
                }
            }, _config.Third);
        });
    }

    protected override int InitialParticipantsValueFactory => Roles.Count;

    private IActorRef CreateMediator()
    {
        return DistributedPubSub.Get(Sys).Mediator;
    }

    private IActorRef Mediator
    {
        get
        {
            return DistributedPubSub.Get(Sys).Mediator;
        }
    }

    private async Task JoinAsync(RoleName from, RoleName to)
    {
        RunOn(() =>
        {
            Cluster.Get(Sys).Join(Node(to).Address);
            CreateMediator();
        }, from);
        await EnterBarrierAsync(from.Name + "-joined");
    }

    private async Task CountAsync(int expected)
    {
        var probe = CreateTestProbe();
        await AwaitAssertAsync(async () =>
        {
            Mediator.Tell(Count.Instance, probe.Ref);
            (await probe.ExpectMsgAsync<int>()).Should().Be(expected);
        });
    }
}