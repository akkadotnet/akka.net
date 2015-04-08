using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Cluster.Routing;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ClusterDeployerSpec : AkkaSpec
    {
        public static readonly Config deployConf = ConfigurationFactory.ParseString(@"
        akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
      akka.actor.deployment {
        /user/service1 {
          router = round-robin-pool
          nr-of-instances = 20
          cluster.enabled = on
          cluster.max-nr-of-instances-per-node = 3
          cluster.allow-local-routees = off
        }
        /user/service2 {
          dispatcher = mydispatcher
          mailbox = mymailbox
          router = round-robin-group
          nr-of-instances = 20
          routees.paths = [""/user/myservice""]
          cluster.enabled = on
          cluster.allow-local-routees = off
          cluster.use-role = backend
        }
      }
      akka.remote.helios.tcp.port = 0

");

        public ClusterDeployerSpec() : base(deployConf) { }

        [Fact]
        public void RemoteDeployer_must_be_able_to_parse_akka_actor_deployment_with_specified_cluster_pool()
        {
            var service = "/user/service1";
            var deployment = Sys.AsInstanceOf<ActorSystemImpl>().Provider.Deployer.Lookup(service.Split('/').Drop(1));
            deployment.ShouldNotBe(null);

            deployment.Path.ShouldBe(service);
            deployment.RouterConfig.GetType().ShouldBe(typeof(ClusterRouterPool));
            deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>().Local.GetType().ShouldBe(typeof(RoundRobinPool));
            deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>().Local.AsInstanceOf<RoundRobinPool>().NrOfInstances.ShouldBe(20);
            deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>().Settings.TotalInstances.ShouldBe(20);
            deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>().Settings.AllowLocalRoutees.ShouldBe(false);
            deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>().Settings.UseRole.ShouldBe(string.Empty);
            deployment.RouterConfig.AsInstanceOf<ClusterRouterPool>().Settings.AsInstanceOf<ClusterRouterPoolSettings>().MaxInstancesPerNode.ShouldBe(3);
            deployment.Scope.ShouldBe(ClusterScope.Instance);
            deployment.Mailbox.ShouldBe(Deploy.NoMailboxGiven);
            deployment.Dispatcher.ShouldBe(Deploy.NoDispatcherGiven);
        }

        [Fact]
        public void RemoteDeployer_must_be_able_to_parse_akka_actor_deployment_with_specified_cluster_group()
        {
            var service = "/user/service2";
            var deployment = Sys.AsInstanceOf<ActorSystemImpl>().Provider.Deployer.Lookup(service.Split('/').Drop(1));
            deployment.ShouldNotBe(null);

            deployment.Path.ShouldBe(service);
            deployment.RouterConfig.GetType().ShouldBe(typeof(ClusterRouterGroup));
            deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>().Local.GetType().ShouldBe(typeof(RoundRobinGroup));
            deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>().Local.AsInstanceOf<RoundRobinGroup>().Paths.ShouldBe(new[]{ "/user/myservice" });
            deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>().Settings.TotalInstances.ShouldBe(20);
            deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>().Settings.AllowLocalRoutees.ShouldBe(false);
            deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>().Settings.UseRole.ShouldBe("backend");
            deployment.RouterConfig.AsInstanceOf<ClusterRouterGroup>().Settings.AsInstanceOf<ClusterRouterGroupSettings>().RouteesPaths.ShouldBe(new[] { "/user/myservice" });
            deployment.Scope.ShouldBe(ClusterScope.Instance);
            deployment.Mailbox.ShouldBe("mymailbox");
            deployment.Dispatcher.ShouldBe("mydispatcher");
        }

        //todo: implement "have correct router mappings" test for adaptive load-balancing routers (not yet implemented)
    }
}
