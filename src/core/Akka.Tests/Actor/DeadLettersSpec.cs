using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests
{
    
    public class DeadLettersSpec : AkkaSpec
    {
        [Fact]
        public void CanSendMessagesToDeadLetters()
        {
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            Sys.DeadLetters.Tell("foobar");
            ExpectMsg<DeadLetter>(deadLetter=>deadLetter.Message.Equals("foobar"));
        }
    }
}
