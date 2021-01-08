//-----------------------------------------------------------------------
// <copyright file="DeployerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Routing;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class DeployerSpec : AkkaSpec
    {
        public DeployerSpec():base(GetConfig())
        {        
        }

        private static string GetConfig()
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
          router = random-pool
        }
        ""/*/some"" {
          router = round-robin-pool
        }
        ""/*/so.me"" {
          router = round-robin-pool
        }
        ""/double/**"" {
          router = random-pool
        }
        ""/double/more/**"" {
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

        #region Tests

        [Fact]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_all_default_values()
        {
            var service = @"/service1";
            var deployment = ((ActorSystemImpl) Sys).Provider.Deployer.Lookup(service.Split('/').Drop(1));

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
            var deployment = ((ActorSystemImpl)Sys).Provider.Deployer.Lookup(service.Split('/').Drop(1));
            Assert.Null(deployment);
        }

        [Fact]
        public void Deployer_must_be_able_to_parse_akka_actor_deployment_with_dispatcher_config()
        {
            var service = @"/service3";
            var deployment = ((ActorSystemImpl)Sys).Provider.Deployer.Lookup(service.Split('/').Drop(1));

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
            var deployment = ((ActorSystemImpl)Sys).Provider.Deployer.Lookup(service.Split('/').Drop(1));

            Assert.Equal(service, deployment.Path);
            Assert.IsType<NoRouter>(deployment.RouterConfig);
            Assert.Equal(Deploy.NoScopeGiven, deployment.Scope);
            Assert.Equal("my-mailbox", deployment.Mailbox);
            Assert.Equal(Deploy.NoDispatcherGiven, deployment.Dispatcher);
        }


        [Fact(DisplayName=@"If a fallaback config is declared with a deployment an actor should be able to be created for the main configuration and the fallback configuration")]
        public void ActorSystem_fallback_deployment_is_not_null_when_config_has_value()
        {
            var config1 = ConfigurationFactory.ParseString(@"
            akka.actor.deployment {
                /worker1 {
                    router = round-robin-group
                    routees.paths = [""/user/testroutes/1""]
                }
            }");
            var config2 = ConfigurationFactory.ParseString(@"
            akka.actor.deployment {
                /worker2 {
                    router = round-robin-group
                    routees.paths = [""/user/testroutes/2""]
                }
            }");
            var configWithFallback = config1.WithFallback(config2);

            //act
            var actorSystem = ActorSystem.Create("actors", configWithFallback);
            var worker1 = actorSystem.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "worker1");
            var worker2 = actorSystem.ActorOf(Props.Empty.WithRouter(FromConfig.Instance), "worker2");

            //assert
            Assert.NotNull(worker1.Path);
            Assert.NotNull(worker2.Path);
            Assert.NotEqual(worker1.Path, worker2.Path);
        }

        [Fact]
        public void Deployer_should_be_able_to_use_wildcards()
        {
            AssertRouting("/some/wildcardmatch", new RandomPool(1), "/some/*");
            AssertRouting("/somewildcardmatch/some", new RoundRobinPool(1), "/*/some");
        }

        [Fact]
        public void Deployer_should_be_able_to_use_double_wildcards()
        {
            AssertRouting("/double/wildcardmatch", new RandomPool(1), "/double/**");
            AssertRouting("/double/wildcardmatch/anothermatch", new RandomPool(1), "/double/**");
            AssertRouting("/double/more/anothermatch", new RoundRobinPool(1), "/double/more/**");
            AssertNoRouting("/double");
        }

        #endregion

        private void AssertNoRouting(string service)
        {
            var deployment = ((ActorSystemImpl)Sys).Provider.Deployer.Lookup(service.Split('/').Drop(1));
            Assert.Null(deployment);
        }

        private void AssertRouting(string service, RouterConfig expected, string expectPath)
        {
            var deployment = ((ActorSystemImpl)Sys).Provider.Deployer.Lookup(service.Split('/').Drop(1));
            Assert.Equal(expectPath, deployment.Path);
            Assert.Equal(expected.GetType(), deployment.RouterConfig.GetType());
            Assert.Equal(Deploy.NoScopeGiven, deployment.Scope);
            if(expected is Pool pool)
            {
                Assert.Equal(pool.Resizer, ((Pool)deployment.RouterConfig).Resizer);
            }
        }
    }
}

