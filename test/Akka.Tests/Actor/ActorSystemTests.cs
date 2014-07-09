using System;
using Xunit;
using Akka.Actor;
using System.Linq;

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
            var system = new ActorSystem("test");

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
            var system = new ActorSystem("test");

            //act
            var child1 = system.ActorOf<TestActor>();
            var child2 = system.ActorOf<TestActor>();

            //assert
            Assert.NotEqual(child1.Path, child2.Path);
        }
    }
}
