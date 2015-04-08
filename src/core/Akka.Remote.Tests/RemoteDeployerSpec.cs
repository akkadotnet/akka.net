using Akka.Actor;
using Akka.Actor.Internals;
using Akka.Configuration;
using Akka.Remote.Routing;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests
{
    /// <summary>
    /// Tests to see that the <see cref="RemoteDeployer"/> parses
    /// <see cref="RemoteRouterConfig"/> sections correctly.
    /// </summary>
    public class RemoteDeployerSpec : AkkaSpec
    {
        public RemoteDeployerSpec()
            : base(@"
            akka.actor.provider = ""Akka.Remote.RemoteActorRefProvider, Akka.Remote""
            akka.remote.helios.tcp.port = 0
            akka.actor.deployment {
                /service2 {
                  router = round-robin-pool
                  nr-of-instances = 3
                  remote = ""akka.tcp://sys@wallace:2552""
                  dispatcher = mydispatcher
                }
            }        
        ")
        {

        }

        [Fact]
        public void RemoteDeployer_must_be_able_to_parse_akka_actor_deployment_with_specified_remote_nodes()
        {
            var service = "/service2";
            var deployment = Sys.AsInstanceOf<ActorSystemImpl>().Provider.Deployer.Lookup(service.Split('/').Drop(1));

            deployment.Path.ShouldBe(service);
            deployment.RouterConfig.AsInstanceOf<RoundRobinPool>().NrOfInstances.ShouldBe(3);
            deployment.Scope.AsInstanceOf<RemoteScope>().Address.ShouldBe(new Address("akka.tcp", "sys", "wallace", 2552));
            deployment.Dispatcher.ShouldBe("mydispatcher");
        }

        [Fact]
        public void RemoteDeployer_must_reject_remote_deployment_when_the_source_requires_LocalScope()
        {
            Intercept<ConfigurationException>(() => Sys.ActorOf(Props.Empty.WithDeploy(Deploy.Local), "service2"));
        }
    }
}
