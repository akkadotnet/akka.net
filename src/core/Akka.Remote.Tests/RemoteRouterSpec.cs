using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Tests for <see cref="RemoteRouterConfig"/>
    /// </summary>
    public class RemoteRouterSpec : AkkaSpec
    {
        #region Echo actor

        class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(Self);
            }
        }

        #endregion

        private int port;
        private string sysName;
        private Config conf;
        private ActorSystem masterActorSystem;
        private Address intendedRemoteAddress;

        public RemoteRouterSpec()
            : base(@"
            akka.test.single-expect-default = 6s #to help overcome issues with GC pauses on build server
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.helios.tcp {
                hostname = localhost
                port = 0
            }
            akka.actor.deployment {
                /remote-override {
                    router = round-robin-pool
                    nr-of-instances = 4
                }
            }
        ")
        {
            // ReSharper disable once PossibleInvalidOperationException
            port = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress.Port.Value;
            sysName = Sys.Name;
            conf = ConfigurationFactory.ParseString(@"
                akka {
      actor.deployment {
        /blub {
          router = round-robin-pool
          nr-of-instances = 2
          target.nodes = [""akka.tcp://${sysName}@localhost:${port}""]
        }
        /elastic-blub {
          router = round-robin-pool
          resizer {
            enabled = on
            lower-bound = 2
            upper-bound = 3
          }
          target.nodes = [""akka.tcp://${sysName}@localhost:${port}""]
        }
        /remote-blub {
          remote = ""akka.tcp://${sysName}@localhost:${port}""
          router = round-robin-pool
          nr-of-instances = 2
        }
        /local-blub {
          remote = ""akka://${masterSysName}""
          router = round-robin-pool
          nr-of-instances = 2
          target.nodes = [""akka.tcp://${sysName}@localhost:${port}""]
        }
        /local-blub2 {
          router = round-robin-pool
          nr-of-instances = 4
          target.nodes = [""akka.tcp://${sysName}@localhost:${port}""]
        }
      }
    }
".Replace("${masterSysName}", "Master" + sysName).Replace("${sysName}", sysName).Replace("${port}", port.ToString())).WithFallback(Sys.Settings.Config);
            masterActorSystem = ActorSystem.Create("Master" + sysName, conf);

            intendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@localhost:${port}"
                .Replace("${sysName}", sysName)
                .Replace("${port}", port.ToString()));
        }

        protected override void AfterTermination()
        {
            Shutdown(masterActorSystem);
        }

        [Fact]
        public void RemoteRouter_must_deploy_its_children_on_remote_host_driven_by_configuration()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()), "blub");
            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(2, replies.Count);
            Assert.Equal(1, replies.Select(x => x.Parent).Distinct().Count());
            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_its_children_on_remote_host_driven_by_programatic_definition()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(new RemoteRouterConfig(new RoundRobinPool(2), new[] { new Address("akka.tcp", sysName, "localhost", port) })
                .Props(Props.Create<Echo>()), "blub2");
            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(2, replies.Count);
            Assert.Equal(1, replies.Select(x => x.Parent).Distinct().Count());
            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_dynamic_resizable_number_of_children_on_remote_host_driven_by_configuration
            ()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "elastic-blub");
            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5000; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.True(replies.Count >= 2);
            Assert.Equal(1, replies.Select(x => x.Parent).Distinct().Count());
            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_remote_routers_based_on_configuration()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "remote-blub");
            router.Path.Address.ShouldBe(intendedRemoteAddress);

            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(2, replies.Count);
            var parents = replies.Select(x => x.Parent).Distinct().ToList();
            parents.Head().ShouldBe(router.Path);

            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_remote_routers_based_on_explicit_deployment()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>())
                .WithDeploy(
                new Deploy(new RemoteScope(intendedRemoteAddress.Copy()))), "remote-blub2");

            router.Path.Address.ShouldBe(intendedRemoteAddress);

            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(2, replies.Count);
            var parents = replies.Select(x => x.Parent).Distinct().ToList();
            parents.Head().ShouldBe(router.Path);

            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_let_remote_deployment_be_overriden_by_local_configuration()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>())
                .WithDeploy(
                new Deploy(new RemoteScope(intendedRemoteAddress.Copy()))), "local-blub");
            router.Path.Address.ToString().ShouldBe(string.Format("akka://{0}", masterActorSystem.Name));

            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(2, replies.Count);
            var parents = replies.Select(x => x.Parent).Distinct().ToList();
            parents.Head().Address.ShouldBe(new Address("akka.tcp", sysName, "localhost", port));

            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_let_remote_deployment_router_be_overriden_by_local_configuration()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>())
                .WithDeploy(
                new Deploy(new RemoteScope(intendedRemoteAddress.Copy()))), "local-blub2");

            // This line was subject to a bug in the original Akka - this router should be locally-deployed.
            router.Path.Address.ToString().ShouldBe(string.Format("akka://{0}", masterActorSystem.Name));

            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(4, replies.Count);
            var parents = replies.Select(x => x.Parent).Distinct().ToList();
            parents.Head().Address.ShouldBe(new Address("akka.tcp", sysName, "localhost", port));

            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_let_remote_deployment_be_overriden_by_remote_configuration()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>())
                .WithDeploy(
                new Deploy(new RemoteScope(intendedRemoteAddress.Copy()))), "remote-override");

            router.Path.Address.ShouldBe(intendedRemoteAddress);

            var replies = new HashSet<ActorPath>();
            for (var i = 0; i < 5; i++)
            {
                router.Tell("", probe.Ref);
                var expected = probe.ExpectMsg<ActorRef>(GetTimeoutOrDefault(null));
                replies.Add(expected.Path);
            }

            Assert.Equal(4, replies.Count);
            var parents = replies.Select(x => x.Parent).Distinct().ToList();
            parents.Count.ShouldBe(1);
            parents.Head().Address.ShouldBe(new Address("akka.tcp", sysName, "localhost", port));

            Assert.True(replies.All(x => x.Address.Equals(intendedRemoteAddress)));
            masterActorSystem.Stop(router);
        }

        [Fact(Skip = "Serialization of custom deciders is currently not supported")]
        public void RemoteRouter_must_set_supplied_SupervisorStrategy()
        {
            var probe = CreateTestProbe(masterActorSystem);
            var escalator = new OneForOneStrategy(ex =>
            {
                probe.Ref.Tell(ex);
                return Directive.Escalate;
            });

            var router =
                masterActorSystem.ActorOf(
                    new RemoteRouterConfig(new RoundRobinPool(1, null, escalator, null),
                        new[] { new Address("akka.tcp", sysName, "localhost", port) })
                        .Props(Props.Empty), "blub3");

            router.Tell(new GetRoutees(), probe.Ref);
            // Need to be able to bind EventFilter to additional actor system (masterActorSystem in this case) before this code works
            // EventFilter.Exception<ActorKilledException>().ExpectOne(() => 
            probe.ExpectMsg<Routees>().Members.Head().Send(Kill.Instance, TestActor);
            //);
            probe.ExpectMsg<ActorKilledException>();
        }
    }
}
