//-----------------------------------------------------------------------
// <copyright file="ClusterDeployerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using FluentAssertions;

namespace Akka.Cluster.Tests
{
    public class ClusterDeployerSpec : AkkaSpec
    {
        public static readonly Config deployerConf = ConfigurationFactory.ParseString(@"
          akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
          akka.actor.deployment {
            /user/service1 {
              router = round-robin-pool
              cluster.enabled = on
              cluster.max-nr-of-instances-per-node = 3
              cluster.max-total-nr-of-instances = 20
              cluster.allow-local-routees = off
            }
            /user/service2 {
              dispatcher = mydispatcher
              mailbox = mymailbox
              router = round-robin-group
              nr-of-instances = 20
              routees.paths = [""/user/myservice""]
              cluster.enabled = on
              cluster.max-total-nr-of-instances = 20
              cluster.allow-local-routees = off
            }
          }
          akka.remote.helios.tcp.port = 0");

        public ClusterDeployerSpec() : base(deployerConf) { }

        [Fact]
        public void RemoteDeployer_must_be_able_to_parse_akka_actor_deployment_with_specified_cluster_pool()
        {
            var service = "/user/service1";
            var deployment = Sys.AsInstanceOf<ActorSystemImpl>().Provider.Deployer.Lookup(service.Split('/').Drop(1));
            deployment.Should().NotBeNull();

            deployment.Path.Should().Be(service);
            deployment.RouterConfig.Should().BeOfType<ClusterRouterPool>();

            var routerConfig = deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>();
            routerConfig.Local.Should().BeOfType<RoundRobinPool>();
            routerConfig.Local.AsInstanceOf<RoundRobinPool>().NrOfInstances.Should().Be(20);
            routerConfig.Settings.TotalInstances.Should().Be(20);
            routerConfig.Settings.AllowLocalRoutees.Should().BeFalse();
            routerConfig.Settings.UseRole.Should().BeNull();
            routerConfig.Settings.AsInstanceOf<ClusterRouterPoolSettings>().MaxInstancesPerNode.Should().Be(3);
            deployment.Scope.Should().Be(ClusterScope.Instance);
            deployment.Mailbox.Should().Be(Deploy.NoMailboxGiven);
            deployment.Dispatcher.Should().Be(Deploy.NoDispatcherGiven);
        }

        [Fact]
        public void RemoteDeployer_must_be_able_to_parse_akka_actor_deployment_with_specified_cluster_group()
        {
            var service = "/user/service2";
            var deployment = Sys.AsInstanceOf<ActorSystemImpl>().Provider.Deployer.Lookup(service.Split('/').Drop(1));
            deployment.Should().NotBeNull();

            deployment.Path.Should().Be(service);
            deployment.RouterConfig.Should().BeOfType<ClusterRouterGroup>();

            var routerConfig = deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>();
            routerConfig.Local.Should().BeOfType<RoundRobinGroup>();
            routerConfig.Local.AsInstanceOf<RoundRobinGroup>().Paths.Should().BeEquivalentTo("/user/myservice");
            routerConfig.Settings.TotalInstances.Should().Be(20);
            routerConfig.Settings.AllowLocalRoutees.Should().BeFalse();
            routerConfig.Settings.UseRole.Should().BeNull();
            routerConfig.Settings.AsInstanceOf<ClusterRouterGroupSettings>().RouteesPaths.Should().BeEquivalentTo("/user/myservice");
            deployment.Scope.Should().Be(ClusterScope.Instance);
            deployment.Mailbox.Should().Be("mymailbox");
            deployment.Dispatcher.Should().Be("mydispatcher");
        }
    }
}

