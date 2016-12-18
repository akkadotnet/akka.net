//-----------------------------------------------------------------------
// <copyright file="ActorSystemTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Configuration;
using Akka.Routing;
using Xunit;

namespace Akka.Tests
{
    
    public class ActorSystemTests
    {
        public class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        [Fact]
        public void ActorSystem_ActorOf_adds_a_child_to_Guardian()
        {
            //arrange
            var system = new ActorSystemImpl("test");
            system.Start(); //When we create a system manually we have to start it ourselves

            //act
            var child = system.ActorOf<TestActor>("test");

            //assert
            var children = system.Provider.Guardian.Children;
            Assert.True(children.Any(c => c == child));
        }        

        [Fact]
        public void ActorOf_gives_child_unique_name_if_not_specified()
        {
            //arrange
            var system = new ActorSystemImpl("test");
            system.Start(); //When we create a system manually we have to start it ourselves
            //act
            var child1 = system.ActorOf<TestActor>();
            var child2 = system.ActorOf<TestActor>();

            //assert
            Assert.NotEqual(child1.Path, child2.Path);
        }

        [Fact(DisplayName =@"If a fallaback config is declared with a deployment a actor should be able to be created for the main configuration and the fallback configuration")]
        public void ActorSystem_fallback_deployment_is_not_null_when_config_has_value()
        {
            //arrange
            var system = new ActorSystemImpl("test");
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
    }
}

