using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WrappedTerminated
    {
        private readonly Terminated _terminated;

        public WrappedTerminated(Terminated terminated)
        {
            _terminated = terminated;
        }

        public Terminated Terminated { get { return _terminated; } }
    }
}