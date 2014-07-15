using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class DeployerSpec : AkkaSpec
    {
        #region Setup / Teardown

        protected override string GetConfig()
        {
            return @"
                akka.actor.deployment {
        /service1 {
        }
        /service-direct {
          router = from-code
        }
        /service-direct2 {
          router = from-code
          # nr-of-instances ignored when router = from-code
          nr-of-instances = 2
        }
        /service3 {
          dispatcher = my-dispatcher
        }
        /service4 {
          mailbox = my-mailbox
        }
        /service-round-robin {
          router = round-robin-pool
        }
        /service-random {
          router = round-robin-pool
        }
        /service-scatter-gather {
          router = round-robin-pool
          within = 2 seconds
        }
        /service-consistent-hashing {
          router = consistent-hashing-pool
        }
        /service-resizer {
          router = round-robin-pool
          resizer {
            lower-bound = 1
            upper-bound = 10
          }
        }
        /some/random-service {
          router = round-robin-pool
        }
        ""/some/*"" {
          router = round-robin-pool
        }
        ""/*/some"" {
          router = round-robin-pool
        }
      }
            ";
        }

        class RecipeActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {

            }
        }
        #endregion

        #region Tests

        [Fact]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_all_default_values()
        {
            var service = @"/service1";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.Equal(service, deployment.Path);
            Assert.IsType<NoRouter>(deployment.RouterConfig);
            Assert.Equal(Deploy.NoScopeGiven, deployment.Scope);
            Assert.Equal(Deploy.NoMailboxGiven, deployment.Mailbox);
            Assert.Equal(Deploy.NoDispatcherGiven, deployment.Dispatcher);
        }

        [Fact]
        public void Deployer_must_use_Null_for_undefined_service()
        {
            var service = @"/undefined";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));
            Assert.Null(deployment);
        }

        [Fact]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_dispatcher_config()
        {
            var service = @"/service3";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.Equal(service, deployment.Path);
            Assert.IsType<NoRouter>(deployment.RouterConfig);
            Assert.Equal(Deploy.NoScopeGiven, deployment.Scope);
            Assert.Equal(Deploy.NoMailboxGiven, deployment.Mailbox);
            Assert.Equal("my-dispatcher", deployment.Dispatcher);
        }

        [Fact]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_mailbox_config()
        {
            var service = @"/service4";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.Equal(service, deployment.Path);
            Assert.IsType<NoRouter>(deployment.RouterConfig);
            Assert.Equal(Deploy.NoScopeGiven, deployment.Scope);
            Assert.Equal("my-mailbox", deployment.Mailbox);
            Assert.Equal(Deploy.NoDispatcherGiven, deployment.Dispatcher);
        }


        #endregion
    }
}
