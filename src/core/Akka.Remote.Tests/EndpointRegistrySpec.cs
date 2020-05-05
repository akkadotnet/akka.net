//-----------------------------------------------------------------------
// <copyright file="EndpointRegistrySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests
{
    
    public class EndpointRegistrySpec : AkkaSpec
    {
        private IActorRef actorA;
        private IActorRef actorB;
        
        Address address1 = new Address("test", "testsys1", "testhost1", 1234);
        Address address2 = new Address("test", "testsy2", "testhost2", 1234);

        public  EndpointRegistrySpec()
        {
            actorA = Sys.ActorOf(Props.Empty, "actorA");
            actorB = Sys.ActorOf(Props.Empty, "actorB");
        }

        [Fact]
        public void EndpointRegistry_must_be_able_to_register_a_writable_endpoint_and_policy()
        {
            var reg = new EndpointRegistry();
            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));

            Assert.Equal(actorA, reg.RegisterWritableEndpoint(address1, actorA,null));

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

            Assert.Equal(actorA, reg.RegisterReadOnlyEndpoint(address1, actorA, 0));
            Assert.Equal((actorA, 0), reg.ReadOnlyEndpointFor(address1));
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

            Assert.Equal(actorA, reg.RegisterReadOnlyEndpoint(address1, actorA, 1));
            Assert.Equal(actorB, reg.RegisterWritableEndpoint(address1, actorB, null));

            Assert.Equal((actorA,1), reg.ReadOnlyEndpointFor(address1));
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
            reg.RegisterWritableEndpoint(address1, actorA, null);
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
            reg.RegisterReadOnlyEndpoint(address1, actorA, 2);
            reg.MarkAsFailed(actorA, Deadline.Now);
            Assert.Null(reg.ReadOnlyEndpointFor(address1));
        }

        [Fact]
        public void EndpointRegistry_must_keep_tombstones_when_removing_an_endpoint()
        {
            var reg = new EndpointRegistry();
            reg.RegisterWritableEndpoint(address1, actorA, null);
            reg.RegisterWritableEndpoint(address2, actorB, null);
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
            reg.RegisterWritableEndpoint(address1, actorA, null);
            reg.RegisterWritableEndpoint(address2, actorB, null);
            reg.MarkAsFailed(actorA, Deadline.Now);
            var farIntheFuture = Deadline.Now + TimeSpan.FromSeconds(60);
            reg.MarkAsFailed(actorB, farIntheFuture);
            reg.Prune();

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

        [Fact]
        public void Fix_1845_EndpointRegistry_should_override_ReadOnly_endpoint()
        {
            var reg = new EndpointRegistry();
            var endpoint = TestActor;
            reg.RegisterReadOnlyEndpoint(address1, endpoint, 1);
            reg.RegisterReadOnlyEndpoint(address1, endpoint, 2);

            var ep = reg.ReadOnlyEndpointFor(address1);
            ep.Value.Item1.ShouldBe(endpoint);
            ep.Value.Item2.ShouldBe(2);
        }

        [Fact]
        public void EndpointRegistry_should_overwrite_Quarantine_policy_with_Pass_on_RegisterWritableEndpoint()
        {
            var reg = new EndpointRegistry();
            var deadline = Deadline.Now + TimeSpan.FromMinutes(30);
            var quarantinedUid = 42;

            Assert.Null(reg.WritableEndpointWithPolicyFor(address1));
            reg.MarkAsQuarantined(address1, quarantinedUid, deadline);
            Assert.True(reg.IsQuarantined(address1, quarantinedUid));

            var writableUid = 43;
            reg.RegisterWritableEndpoint(address1, TestActor, writableUid);
            Assert.True(reg.IsWritable(TestActor));
        }

        [Fact]
        public void EndpointRegistry_should_overwrite_Gated_policy_with_Pass_on_RegisterWritableEndpoint()
        {
            var reg = new EndpointRegistry();
            var deadline = Deadline.Now + TimeSpan.FromMinutes(30);
            var willBeGated = 42;

            reg.RegisterWritableEndpoint(address1, TestActor, willBeGated);
            Assert.NotNull(reg.WritableEndpointWithPolicyFor(address1));
            Assert.True(reg.IsWritable(TestActor));
            reg.MarkAsFailed(TestActor, deadline);
            Assert.False(reg.IsWritable(TestActor));

            var writableUid = 43;
            reg.RegisterWritableEndpoint(address1, TestActor, writableUid);
            Assert.True(reg.IsWritable(TestActor));
        }

        [Fact]
        public void EndpointRegistry_should_not_report_endpoint_as_writable_if_no_Pass_policy()
        {
            var reg = new EndpointRegistry();
            var deadline = Deadline.Now + TimeSpan.FromMinutes(30);
            
            Assert.False(reg.IsWritable(TestActor)); // no policy

            reg.RegisterWritableEndpoint(address1, TestActor, 42);
            Assert.True(reg.IsWritable(TestActor)); // pass
            reg.MarkAsFailed(TestActor, deadline); 
            Assert.False(reg.IsWritable(TestActor)); // Gated
            reg.RegisterWritableEndpoint(address1, TestActor, 43); // restarted
            Assert.True(reg.IsWritable(TestActor)); // pass
            reg.MarkAsQuarantined(address1, 43, deadline);
            Assert.False(reg.HasWritableEndpointFor(address1)); // Quarantined
        }

        [Fact]
        public void EndpointRegistry_should_keep_refuseUid_after_register_new_Endpoint()
        {
            var reg = new EndpointRegistry();
            var deadline = Deadline.Now + TimeSpan.FromMinutes(30);

            reg.RegisterWritableEndpoint(address1, actorA, null);
            reg.MarkAsQuarantined(address1, 42, deadline);
            reg.RefuseUid(address1).Should().Be(42);
            reg.IsQuarantined(address1, 42).Should().BeTrue();

            reg.UnregisterEndpoint(actorA);
            // Quarantined marker is kept so far
            var policy = reg.WritableEndpointWithPolicyFor(address1);
            policy.Should().BeOfType<EndpointManager.Quarantined>();
            policy.AsInstanceOf<EndpointManager.Quarantined>().Uid.Should().Be(42);
            policy.AsInstanceOf<EndpointManager.Quarantined>().Deadline.Should().Be(deadline);

            reg.RefuseUid(address1).Should().Be(42);
            reg.IsQuarantined(address1, 42).Should().BeTrue();

            reg.RegisterWritableEndpoint(address1, actorB, null);
            // Quarantined marker is gone
            var policy2 = reg.WritableEndpointWithPolicyFor(address1);
            policy2.Should().BeOfType<EndpointManager.Pass>();
            policy2.AsInstanceOf<EndpointManager.Pass>().Endpoint.Should().Be(actorB);
            // but we still have the refuseUid
            reg.RefuseUid(address1).Should().Be(42);
            reg.IsQuarantined(address1, 42).Should().BeTrue();
        }
    }
}

