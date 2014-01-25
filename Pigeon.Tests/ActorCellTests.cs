using Microsoft.VisualStudio.TestTools.UnitTesting;
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Tests
{
    [TestClass]
    public class ActorCellTests
    {
        public class TestActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
            }
        }

        public class TestParentActor : UntypedActor
        {
            private ActorRef child = Context.ActorOf<TestActor>();

            protected override void OnReceive(object message)
            {
            }
        }

        [TestMethod]
        public void Context_ActorOf_adds_a_child()
        {
            //arrange
            var system = new ActorSystem("test");

            //act
            var parent = system.ActorOf<TestParentActor>("test");

            //assert
            var children = parent.Cell.GetChildren();
            Assert.IsTrue(children.Count() == 1);
        }
    }
}
