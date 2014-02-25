using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class ActorSelectionSpec : AkkaSpec
    {
        [TestMethod()]
        public void CanResolveChildPath()
        {
            var selection = sys.ActorSelection("user/test");
            selection.Tell("hello");
            expectMsg("hello");
        }

        [TestMethod()]
        public void CanResolveUpAndDownPath()
        {
            var selection = sys.ActorSelection("user/test/../../user/test");
            selection.Tell("hello");
            expectMsg("hello");
        }

        [TestMethod()]
        public void CanAskActorSelection()
        {
            var selection = sys.ActorSelection("user/echo");
            var task = selection.Ask("hello");
            expectMsg("hello");
            task.Wait();
            Assert.AreEqual("hello", task.Result);
        }

        #region Tests for verifying that ActorSelections made within an ActorContext can be resolved

        /// <summary>
        /// Accepts a Tuple containing a string representation of an ActorPath and a message, respectively
        /// </summary>
        public class ActorContextSelectionActor : TypedActor, IHandle<Tuple<string, string>>
        {
            public void Handle(Tuple<string, string> message)
            {
                var testActorSelection = Context.ActorSelection(message.Item1);
                testActorSelection.Tell(message.Item2);
            }
        }

        [TestMethod()]
        public void CanResolveAbsoluteActorPathInActorContext()
        {
            var contextActor = sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string,string>("/user/test", "hello"));
            expectMsg("hello");
        }

        [TestMethod()]
        public void CanResolveRelativeActorPathInActorContext()
        {
            var contextActor = sys.ActorOf<ActorContextSelectionActor>();
            contextActor.Tell(new Tuple<string, string>("../test/../../user/test", "hello"));
            expectMsg("hello");
        }

        #endregion
    }
}
