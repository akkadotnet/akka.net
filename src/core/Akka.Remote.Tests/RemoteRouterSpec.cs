//-----------------------------------------------------------------------
// <copyright file="RemoteRouterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Tests for <see cref="RemoteRouterConfig"/>
    /// </summary>
    public class RemoteRouterSpec : AkkaSpec
    {
        #region Echo actor

        private class Echo : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                Sender.Tell(message);
            }
        }

        private class Parent : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                var tuple = message as Tuple<Props, string>;
                if (tuple != null)
                {
                    Sender.Tell(Context.ActorOf(tuple.Item1, tuple.Item2));
                }
            }
        }

        private Props EchoActorProps
        {
            get { return Props.Create<Echo>(); }
        }

        #endregion

        private int port;
        private string sysName;
        private Config conf;
        private ActorSystem masterSystem;
        private Address intendedRemoteAddress;

        public RemoteRouterSpec()
            : base(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.helios.tcp {
                hostname = 127.0.0.1
                port = 0
            }
            akka.actor.deployment {
              /remote-override {
                router = round-robin-pool
                nr-of-instances = 4
              }
              /round {
                router = round-robin-pool
                nr-of-instances = 5
              }
              /sys-parent/round {
                router = round-robin-pool
                nr-of-instances = 6
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
          target.nodes = [""akka.tcp://${sysName}@127.0.0.1:${port}""]
        }
        /elastic-blub {
          router = round-robin-pool
          resizer {
            enabled = on
            lower-bound = 2
            upper-bound = 3
          }
          target.nodes = [""akka.tcp://${sysName}@127.0.0.1:${port}""]
        }
        /remote-blub {
          remote = ""akka.tcp://${sysName}@127.0.0.1:${port}""
          router = round-robin-pool
          nr-of-instances = 2
        }
        /local-blub {
          remote = ""akka://${masterSysName}""
          router = round-robin-pool
          nr-of-instances = 2
          target.nodes = [""akka.tcp://${sysName}@127.0.0.1:${port}""]
        }
        /local-blub2 {
          router = round-robin-pool
          nr-of-instances = 4
          target.nodes = [""akka.tcp://${sysName}@127.0.0.1:${port}""]
        }
      }
    }
".Replace("${masterSysName}", "Master" + sysName).Replace("${sysName}", sysName).Replace("${port}", port.ToString())).WithFallback(Sys.Settings.Config);

            masterSystem = ActorSystem.Create("Master" + sysName, conf);

            intendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@127.0.0.1:${port}"
                .Replace("${sysName}", sysName)
                .Replace("${port}", port.ToString()));
        }

        protected override void AfterTermination()
        {
            Shutdown(masterSystem);
        }

        private IEnumerable<ActorPath> CollectRouteePaths(TestProbe probe, IActorRef router, int n)
        {
            List<ActorPath> list = new List<ActorPath>();

            for (var i = 1; i <= n; i++)
            {
                string msg = i.ToString();
                router.Tell(msg, probe.Ref);
                probe.ExpectMsg(msg);
                list.Add(probe.LastSender.Path);
            }

            return list;
        }

        [Fact]
        public void RemoteRouter_must_deploy_its_children_on_remote_host_driven_by_configuration()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(new RoundRobinPool(2).Props(EchoActorProps), "blub");
            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(2);
            childred.Select(x => x.Parent).Distinct().Should().HaveCount(1);
            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_its_children_on_remote_host_driven_by_programmatic_definition()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(new RemoteRouterConfig(
                new RoundRobinPool(2),
                new[] { new Address("akka.tcp", sysName, "127.0.0.1", port) })
                .Props(EchoActorProps), "blub2");
            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(2);
            childred.Select(x => x.Parent).Distinct().Should().HaveCount(1);
            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_dynamic_resizable_number_of_children_on_remote_host_driven_by_configuration()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(FromConfig.Instance.Props(EchoActorProps), "elastic-blub");
            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(2);
            childred.Select(x => x.Parent).Distinct().Should().HaveCount(1);
            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_remote_routers_based_on_configuration()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(FromConfig.Instance.Props(EchoActorProps), "remote-blub");
            router.Path.Address.Should().Be(intendedRemoteAddress);

            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(2);

            var parents = childred.Select(x => x.Parent).Distinct().ToList();
            parents.Should().HaveCount(1);
            parents.Head().Should().Be(router.Path);

            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_deploy_remote_routers_based_on_explicit_deployment()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(new RoundRobinPool(2)
                .Props(EchoActorProps)
                .WithDeploy(new Deploy(new RemoteScope(intendedRemoteAddress))), "remote-blub2");

            router.Path.Address.Should().Be(intendedRemoteAddress);

            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(2);

            var parents = childred.Select(x => x.Parent).Distinct().ToList();
            parents.Should().HaveCount(1);
            parents.Head().Should().Be(router.Path);

            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_let_remote_deployment_be_overridden_by_local_configuration()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(
                new RoundRobinPool(2)
                .Props(EchoActorProps)
                .WithDeploy(new Deploy(new RemoteScope(intendedRemoteAddress))), "local-blub");
            router.Path.Address.ToString().Should().Be(string.Format("akka://{0}", masterSystem.Name));

            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(2);

            var parents = childred.Select(x => x.Parent).Distinct().ToList();
            parents.Should().HaveCount(1);
            parents.Head().Address.Should().Be(new Address("akka.tcp", sysName, "127.0.0.1", port));

            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_let_remote_deployment_router_be_overridden_by_local_configuration()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(
                new RoundRobinPool(2)
                .Props(EchoActorProps)
                .WithDeploy(new Deploy(new RemoteScope(intendedRemoteAddress))), "local-blub2");

            router.Path.Address.Should().Be(intendedRemoteAddress);

            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(4);

            var parents = childred.Select(x => x.Parent).Distinct().ToList();
            parents.Should().HaveCount(1);
            parents.Head().Address.Should().Be(new Address("akka.tcp", sysName, "127.0.0.1", port));

            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact]
        public void RemoteRouter_must_let_remote_deployment_be_overridden_by_remote_configuration()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(
                new RoundRobinPool(2)
                .Props(EchoActorProps)
                .WithDeploy(new Deploy(new RemoteScope(intendedRemoteAddress))), "remote-override");

            router.Path.Address.Should().Be(intendedRemoteAddress);

            var replies = CollectRouteePaths(probe, router, 5);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(4);

            var parents = childred.Select(x => x.Parent).Distinct().ToList();
            parents.Should().HaveCount(1);
            parents.Head().Address.Should().Be(router.Path.Address);

            childred.ForEach(x => x.Address.Should().Be(intendedRemoteAddress));
            masterSystem.Stop(router);
        }

        [Fact(Skip = "Serialization of custom deciders is currently not supported")]
        public void RemoteRouter_must_set_supplied_SupervisorStrategy()
        {
            var probe = CreateTestProbe(masterSystem);
            var escalator = new OneForOneStrategy(ex =>
            {
                probe.Ref.Tell(ex);
                return Directive.Escalate;
            });

            var router = masterSystem.ActorOf(new RemoteRouterConfig(
                new RoundRobinPool(1, null, escalator, Dispatchers.DefaultDispatcherId),
                new[] { new Address("akka.tcp", sysName, "127.0.0.1", port) }).Props(Props.Empty), "blub3");

            router.Tell(new GetRoutees(), probe.Ref);

            // Need to be able to bind EventFilter to additional actor system (masterActorSystem in this case) before this code works
            // EventFilter.Exception<ActorKilledException>().ExpectOne(() => 
            probe.ExpectMsg<Routees>().Members.Head().Send(Kill.Instance, TestActor);
            //);
            probe.ExpectMsg<ActorKilledException>();
        }

        [Fact(Skip = "Remote actor's DCN is currently not supported")]
        public void RemoteRouter_must_load_settings_from_config_for_local_router()
        {
            var probe = CreateTestProbe(masterSystem);
            var router = masterSystem.ActorOf(FromConfig.Instance.Props(EchoActorProps), "round");
            var replies = CollectRouteePaths(probe, router, 10);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(5);
            masterSystem.Stop(router);
        }

        [Fact(Skip = "Remote actor's DCN is currently not supported")]
        public void RemoteRouter_must_load_settings_from_config_for_local_child_router_of_system_actor()
        {
            // we don't really support deployment configuration of system actors, but
            // it's used for the pool of the SimpleDnsManager "/IO-DNS/inet-address"
            var probe = CreateTestProbe(masterSystem);
            var parent = ((ExtendedActorSystem)masterSystem).SystemActorOf(FromConfig.Instance.Props(Props.Create<Parent>()), "sys-parent");
            parent.Tell(Tuple.Create(FromConfig.Instance.Props(EchoActorProps), "round"), probe);
            var router = probe.ExpectMsg<IActorRef>();
            var replies = CollectRouteePaths(probe, router, 10);
            var childred = new HashSet<ActorPath>(replies);
            childred.Should().HaveCount(6);
            masterSystem.Stop(router);
        }
    }
}

