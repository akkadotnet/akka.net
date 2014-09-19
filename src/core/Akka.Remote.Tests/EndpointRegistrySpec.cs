using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class EndpointRegistrySpec : AkkaSpec
    {
        private ActorRef actorA;
        private ActorRef actorB;
        
        Address address1 = new Address("test", "testsys1", "testhost1", 1234);
        Address address2 = new Address("test", "testsy2", "testhost2", 1234);

        public  EndpointRegistrySpec()
        {
            actorA = Sys.ActorOf(Props.Empty, "actorA");
            actorB = Sys.ActorOf(Props.Empty, "actorB");
        }

        [Fact]
        public void EndpointRegistry_must_be_able_to_register_a_writeable_endpoint_and_policy()
        {
            var reg = new EndpointRegistry();
            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));

            Assert.Equal(actorA, reg.RegisterWritableEndpoint(address1, actorA));

            Assert.IsType<EndpointManager.Pass>(reg.WritableEndpointWithPolicyFor(address1));
            Assert.Equal(actorA, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Pass>().Endpoint);

            Assert.Null(reg.ReadOnlyEndpointFor(address1));
            Assert.True(reg.IsWritable(actorA));
            Assert.False(reg.IsReadOnly(actorA));

            Assert.False(reg.IsQuarantined(address1, 42));
        }

        [Fact]
        public void EndpointRegistry_must_be_able_to_register_a_readonly_endpoint()
        {
            var reg = new EndpointRegistry();
            Assert.Null(reg.ReadOnlyEndpointFor(address1));

            Assert.Equal(actorA, reg.RegisterReadOnlyEndpoint(address1, actorA));
            Assert.Equal(actorA, reg.ReadOnlyEndpointFor(address1));
            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));
            Assert.False(reg.IsWritable(actorA));
            Assert.True(reg.IsReadOnly(actorA));
            Assert.False(reg.IsQuarantined(address1, 42));
        }

        [Fact]
        public void EndpointRegistry_must_be_able_to_register_writable_and_readonly_endpoint_correctly()
        {
            var reg = new EndpointRegistry();
            Assert.Null(reg.ReadOnlyEndpointFor(address1));
            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));

            Assert.Equal(actorA, reg.RegisterReadOnlyEndpoint(address1, actorA));
            Assert.Equal(actorB, reg.RegisterWritableEndpoint(address1, actorB));

            Assert.Equal(actorA, reg.ReadOnlyEndpointFor(address1));
            Assert.Equal(actorB, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Pass>().Endpoint);

            Assert.False(reg.IsWritable(actorA));
            Assert.True(reg.IsWritable(actorB));

            Assert.True(reg.IsReadOnly(actorA));
            Assert.False(reg.IsReadOnly(actorB));
        }

        [Fact]
        public void EndpointRegistry_must_be_able_to_register_Gated_policy_for_an_address()
        {
            var reg = new EndpointRegistry();
            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));
            reg.RegisterWritableEndpoint(address1, actorA);
            var deadline = Deadline.Now;
            reg.MarkAsFailed(actorA, deadline);
            Assert.Equal(deadline, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Gated>().TimeOfRelease);
            Assert.False(reg.IsReadOnly(actorA));
            Assert.False(reg.IsWritable(actorA));
        }

        [Fact]
        public void EndpointRegistry_must_remove_readonly_endpoints_if_marked_as_failed()
        {
            var reg = new EndpointRegistry();
            reg.RegisterReadOnlyEndpoint(address1, actorA);
            reg.MarkAsFailed(actorA, Deadline.Now);
            Assert.Null(reg.ReadOnlyEndpointFor(address1));
        }

        [Fact]
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

            Assert.Equal(deadline, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Gated>().TimeOfRelease);
            Assert.Equal(deadline, reg.WritableEndpointWithPolicyFor(address2).AsInstanceOf<EndpointManager.Quarantined>().Deadline);
            Assert.Equal(42, reg.WritableEndpointWithPolicyFor(address2).AsInstanceOf<EndpointManager.Quarantined>().Uid);
        }

        [Fact]
        public void EndpointRegistry_should_prune_outdated_Gated_directives_properly()
        {
            var reg = new EndpointRegistry();
            reg.RegisterWritableEndpoint(address1, actorA);
            reg.RegisterWritableEndpoint(address2, actorB);
            reg.MarkAsFailed(actorA, Deadline.Now);
            var farIntheFuture = Deadline.Now + TimeSpan.FromSeconds(60);
            reg.MarkAsFailed(actorB, farIntheFuture);
            reg.Prune();

            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));
            Assert.Equal(farIntheFuture, reg.WritableEndpointWithPolicyFor(address2).AsInstanceOf<EndpointManager.Gated>().TimeOfRelease);
        }

        [Fact]
        public void EndpointRegistry_should_be_able_to_register_Quarantined_policy_for_an_address()
        {
            var reg = new EndpointRegistry();
            var deadline = Deadline.Now + TimeSpan.FromMinutes(30);

            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));
            reg.MarkAsQuarantined(address1, 42, deadline);
            Assert.True(reg.IsQuarantined(address1, 42));
            Assert.False(reg.IsQuarantined(address1, 33));
            Assert.Equal(42, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Quarantined>().Uid);
            Assert.Equal(deadline, reg.WritableEndpointWithPolicyFor(address1).AsInstanceOf<EndpointManager.Quarantined>().Deadline);
        }
    }
}
