using Akka.Actor;
using Akka.Event;
using Xunit.Abstractions;

namespace Akka.TestKit.Xunit2.Internals
{
    public class TestOutputLogger : ReceiveActor
    {
        public TestOutputLogger(ITestOutputHelper output)
        {
            Receive<Debug>(e => output.WriteLine(e.ToString()));
            Receive<Info>(e => output.WriteLine(e.ToString()));
            Receive<Warning>(e => output.WriteLine(e.ToString()));
            Receive<Error>(e => output.WriteLine(e.ToString()));
            Receive<InitializeLogger>(e =>
            {
                e.LoggingBus.Subscribe(Self, typeof (LogEvent));
            });
        }
    }
}