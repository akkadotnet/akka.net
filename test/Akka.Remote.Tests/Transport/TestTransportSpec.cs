using Akka.Actor;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Pigeon.Remote.Tests.Transport
{
    [TestClass]
    public class TestTransportSpec : AkkaSpec, ImplicitSender
    {
        public ActorRef Self { get { return testActor; } }
    }
}
