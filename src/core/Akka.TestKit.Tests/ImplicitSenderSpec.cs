using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Testkit.Tests
{

    public class ImplicitSenderSpec : AkkaSpec, ImplicitSender
    {
        [Fact]
        public void ImplicitSender_should_have_testActor_as_itself()
        {
            //arrange

            //act

            //assert
            Self.ShouldBe(TestActor);
        }

        public ActorRef Self { get { return TestActor; } }
    }
}
