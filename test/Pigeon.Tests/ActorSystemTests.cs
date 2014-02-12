using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using System.Linq;

namespace Pigeon.Tests
{
    [TestClass]
    public class ActorSystemTests
    {
        public class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        [TestMethod]
        public void ActorSystem_ActorOf_adds_a_child_to_Guardian()
        {
            //arrange
            var system = new ActorSystem("test");

            //act
            var child = system.ActorOf<TestActor>("test");

            //assert
            var children = system.Provider.Guardian.Cell.GetChildren();
            Assert.IsTrue(children.Any(c => c == child));
        }        

        [TestMethod]
        public void ActorOf_gives_child_unique_name_if_not_specified()
        {
            //arrange
            var system = new ActorSystem("test");

            //act
            var child1 = system.ActorOf<TestActor>();
            var child2 = system.ActorOf<TestActor>();

            //assert
            Assert.AreNotEqual(child1.Path, child2.Path);
        }
    }
}
