//-----------------------------------------------------------------------
// <copyright file="GenericTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Serialization;
using Akka.Remote.Transport;
using Akka.TestKit;
using Google.Protobuf;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public abstract class GenericTransportSpec : AkkaSpec
    {
        private Address addressATest = new Address("test", "testsytemA", "testhostA", 4321);
        private Address addressBTest = new Address("test", "testsytemB", "testhostB", 5432);

        private Address addressA;
        private Address addressB;
        private Address nonExistingAddress;
        private bool withAkkaProtocol;

        public GenericTransportSpec(bool withAkkaProtocol = false)
            : base("akka.actor.provider = \"Akka.Remote.RemoteActorRefProvider, Akka.Remote\" ")
        {
            this.withAkkaProtocol = withAkkaProtocol;

            addressA = addressATest.WithProtocol(string.Format("{0}.{1}", SchemeIdentifier, addressATest.Protocol));
            addressB = addressBTest.WithProtocol(string.Format("{0}.{1}", SchemeIdentifier, addressBTest.Protocol));
            nonExistingAddress = new Address(SchemeIdentifier + ".test", "nosystem", "nohost", 0);
        }

        private TimeSpan DefaultTimeout { get { return Dilated(TestKitSettings.DefaultTimeout); } }

        protected abstract Akka.Remote.Transport.Transport FreshTransport(TestTransport testTransport);

        protected abstract string SchemeIdentifier { get; }

        private Akka.Remote.Transport.Transport WrapTransport(Akka.Remote.Transport.Transport transport)
        {
            if (withAkkaProtocol) {
                var provider = (RemoteActorRefProvider)((ExtendedActorSystem)Sys).Provider;

                return new AkkaProtocolTransport(transport, Sys, new AkkaProtocolSettings(provider.RemoteSettings.Config), new AkkaPduProtobuffCodec(Sys));
            }

            return transport;
        }

        private Akka.Remote.Transport.Transport NewTransportA(AssociationRegistry registry)
        {
            return WrapTransport(FreshTransport(new TestTransport(addressATest, registry)));
        }

        private Akka.Remote.Transport.Transport NewTransportB(AssociationRegistry registry)
        {
            return WrapTransport(FreshTransport(new TestTransport(addressBTest, registry)));
        }

        [Fact]
        public void Transport_must_return_an_Address_and_promise_when_listen_is_called()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);

            var result = AwaitResult(transportA.Listen());

            Assert.Equal(addressA, result.Item1);
            Assert.NotNull(result.Item2);

            Assert.Contains(registry.LogSnapshot().OfType<ListenAttempt>(), x => x.BoundAddress == addressATest);
        }

        [Fact]
        public void Transport_must_associate_successfully_with_another_transport_of_its_kind()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);
            var transportB = NewTransportB(registry);

            // Must complete the returned promise to receive events
            AwaitResult(transportA.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            AwaitResult(transportB.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));

            AwaitCondition(() => registry.TransportsReady(addressATest, addressBTest));

            transportA.Associate(addressB);
            ExpectMsgPf(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                var inbound = o as InboundAssociation;

                if (inbound != null && inbound.Association.RemoteAddress == addressA)
                    return inbound.Association;

                return null;
            });

            Assert.Contains(registry.LogSnapshot().OfType<AssociateAttempt>(), x => x.LocalAddress == addressATest && x.RemoteAddress == addressBTest);
            AwaitCondition(() => registry.ExistsAssociation(addressATest, addressBTest));
        }

        [Fact]
        public void Transport_must_fail_to_associate_with_nonexisting_address()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);

            AwaitResult(transportA.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            AwaitCondition(() => registry.TransportsReady(addressATest));

            // Transport throws InvalidAssociationException when trying to associate with non-existing system
            XAssert.Throws<InvalidAssociationException>(() =>
                AwaitResult(transportA.Associate(nonExistingAddress))
            );
        }

        [Fact]
        public void Transport_must_successfully_send_PDUs()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);
            var transportB = NewTransportB(registry);

            AwaitResult(transportA.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            AwaitResult(transportB.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));

            AwaitCondition(() => registry.TransportsReady(addressATest, addressBTest));

            var associate = transportA.Associate(addressB);
            var handleB = ExpectMsgPf(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                var handle = o as InboundAssociation;
                if (handle != null && handle.Association.RemoteAddress == addressA)
                    return handle.Association;

                return null;
            });

            var handleA = AwaitResult(associate);

            // Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            var payload = ByteString.CopyFromUtf8("PDU");
            var pdu = withAkkaProtocol ? new AkkaPduProtobuffCodec(Sys).ConstructPayload(payload) : payload;
            
            AwaitCondition(() => registry.ExistsAssociation(addressATest, addressBTest));

            handleA.Write(payload);
            ExpectMsgPf(DefaultTimeout, "Expect InboundPayload from A", o =>
            {
                var inboundPayload = o as InboundPayload;

                if (inboundPayload != null && inboundPayload.Payload.Equals(pdu))
                    return inboundPayload.Payload;

                return null;
            });

            Assert.Contains(registry.LogSnapshot().OfType<WriteAttempt>(), x => x.Sender == addressATest && x.Recipient == addressBTest && x.Payload.Equals(pdu));
        }

        [Fact]
        public void Transport_must_successfully_disassociate()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);
            var transportB = NewTransportB(registry);

            AwaitResult(transportA.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            AwaitResult(transportB.Listen()).Item2.SetResult(new ActorAssociationEventListener(TestActor));

            AwaitCondition(() => registry.TransportsReady(addressATest, addressBTest));

            var associate = transportA.Associate(addressB);
            var handleB = ExpectMsgPf(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                var handle = o as InboundAssociation;
                if (handle != null && handle.Association.RemoteAddress == addressA)
                    return handle.Association;

                return null;
            });

            var handleA = AwaitResult(associate);

            // Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            AwaitCondition(() => registry.ExistsAssociation(addressATest, addressBTest));

            handleA.Disassociate();

            ExpectMsgPf(DefaultTimeout, "Should receive Disassociated", o => o as Disassociated);

            AwaitCondition(() => !registry.ExistsAssociation(addressATest, addressBTest));

            AwaitCondition(() =>
                registry.LogSnapshot().OfType<DisassociateAttempt>().Any(x => x.Requestor == addressATest && x.Remote == addressBTest)
            );
        }

        private T AwaitResult<T>(Task<T> task)
        {
            task.Wait(DefaultTimeout);

            return task.Result;
        }
    }
}

