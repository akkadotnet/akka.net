//-----------------------------------------------------------------------
// <copyright file="GenericTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Google.Protobuf;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public abstract class GenericTransportSpec : AkkaSpec
    {
        private readonly Address _addressATest = new Address("test", "testsytemA", "testhostA", 4321);
        private readonly Address _addressBTest = new Address("test", "testsytemB", "testhostB", 5432);

        private Address _addressA;
        private Address _addressB;
        private Address _nonExistingAddress;
        private readonly bool _withAkkaProtocol;

        protected GenericTransportSpec(bool withAkkaProtocol = false)
            : base("akka.actor.provider = \"Akka.Remote.RemoteActorRefProvider, Akka.Remote\" ")
        {
            _withAkkaProtocol = withAkkaProtocol;
        }

        public override async Task InitializeAsync()
        {
            await base.InitializeAsync();
            
            _addressA = _addressATest.WithProtocol($"{SchemeIdentifier}.{_addressATest.Protocol}");
            _addressB = _addressBTest.WithProtocol($"{SchemeIdentifier}.{_addressBTest.Protocol}");
            _nonExistingAddress = new Address(SchemeIdentifier + ".test", "nosystem", "nohost", 0);
        }

        private TimeSpan DefaultTimeout { get { return Dilated(TestKitSettings.DefaultTimeout); } }

        protected abstract Akka.Remote.Transport.Transport FreshTransport(TestTransport testTransport);

        protected abstract string SchemeIdentifier { get; }

        private Akka.Remote.Transport.Transport WrapTransport(Akka.Remote.Transport.Transport transport)
        {
            if (_withAkkaProtocol) {
                var provider = (RemoteActorRefProvider)((ExtendedActorSystem)Sys).Provider;

                return new AkkaProtocolTransport(transport, Sys, new AkkaProtocolSettings(provider.RemoteSettings.Config), new AkkaPduProtobuffCodec(Sys));
            }

            return transport;
        }

        private Akka.Remote.Transport.Transport NewTransportA(AssociationRegistry registry)
        {
            return WrapTransport(FreshTransport(new TestTransport(_addressATest, registry)));
        }

        private Akka.Remote.Transport.Transport NewTransportB(AssociationRegistry registry)
        {
            return WrapTransport(FreshTransport(new TestTransport(_addressBTest, registry)));
        }

        [Fact]
        public async Task Transport_must_return_an_Address_and_promise_when_listen_is_called()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);

            var result = await transportA.Listen().WithTimeout(DefaultTimeout);

            Assert.Equal(_addressA, result.Item1);
            Assert.NotNull(result.Item2);

            Assert.Contains(registry.LogSnapshot().OfType<ListenAttempt>(), x => x.BoundAddress == _addressATest);
        }

        [Fact]
        public async Task Transport_must_associate_successfully_with_another_transport_of_its_kind()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);
            var transportB = NewTransportB(registry);

            // Must complete the returned promise to receive events
            (await transportA.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            (await transportB.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));

            await AwaitConditionAsync(async () => registry.TransportsReady(_addressATest, _addressBTest));

            // task is not awaited deliberately
            var task = transportA.Associate(_addressB);
            await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                if (o is InboundAssociation inbound && inbound.Association.RemoteAddress == _addressA)
                    return inbound.Association;

                return null;
            });

            Assert.Contains(registry.LogSnapshot().OfType<AssociateAttempt>(), x => x.LocalAddress == _addressATest && x.RemoteAddress == _addressBTest);
            await AwaitConditionAsync(async () => registry.ExistsAssociation(_addressATest, _addressBTest));
        }

        [Fact]
        public async Task Transport_must_fail_to_associate_with_non_existing_address()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);

            (await transportA.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            await AwaitConditionAsync(async () => registry.TransportsReady(_addressATest));

            // Transport throws InvalidAssociationException when trying to associate with non-existing system
            await Assert.ThrowsAsync<InvalidAssociationException>(async () =>
                await transportA.Associate(_nonExistingAddress).WithTimeout(DefaultTimeout)
            );
        }

        [Fact]
        public async Task Transport_must_successfully_send_PDUs()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);
            var transportB = NewTransportB(registry);

            (await transportA.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            (await transportB.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));

            await AwaitConditionAsync(async () => registry.TransportsReady(_addressATest, _addressBTest));

            var associate = transportA.Associate(_addressB);
            var handleB = await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                if (o is InboundAssociation handle && handle.Association.RemoteAddress == _addressA)
                    return handle.Association;

                return null;
            });

            var handleA = await associate.WithTimeout(DefaultTimeout);

            // Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            var payload = ByteString.CopyFromUtf8("PDU");
            var pdu = _withAkkaProtocol ? new AkkaPduProtobuffCodec(Sys).ConstructPayload(payload) : payload;
            
            await AwaitConditionAsync(async () => registry.ExistsAssociation(_addressATest, _addressBTest));

            handleA.Write(payload);
            await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundPayload from A", o =>
            {
                if (o is InboundPayload inboundPayload && inboundPayload.Payload.Equals(pdu))
                    return inboundPayload.Payload;

                return null;
            });

            Assert.Contains(registry.LogSnapshot().OfType<WriteAttempt>(), x => x.Sender == _addressATest && x.Recipient == _addressBTest && x.Payload.Equals(pdu));
        }

        [Fact]
        public async Task Transport_must_successfully_disassociate()
        {
            var registry = new AssociationRegistry();
            var transportA = NewTransportA(registry);
            var transportB = NewTransportB(registry);

            (await transportA.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));
            (await transportB.Listen().WithTimeout(DefaultTimeout)).Item2.SetResult(new ActorAssociationEventListener(TestActor));

            await AwaitConditionAsync(async () => registry.TransportsReady(_addressATest, _addressBTest));

            var associate = transportA.Associate(_addressB);
            var handleB = await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                if (o is InboundAssociation handle && handle.Association.RemoteAddress == _addressA)
                    return handle.Association;

                return null;
            });

            var handleA = await associate.WithTimeout(DefaultTimeout);

            // Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            await AwaitConditionAsync(async () => registry.ExistsAssociation(_addressATest, _addressBTest));

            handleA.Disassociate("Disassociation test", Log);

            await ExpectMsgOfAsync(DefaultTimeout, "Should receive Disassociated", o => o as Disassociated);

            await AwaitConditionAsync(async () => !registry.ExistsAssociation(_addressATest, _addressBTest));

            await AwaitConditionAsync(async () =>
                registry.LogSnapshot().OfType<DisassociateAttempt>().Any(x => x.Requestor == _addressATest && x.Remote == _addressBTest)
            );
        }
    }
}

