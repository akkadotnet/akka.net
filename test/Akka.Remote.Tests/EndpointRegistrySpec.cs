using System;
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

        [TestMethod]
        public void EndpointRegistry_must_be_able_to_register_a_readonly_endpoint()
        {
            var reg = new EndpointRegistry();
            Assert.IsNull(reg.ReadOnlyEndpointFor(address1));

            Assert.AreEqual(actorA, reg.RegisterReadOnlyEndpoint(address1, actorA));
            Assert.AreEqual(actorA, reg.ReadOnlyEndpointFor(address1));
            Assert.IsNull(reg.WritableEndpointWithPolicyFor(address1));
            Assert.IsFalse(reg.IsWritable(actorA));
            Assert.IsTrue(reg.IsReadOnly(actorA));
            Assert.IsFalse(reg.IsQuarantined(address1, 42));
        }

        [TestMethod]
        public void EndpointRegistry_must_be_able_to_register_writable_and_readonly_endpoint_correctly()
        {
            var reg = new EndpointRegistry();
            Assert.IsNull(reg.ReadOnlyEndpointFor(address1));
            Assert.IsNull(reg.WritableEndpointWithPolicyFor(address1));

            Assert.AreEqual(actorA, reg.RegisterReadOnlyEndpoint(address1, actorA));
            Assert.AreEqual(actorB, reg.RegisterWritableEndpoint(address1, actorB));

            Assert.AreEqual(actorA, reg.ReadOnlyEndpointFor(address1));
            Assert.AreEqual(actorB, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Pass>().Endpoint);

            Assert.IsFalse(reg.IsWritable(actorA));
            Assert.IsTrue(reg.IsWritable(actorB));

            Assert.IsTrue(reg.IsReadOnly(actorA));
            Assert.IsFalse(reg.IsReadOnly(actorB));
        }

        [TestMethod]
        public void EndpointRegistry_must_be_able_to_register_Gated_policy_for_an_address()
        {
            var reg = new EndpointRegistry();
            Assert.IsNull(reg.WritableEndpointWithPolicyFor(address1));
            reg.RegisterWritableEndpoint(address1, actorA);
            var deadline = Deadline.Now;
            reg.MarkAsFailed(actorA, deadline);
            Assert.AreEqual(deadline, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Gated>().TimeOfRelease);
            Assert.IsFalse(reg.IsReadOnly(actorA));
            Assert.IsFalse(reg.IsWritable(actorA));
        }

        [TestMethod]
        public void EndpointRegistry_must_remove_readonly_endpoints_if_marked_as_failed()
        {
            var reg = new EndpointRegistry();
            reg.RegisterReadOnlyEndpoint(address1, actorA);
            reg.MarkAsFailed(actorA, Deadline.Now);
            Assert.IsNull(reg.ReadOnlyEndpointFor(address1));
        }

        [TestMethod]
        public void EndpointRegistry_must_keep_tombstones_when_removing_an_endpoint()
        {
            var reg = new EndpointRegistry();
            reg.RegisterWritableEndpoint(address1, actorA);
            reg.RegisterWritableEndpoint(address2, actorB);
            var deadline = Deadline.Now;
            reg.MarkAsFailed(actorA, deadline);
            reg.MarkAsQuarantined(address2, 42, deadline);

            reg.UnregisterEndpoint(actorA);
            reg.UnregisterEndpoint(actorB);

            Assert.AreEqual(deadline, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Gated>().TimeOfRelease);
            Assert.AreEqual(deadline, reg.WritableEndpointWithPolicyFor(address2).AsInstanceOf<EndpointManager.Quarantined>().Deadline);
            Assert.AreEqual(42, reg.WritableEndpointWithPolicyFor(address2).AsInstanceOf<EndpointManager.Quarantined>().Uid);
        }

        [TestMethod]
        public void EndpointRegistry_should_prune_outdated_Gated_directives_properly()
        {
            var reg = new EndpointRegistry();
            reg.RegisterWritableEndpoint(address1, actorA);
            reg.RegisterWritableEndpoint(address2, actorB);
            reg.MarkAsFailed(actorA, Deadline.Now);
            var farIntheFuture = Deadline.Now + TimeSpan.FromSeconds(60);
            reg.MarkAsFailed(actorB, farIntheFuture);
            reg.Prune();

            Assert.IsNull(reg.WritableEndpointWithPolicyFor(address1));
            Assert.AreEqual(farIntheFuture, reg.WritableEndpointWithPolicyFor(address2).AsInstanceOf<EndpointManager.Gated>().TimeOfRelease);
        }

        [TestMethod]
        public void EndpointRegistry_should_be_able_to_register_Quarantined_policy_for_an_address()
        {
            var reg = new EndpointRegistry();
            var deadline = Deadline.Now + TimeSpan.FromMinutes(30);

            Assert.IsNull(reg.WritableEndpointWithPolicyFor(address1));
            reg.MarkAsQuarantined(address1, 42, deadline);
            Assert.IsTrue(reg.IsQuarantined(address1, 42));
            Assert.IsFalse(reg.IsQuarantined(address1, 33));
            Assert.AreEqual(42, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Quarantined>().Uid);
            Assert.AreEqual(deadline, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Quarantined>().Deadline);
        }
    }
}
