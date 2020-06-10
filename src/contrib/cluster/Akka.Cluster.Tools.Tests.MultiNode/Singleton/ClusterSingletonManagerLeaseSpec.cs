//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerLeaseSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;

namespace Akka.Cluster.Tools.Tests.MultiNode.Singleton
{
    public class ClusterSingletonManagerLeaseSpecConfig : MultiNodeConfig
    {
        public RoleName Controller { get; }
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public ClusterSingletonManagerLeaseSpecConfig()
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.loglevel = DEBUG
                akka.actor.provider = ""cluster""
                akka.remote.log-remote-lifecycle-events = off
                #akka.cluster.auto-down-unreachable-after = off
                #akka.cluster.downing-provider-class = akka.cluster.testkit.AutoDowning
                akka.cluster.auto-down-unreachable-after = 0s
                akka.cluster.testkit.auto-down-unreachable-after = 0s
                test-lease {
                    lease-class = ""Akka.Cluster.Tools.Tests.MultiNode.TestLeaseActorClient, Akka.Cluster.Tools.Tests.MultiNode""
                    heartbeat-interval = 1s
                    heartbeat-timeout = 120s
                    lease-operation-timeout = 3s
                }
                akka.cluster.singleton {
                    use-lease = ""test-lease""
                }
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(ClusterSingletonProxy.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            NodeConfig(new[] { First, Second, Third, Fourth }, new[] { ConfigurationFactory.ParseString(@"
                akka.cluster.roles = [worker]
            ") });
        }

        internal class ImportantSingleton : ActorBase
        {
            internal sealed class Response : IEquatable<Response>
            {
                public object Msg { get; }
                public Address Address { get; }

                public Response(object msg, Address address)
                {
                    Msg = msg;
                    Address = address;
                }

                public bool Equals(Response other)
                {
                    if (ReferenceEquals(other, null)) return false;
                    if (ReferenceEquals(this, other)) return true;

                    return Equals(Msg, other.Msg) && Equals(Address, other.Address);
                }

                public override bool Equals(object obj) => obj is Response r && Equals(r);

                public override int GetHashCode()
                {
                    unchecked
                    {
                        var hashCode = Msg.GetHashCode();
                        hashCode = (hashCode * 397) ^ Address.GetHashCode();
                        return hashCode;
                    }
                }
            }

            public static Props Props => Props.Create(() => new ImportantSingleton());

            private readonly ILoggingAdapter _log = Context.GetLogger();
            private Address selfAddress;

            public ImportantSingleton()
            {
                selfAddress = Cluster.Get(Context.System).SelfAddress;
            }

            protected override void PreStart()
            {
                _log.Info("Singleton starting");
            }

            protected override void PostStop()
            {
                _log.Info("Singleton stopping");
            }

            protected override bool Receive(object message)
            {
                Sender.Tell(new Response(message, selfAddress));
                return true;
            }
        }
    }

    public class ClusterSingletonManagerLeaseSpec : MultiNodeClusterSpec
    {
        private readonly ClusterSingletonManagerLeaseSpecConfig _config;

        protected override int InitialParticipantsValueFactory => Roles.Count;

        // used on the controller
        private TestProbe leaseProbe;

        public ClusterSingletonManagerLeaseSpec()
            : this(new ClusterSingletonManagerLeaseSpecConfig())
        { }

        protected ClusterSingletonManagerLeaseSpec(ClusterSingletonManagerLeaseSpecConfig config)
            : base(config, typeof(ClusterSingletonManagerLeaseSpec))
        {
            _config = config;

            leaseProbe = CreateTestProbe();
        }

        [MultiNodeFact]
        public void ClusterSingletonManagerLeaseSpecs()
        {
            Cluster_singleton_manager_with_lease_should_form_a_cluster();
            Cluster_singleton_manager_with_lease_should_start_test_lease();
            Cluster_singleton_manager_with_lease_should_find_the_lease_on_every_node();
            Cluster_singleton_manager_with_lease_should_Start_singleton_and_ping_from_all_nodes();
            Cluster_singleton_manager_with_lease_should_Move_singleton_when_oldest_node_downed();
        }

        public void Cluster_singleton_manager_with_lease_should_form_a_cluster()
        {
            AwaitClusterUp(_config.Controller, _config.First);
            EnterBarrier("initial-up");
            RunOn(() =>
            {
                JoinWithin(_config.First);
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.Status).Should().BeEquivalentTo(MemberStatus.Up, MemberStatus.Up, MemberStatus.Up);
                }, TimeSpan.FromSeconds(10));
            }, _config.Second);

            EnterBarrier("second-up");
            RunOn(() =>
            {
                JoinWithin(_config.First);
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.Status).Should().BeEquivalentTo(MemberStatus.Up, MemberStatus.Up, MemberStatus.Up, MemberStatus.Up);
                }, TimeSpan.FromSeconds(10));
            }, _config.Third);
            EnterBarrier("third-up");
            RunOn(() =>
            {
                JoinWithin(_config.First);
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.Status).Should().BeEquivalentTo(MemberStatus.Up, MemberStatus.Up, MemberStatus.Up, MemberStatus.Up, MemberStatus.Up);
                }, TimeSpan.FromSeconds(10));
            }, _config.Fourth);
            EnterBarrier("fourth-up");
        }

        public void Cluster_singleton_manager_with_lease_should_start_test_lease()
        {
            RunOn(() =>
            {
                Sys.ActorOf(TestLeaseActor.Props, $"lease-{Sys.Name}");
            }, _config.Controller);

            EnterBarrier("lease-actor-started");
        }

        public void Cluster_singleton_manager_with_lease_should_find_the_lease_on_every_node()
        {
            Sys.ActorSelection(Node(_config.Controller) / "user" / $"lease-{Sys.Name}").Tell(new Identify(null));
            IActorRef leaseRef = ExpectMsg<ActorIdentity>().Subject;
            TestLeaseActorClientExt.Get(Sys).SetActorLease(leaseRef);
            EnterBarrier("singleton-started");
        }

        public void Cluster_singleton_manager_with_lease_should_Start_singleton_and_ping_from_all_nodes()
        {
            RunOn(() =>
            {
                Sys.ActorOf(
                    ClusterSingletonManager.Props(
                        ClusterSingletonManagerLeaseSpecConfig.ImportantSingleton.Props, PoisonPill.Instance, ClusterSingletonManagerSettings.Create(Sys).WithRole("worker")),
                        "important");
            }, _config.First, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("singleton-started");

            var proxy = Sys.ActorOf(
                ClusterSingletonProxy.Props(
                    singletonManagerPath: "/user/important",
                    settings: ClusterSingletonProxySettings.Create(Sys).WithRole("worker")));

            RunOn(() =>
            {
                proxy.Tell("Ping");
                // lease has not been granted so now allowed to come up
                ExpectNoMsg(TimeSpan.FromSeconds(2));
            }, _config.First, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("singleton-pending");

            RunOn(() =>
            {
                TestLeaseActorClientExt.Get(Sys).GetLeaseActor().Tell(TestLeaseActor.GetRequests.Instance);
                ExpectMsg<TestLeaseActor.LeaseRequests>(msg => msg.Requests.Should().BeEquivalentTo(new TestLeaseActor.Acquire(GetAddress(_config.First).HostPort())));
                TestLeaseActorClientExt.Get(Sys).GetLeaseActor().Tell(new TestLeaseActor.ActionRequest(new TestLeaseActor.Acquire(GetAddress(_config.First).HostPort()), true));
            }, _config.Controller);
            EnterBarrier("lease-acquired");

            RunOn(() =>
            {
                ExpectMsg(new ClusterSingletonManagerLeaseSpecConfig.ImportantSingleton.Response("Ping", GetAddress(_config.First)));
            }, _config.First, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("pinged");
        }

        public void Cluster_singleton_manager_with_lease_should_Move_singleton_when_oldest_node_downed()
        {
            Cluster.State.Members.Count.ShouldBe(5);

            RunOn(() =>
            {
                Cluster.Down(GetAddress(_config.First));
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.Status).Should().BeEquivalentTo(MemberStatus.Up, MemberStatus.Up, MemberStatus.Up, MemberStatus.Up);
                }, TimeSpan.FromSeconds(20));

                TestLeaseActor.LeaseRequests requests = null;
                AwaitAssert(() =>
                {
                    TestLeaseActorClientExt.Get(Sys).GetLeaseActor().Tell(TestLeaseActor.GetRequests.Instance);
                    var msg = ExpectMsg<TestLeaseActor.LeaseRequests>();

                    msg.Requests.Count.ShouldBe(2, "Requests: " + msg);
                    requests = msg;
                }, TimeSpan.FromSeconds(10));

                requests.Requests.Should().Contain(new TestLeaseActor.Release(GetAddress(_config.First).HostPort()));
                requests.Requests.Should().Contain(new TestLeaseActor.Acquire(GetAddress(_config.Second).HostPort()));

            }, _config.Controller);

            RunOn(() =>
            {
                AwaitAssert(() =>
                {
                    Cluster.State.Members.Select(i => i.Status).Should().BeEquivalentTo(MemberStatus.Up, MemberStatus.Up, MemberStatus.Up, MemberStatus.Up);
                }, TimeSpan.FromSeconds(20));
            }, _config.Second, _config.Third, _config.Fourth);

            EnterBarrier("first node downed");

            var proxy = Sys.ActorOf(
                ClusterSingletonProxy.Props(
                    singletonManagerPath: "/user/important",
                    settings: ClusterSingletonProxySettings.Create(Sys).WithRole("worker")));

            RunOn(() =>
            {
                proxy.Tell("Ping");
                // lease has not been granted so now allowed to come up
                ExpectNoMsg(TimeSpan.FromSeconds(2));
            }, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("singleton-not-migrated");

            RunOn(() =>
            {
                TestLeaseActorClientExt.Get(Sys).GetLeaseActor().Tell(new TestLeaseActor.ActionRequest(new TestLeaseActor.Acquire(GetAddress(_config.Second).HostPort()), true));
            }, _config.Controller);

            EnterBarrier("singleton-moved-to-second");

            RunOn(() =>
            {
                proxy.Tell("Ping");
                ExpectMsg(new ClusterSingletonManagerLeaseSpecConfig.ImportantSingleton.Response("Ping", GetAddress(_config.Second)), TimeSpan.FromSeconds(20));
            }, _config.Second, _config.Third, _config.Fourth);
            EnterBarrier("finished");
        }
    }
}
