using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Event
{
    public class LoggerMailboxSpec : AkkaSpec
    {
        [Fact]
        public void CleanUp_drains_queue()
        {
            using (var loggerMailbox = new LoggerMailbox())
            {
                loggerMailbox.Post(TestActor, new Envelope {Message = "foo", Sender = TestActor});

                loggerMailbox.NumberOfMessages.ShouldBe(1);

                loggerMailbox.CleanUp();

                loggerMailbox.NumberOfMessages.ShouldBe(0);
            }
        }
    }
}