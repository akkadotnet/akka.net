using Akka.Actor;
using Akka.Tests;
using Xunit;

namespace Akka.TestKit
{
    
    public class ImplicitSenderSpec : AkkaSpec, ImplicitSender
    {
        [Fact]
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
