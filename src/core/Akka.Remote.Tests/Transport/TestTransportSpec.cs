//-----------------------------------------------------------------------
// <copyright file="TestTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using Google.Protobuf;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    public class TestTransportSpec : AkkaSpec
    {
        #region Setup / Teardown

        private readonly Address _addressA = new Address("test", "testsystemA", "testhostA", 4321);
        private readonly Address _addressB = new Address("test", "testsystemB", "testhostB", 5432);
        private readonly Address _nonExistantAddress = new Address("test", "nosystem", "nohost", 0);

        private TimeSpan DefaultTimeout => Dilated(TestKitSettings.DefaultTimeout);
        #endregion

        #region Tests

        [Fact]
        public async Task TestTransport_must_return_an_Address_and_TaskCompletionSource_on_Listen()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(_addressA, registry);

            //act
            var result = await transportA.Listen().WithTimeout(DefaultTimeout);

            //assert
            Assert.Equal(_addressA, result.Item1);
            Assert.NotNull(result.Item2);

            var snapshot = registry.LogSnapshot();
            Assert.Equal(1, snapshot.Count);
            Assert.IsType<ListenAttempt>(snapshot[0]);
            Assert.Equal(_addressA, ((ListenAttempt)snapshot[0]).BoundAddress);
        }

        [Fact]
        public async Task TestTransport_must_associate_successfully_with_another_TestTransport()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(_addressA, registry);
            var transportB = new TestTransport(_addressB, registry);

            //act

            //must complete returned promises to receive events
            var localConnectionFuture = await transportA.Listen().WithTimeout(DefaultTimeout);
            localConnectionFuture.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            var remoteConnectionFuture = await transportB.Listen().WithTimeout(DefaultTimeout);
            remoteConnectionFuture.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            var ready = registry.TransportsReady(_addressA, _addressB);
            Assert.True(ready);

            // task is deliberately not awaited
            var task = transportA.Associate(_addressB);
            await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundAssociation from A",
                m => m.AsInstanceOf<InboundAssociation>().Association);

            //assert
            var associateAttempt = (registry.LogSnapshot().Single(x => x is AssociateAttempt)).AsInstanceOf<AssociateAttempt>();
            Assert.Equal(_addressA, associateAttempt.LocalAddress);
            Assert.Equal(_addressB, associateAttempt.RemoteAddress);
        }

        [Fact]
        public async Task TestTransport_fail_to_association_with_non_existing_Address()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(_addressA, registry);

            //act
            var result = await transportA.Listen().WithTimeout(DefaultTimeout);
            result.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            //assert
            await Assert.ThrowsAsync<InvalidAssociationException>(async () =>
            {
                await transportA.Associate(_nonExistantAddress).WithTimeout(DefaultTimeout);
            });
        }

        [Fact]
        public async Task TestTransport_should_emulate_sending_PDUs()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(_addressA, registry);
            var transportB = new TestTransport(_addressB, registry);

            //act

            //must complete returned promises to receive events
            var localConnectionFuture = await transportA.Listen().WithTimeout(DefaultTimeout);
            localConnectionFuture.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            var remoteConnectionFuture = await transportB.Listen().WithTimeout(DefaultTimeout);
            remoteConnectionFuture.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            var ready = registry.TransportsReady(_addressA, _addressB);
            Assert.True(ready);

            var associate = transportA.Associate(_addressB);
            var handleB = await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                if (o is InboundAssociation handle && handle.Association.RemoteAddress.Equals(_addressA)) 
                    return handle.Association;
                return null;
            });
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            await associate.WithTimeout(DefaultTimeout);
            var handleA = associate.Result;

            //Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            var akkaPdu = ByteString.CopyFromUtf8("AkkaPDU");

            var exists = registry.ExistsAssociation(_addressA, _addressB);
            Assert.True(exists);

            handleA.Write(akkaPdu);

            //assert
            await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundPayload from A", o =>
            {
                if (o is InboundPayload payload && payload.Payload.Equals(akkaPdu)) 
                    return akkaPdu;
                return null;
            });

            var writeAttempt = (registry.LogSnapshot().Single(x => x is WriteAttempt)).AsInstanceOf<WriteAttempt>();
            Assert.True(writeAttempt.Sender.Equals(_addressA) && writeAttempt.Recipient.Equals(_addressB)
                && writeAttempt.Payload.Equals(akkaPdu));
        }

        [Fact]
        public async Task TestTransport_should_emulate_disassociation()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(_addressA, registry);
            var transportB = new TestTransport(_addressB, registry);

            //act

            //must complete returned promises to receive events
            var localConnectionFuture = await transportA.Listen().WithTimeout(DefaultTimeout);
            localConnectionFuture.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            var remoteConnectionFuture = await transportB.Listen().WithTimeout(DefaultTimeout);
            remoteConnectionFuture.Item2.SetResult(new ActorAssociationEventListener(TestActor));

            var ready = registry.TransportsReady(_addressA, _addressB);
            Assert.True(ready);

            var associate = transportA.Associate(_addressB);
            var handleB = await ExpectMsgOfAsync(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                if (o is InboundAssociation handle && handle.Association.RemoteAddress.Equals(_addressA)) 
                    return handle.Association;
                return null;
            });
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            await associate.WithTimeout(DefaultTimeout);
            var handleA = associate.Result;

            //Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            var exists = registry.ExistsAssociation(_addressA, _addressB);
            Assert.True(exists);

            handleA.Disassociate("Disassociation test", Log);

            var msg = await ExpectMsgOfAsync(DefaultTimeout, "Expected Disassociated", o => o.AsInstanceOf<Disassociated>());

            //assert
            Assert.NotNull(msg);

            exists = registry.ExistsAssociation(_addressA, _addressB);
            Assert.True(!exists, "Association should no longer exist");

            var disassociateAttempt = registry.LogSnapshot().Single(x => x is DisassociateAttempt).AsInstanceOf<DisassociateAttempt>();
            Assert.True(disassociateAttempt.Requestor.Equals(_addressA) && disassociateAttempt.Remote.Equals(_addressB));
        }

        #endregion
    }
}

