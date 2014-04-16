using System.Linq;
using Akka.Actor;
using Akka.Routing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
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

        [TestMethod]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_all_default_values()
        {
            var service = @"/service1";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.AreEqual(service, deployment.Path);
            Assert.IsInstanceOfType(deployment.RouterConfig, typeof(NoRouter));
            Assert.AreEqual(Deploy.NoScopeGiven, deployment.Scope);
            Assert.AreEqual(Deploy.NoMailboxGiven, deployment.Mailbox);
            Assert.AreEqual(Deploy.NoDispatcherGiven, deployment.Dispatcher);
        }

        [TestMethod]
        public void Deployer_must_use_Null_for_undefined_service()
        {
            var service = @"/undefined";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));
            Assert.IsNull(deployment);
        }

        [TestMethod]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_dispatcher_config()
        {
            var service = @"/service3";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.AreEqual(service, deployment.Path);
            Assert.IsInstanceOfType(deployment.RouterConfig, typeof(NoRouter));
            Assert.AreEqual(Deploy.NoScopeGiven, deployment.Scope);
            Assert.AreEqual(Deploy.NoMailboxGiven, deployment.Mailbox);
            Assert.AreEqual("my-dispatcher", deployment.Dispatcher);
        }

        [TestMethod]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_mailbox_config()
        {
            var service = @"/service4";
            var deployment = sys.Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.AreEqual(service, deployment.Path);
            Assert.IsInstanceOfType(deployment.RouterConfig, typeof(NoRouter));
            Assert.AreEqual(Deploy.NoScopeGiven, deployment.Scope);
            Assert.AreEqual("my-mailbox", deployment.Mailbox);
            Assert.AreEqual(Deploy.NoDispatcherGiven, deployment.Dispatcher);
        }


        #endregion
    }
}
