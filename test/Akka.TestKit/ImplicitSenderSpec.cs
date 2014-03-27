using Akka.Actor;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.TestKit
{
    [TestClass]
    public class ImplicitSenderSpec : AkkaSpec, ImplicitSender
    {
        [TestMethod]
        public void ImplicitSender_should_have_testActor_as_itself()
        {
            //arrange

            //act

            //assert
            Self.ShouldBe(testActor);
        }

        public ActorRef Self { get { return testActor; } }
    }
}
