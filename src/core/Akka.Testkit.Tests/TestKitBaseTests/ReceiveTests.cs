using System;
using Akka.TestKit;
using Xunit;

namespace Akka.Testkit.Tests.TestKitBaseTests
{
    public class ReceiveTests : AkkaSpec
    {
        [Fact]
        public void ReceiveN_should_receive_correct_number_of_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            TestActor.Tell("3");
            TestActor.Tell("4");
            ReceiveN(3).ShouldOnlyContainInOrder("1", "2", "3");
            ReceiveN(1).ShouldOnlyContainInOrder("4");
        }

        [Fact]
        public void ReceiveN_should_timeout_if_no_messages()
        {
            Intercept(() => ReceiveN(3, TimeSpan.FromMilliseconds(10)));
        }

        [Fact]
        public void ReceiveN_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            Intercept(() => ReceiveN(3, TimeSpan.FromMilliseconds(100)));
        }


        [Fact]
        public void FishForMessage_should_return_matched_message()
        {
            TestActor.Tell(1);
            TestActor.Tell(2);
            TestActor.Tell(10);
            TestActor.Tell(20);
            FishForMessage<int>(i => i>=10).ShouldBe(10);
        }

        [Fact]
        public void FishForMessage_should_timeout_if_no_messages()
        {
            Intercept(() => FishForMessage(_=>false, TimeSpan.FromMilliseconds(10)));
        }

        [Fact]
        public void FishForMessage_should_timeout_if_to_few_messages()
        {
            TestActor.Tell("1");
            TestActor.Tell("2");
            Intercept(() => FishForMessage(_ => false, TimeSpan.FromMilliseconds(100)));
        }
    }
}