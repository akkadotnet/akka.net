using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class FSMTimingSpec : AkkaSpec, ImplicitSender
    {
        public ActorRef Self { get { return testActor; } }

        #region Actors
        #endregion
    }
}
