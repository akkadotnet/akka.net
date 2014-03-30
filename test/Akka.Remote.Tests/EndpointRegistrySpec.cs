using Akka.Actor;
using Akka.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests
{
    [TestClass]
    public class EndpointRegistrySpec : AkkaSpec
    {
        private ActorRef actorA;
        private ActorRef actorB;
        
        Address address1 = new Address("test", "testsys1", "testhost1", 1234);
        Address address2 = new Address("test", "testsy2", "testhost2", 1234);

        [TestInitialize]
        public override void Setup()
        {
            base.Setup();
            actorA = sys.ActorOf(Props.Empty, "actorA");
            actorB = sys.ActorOf(Props.Empty, "actorB");
        }

        [TestMethod]
        public void EndpointRegistry_must_be_able_to_register_a_writeable_endpoint_and_policy()
        {
            var reg = new EndpointRegistry();
            Assert.IsNull(reg.WritableEndpointWithPolicyFor(address1));

            Assert.AreEqual(actorA, reg.RegisterWritableEndpoint(address1, actorA));

            Assert.IsInstanceOfType(reg.WritableEndpointWithPolicyFor(address1), typeof(EndpointManager.Pass));
            Assert.AreEqual(actorA, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Pass>().Endpoint);

            Assert.IsNull(reg.ReadOnlyEndpointFor(address1));
            Assert.IsTrue(reg.IsWritable(actorA));
            Assert.IsFalse(reg.IsReadOnly(actorA));

            Assert.IsFalse(reg.IsQuarantined(address1, 42));
        }
    }
}
