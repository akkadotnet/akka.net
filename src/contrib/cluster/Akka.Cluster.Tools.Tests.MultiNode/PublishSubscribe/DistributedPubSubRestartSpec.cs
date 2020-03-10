//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubRestartSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.PublishSubscribe
{
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
                Receive<string>(str => str.Equals("shutdown"), evt =>
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
        public void DistributedPubSubRestartSpecs()
        {
            A_Cluster_with_DistributedPubSub_must_startup_3_node_cluster();
            A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address();
        }

        public void A_Cluster_with_DistributedPubSub_must_startup_3_node_cluster()
        {
            Within(15.Seconds(), () =>
            {
                Join(_config.First, _config.First);
                Join(_config.Second, _config.First);
                Join(_config.Third, _config.First);
                EnterBarrier("after-1");
            });
        }

        public void A_Cluster_with_DistributedPubSub_must_handle_restart_of_nodes_with_same_address()
        {
            Within(30.Seconds(), () =>
            {
                Mediator.Tell(new Subscribe("topic1", TestActor));
                ExpectMsg<SubscribeAck>();
                AwaitCount(3);

                RunOn(() =>
                {
                    Mediator.Tell(new Publish("topic1", "msg1"));
                }, _config.First);
                EnterBarrier("pub-msg1");

                ExpectMsg("msg1");
                EnterBarrier("got-msg1");

                RunOn(() =>
                {
                    Mediator.Tell(DeltaCount.Instance);
                    var oldDeltaCount = ExpectMsg<long>();

                    EnterBarrier("end");

                    Mediator.Tell(DeltaCount.Instance);
                    var deltaCount = ExpectMsg<long>();
                    deltaCount.Should().Be(oldDeltaCount);
                }, _config.Second);

                RunOn(() =>
                {
                    Mediator.Tell(DeltaCount.Instance);
                    var oldDeltaCount = ExpectMsg<long>();

                    var thirdAddress = Node(_config.Third).Address;
                    TestConductor.Shutdown(_config.Third).Wait();

                    Within(20.Seconds(), () =>
                    {
                        AwaitAssert(() =>
                        {
                            Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell(new Identify(null));
                            ExpectMsg<ActorIdentity>(1.Seconds()).Subject.Should().NotBeNull();
                        });
                    });

                    Sys.ActorSelection(new RootActorPath(thirdAddress) / "user" / "shutdown").Tell("shutdown");

                    EnterBarrier("end");

                    Mediator.Tell(DeltaCount.Instance);
                    var deltaCount = ExpectMsg<long>();
                    deltaCount.Should().Be(oldDeltaCount);
                }, _config.First);

                RunOn(() =>
                {
                    Sys.WhenTerminated.Wait(10.Seconds());
                    var newSystem = ActorSystem.Create(
                        Sys.Name,
                        ConfigurationFactory
                            .ParseString($"akka.remote.dot-netty.tcp.port={Cluster.Get(Sys).SelfAddress.Port}")
                            .WithFallback(Sys.Settings.Config));

                    try
                    {
                        // don't join the old cluster
                        Cluster.Get(newSystem).Join(Cluster.Get(newSystem).SelfAddress);
                        var newMediator = DistributedPubSub.Get(newSystem).Mediator;
                        var probe = CreateTestProbe(newSystem);
                        newMediator.Tell(new Subscribe("topic2", probe.Ref), probe.Ref);
                        probe.ExpectMsg<SubscribeAck>();

                        // let them gossip, but Delta should not be exchanged
                        probe.ExpectNoMsg(5.Seconds());
                        newMediator.Tell(DeltaCount.Instance, probe.Ref);
                        probe.ExpectMsg(0L);

                        newSystem.ActorOf<DistributedPubSubRestartSpecConfig.Shutdown>("shutdown");
                        newSystem.WhenTerminated.Wait(10.Seconds());
                    }
                    finally
                    {
                        newSystem.Terminate();
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

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Join(Node(to).Address);
                CreateMediator();
            }, from);
            EnterBarrier(from.Name + "-joined");
        }

        private void AwaitCount(int expected)
        {
            var probe = CreateTestProbe();
            AwaitAssert(() =>
            {
                Mediator.Tell(Count.Instance, probe.Ref);
                probe.ExpectMsg<int>().Should().Be(expected);
            });
        }
    }
}
