//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonManagerChaosSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tests.MultiNode;
using Akka.Cluster.Tools.Singleton;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;
using Akka.TestKit.Internal;
using Akka.TestKit.TestEvent;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public sealed class EchoStarted
    {
        public static readonly EchoStarted Instance = new EchoStarted();

        private EchoStarted() { }
    }

    public class Echo : ReceiveActor
    {
        public Echo(IActorRef testActor)
        {
            testActor.Tell(EchoStarted.Instance);
            ReceiveAny(_ => Sender.Tell(Self));
        }
    }
    public class ClusterSingletonManagerChaosConfig : MultiNodeConfig
    {
        public readonly RoleName Controller;
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;
        public readonly RoleName Fourth;
        public readonly RoleName Fifth;
        public readonly RoleName Sixth;

        public ClusterSingletonManagerChaosConfig()
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");
            Fifth = Role("fifth");
            Sixth = Role("sixth");

            CommonConfig = ConfigurationFactory.ParseString(@"
            akka.loglevel = DEBUG
            akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
            akka.remote.log-remote-lifecycle-events = off
            akka.cluster.auto-down-unreachable-after = 0s
            ")
            .WithFallback(ClusterSingletonManager.DefaultConfig())
            .WithFallback(MultiNodeClusterSpec.ClusterConfig());
        }
    }

    public class ClusterSingletonManagerChaosNode1 : ClusterSingletonManagerChaosConfig { }
    public class ClusterSingletonManagerChaosNode2 : ClusterSingletonManagerChaosConfig { }
    public class ClusterSingletonManagerChaosNode3 : ClusterSingletonManagerChaosConfig { }
    public class ClusterSingletonManagerChaosNode4 : ClusterSingletonManagerChaosConfig { }
    public class ClusterSingletonManagerChaosNode5 : ClusterSingletonManagerChaosConfig { }
    public class ClusterSingletonManagerChaosNode6 : ClusterSingletonManagerChaosConfig { }
    public class ClusterSingletonManagerChaosNode7 : ClusterSingletonManagerChaosConfig { }

    public abstract class ClusterSingletonManagerChaosSpec : MultiNodeClusterSpec
    {
        protected ClusterSingletonManagerChaosSpec() : base(new ClusterSingletonManagerChaosConfig())
        {
        }

        protected override int InitialParticipantsValueFactory { get { return Roles.Count; } }

        //[MultiNodeFact(Skip = "TODO")]
        public void ClusterSingletonManager_in_chaotic_cluster_should_startup_6_node_cluster()
        {
            Within(TimeSpan.FromMinutes(1), () =>
            {
                var memberProbe = CreateTestProbe();
                Cluster.Get(Sys).Subscribe(memberProbe.Ref, new[] { typeof(ClusterEvent.MemberUp) });
                memberProbe.ExpectMsg<ClusterEvent.CurrentClusterState>();

                var first = GetRole("first");
                var second = GetRole("second");
                var third = GetRole("third");
                var fourth = GetRole("fourth");
                var fifth = GetRole("fifth");
                var sixth = GetRole("sixth");

                AwaitClusterUp(first, second, third, fourth, fifth, sixth);

                Join(first, first);
                AwaitMemberUp(memberProbe, first);
                RunOn(() =>
                {
                    ExpectMsg<EchoStarted>();
                }, first);
                EnterBarrier("first-started");

                Join(second, first);
                AwaitMemberUp(memberProbe, second, first);

                Join(third, first);
                AwaitMemberUp(memberProbe, third, second, first);

                Join(fourth, first);
                AwaitMemberUp(memberProbe, fourth, third, second, first);

                Join(fifth, first);
                AwaitMemberUp(memberProbe, fifth, fourth, third, second, first);

                Join(sixth, first);
                AwaitMemberUp(memberProbe, sixth, fifth, fourth, third, second, first);

                RunOn(() =>
                {
                    Echo(first).Tell("hello");
                    Assert.Equal(ExpectMsg<IActorRef>(TimeSpan.FromSeconds(3)).Path.Address, Node(first).Address);
                }, GetRole("controller"));

                EnterBarrier("first-verified");
            });
        }

        [MultiNodeFact]
        public void ClusterSingletonManager_in_chaotic_cluster_should_take_over_when_tree_oldest_nodes_crash_in_6_nodes_cluster()
        {
            ClusterSingletonManager_in_chaotic_cluster_should_startup_6_node_cluster();

            Within(TimeSpan.FromSeconds(90), () =>
            {
                // mute logging of deadLetters during shutdown of systems
                if (!Log.IsDebugEnabled)
                    Sys.EventStream.Publish(new Mute(new WarningFilter()));
                EnterBarrier("logs-muted");

                var first = GetRole("first");
                var second = GetRole("second");
                var third = GetRole("third");
                var fourth = GetRole("fourth");

                Crash(first, second, third);
                EnterBarrier("after-crash");
                RunOn(() =>
                {
                    ExpectMsg<EchoStarted>();
                }, fourth);
                EnterBarrier("fourth-active");

                RunOn(() =>
                {
                    Echo(fourth).Tell("hello");
                    var address = ExpectMsg<IActorRef>(TimeSpan.FromSeconds(3)).Path.Address;
                    Assert.Equal(address, Node(fourth).Address);
                }, GetRole("controller"));
                EnterBarrier("fourth-verified");
            });
        }

        private void Join(RoleName from, RoleName to)
        {
            RunOn(() =>
            {
                Cluster.Get(Sys).Join(Node(to).Address);
                CreateSingleton();
            }, from);
        }

        private RoleName GetRole(string name)
        {
            return Roles.First(roleName => roleName.Name == name);
        }

        private IActorRef CreateSingleton()
        {
            /**
            system.actorOf(ClusterSingletonManager.props(
              singletonProps = Props(classOf[Echo], testActor),
              terminationMessage = PoisonPill,
              settings = ClusterSingletonManagerSettings(system)),
              name = "echo")
            */
            return Sys.ActorOf(ClusterSingletonManager.Props(
                singletonProps: Props.Create(() => new Echo(TestActor)),
                terminationMessage: PoisonPill.Instance,
                settings: ClusterSingletonManagerSettings.Create(Sys)),
                name: "echo");
        }

        private void Crash(params RoleName[] roles)
        {
            RunOn(() =>
            {
                foreach (var roleName in roles)
                {
                    Log.Info("Shutdown [{0}]", roleName);
                    TestConductor.Exit(roleName, 0).Wait(TimeSpan.FromSeconds(10));
                }
            }, GetRole("controller") /*controller*/);
        }

        private ActorSelection Echo(RoleName oldest)
        {
            return Sys.ActorSelection(Node(oldest) / "user" / "echo" / "singleton");
        }

        private void AwaitMemberUp(TestProbe memberProbe, params RoleName[] nodes)
        {
            RunOn(() =>
            {
                var address = memberProbe.ExpectMsg<ClusterEvent.MemberUp>(TimeSpan.FromSeconds(15)).Member.Address;
                var headAddress = Node(nodes[0]).Address;
                Assert.Equal(address, headAddress);
            }, nodes.Where(n => n != nodes[0]).ToArray());

            RunOn(() =>
            {
                var addresses = new HashSet<Address>(memberProbe.ReceiveN(nodes.Length, TimeSpan.FromSeconds(15))
                    .Where(x => x is ClusterEvent.MemberUp)
                    .Select(x => (x as ClusterEvent.MemberUp).Member.Address));

                var rolenodes = nodes.Select(n => Node(n).Address).ToList();
                Assert.Equal(addresses, rolenodes);

                EnterBarrier(nodes[0].Name + "-up");

            }, nodes[0]);
        }
    }
}