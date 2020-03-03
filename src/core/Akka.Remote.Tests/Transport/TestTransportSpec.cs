//-----------------------------------------------------------------------
// <copyright file="TestTransportSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util.Internal;
using Google.Protobuf;
using Xunit;

namespace Akka.Remote.Tests.Transport{

    
    public class TestTransportSpec : AkkaSpec
    {
        #region Setup / Teardown

        protected Address addressA = new Address("test", "testsystemA", "testhostA", 4321);
        protected Address addressB = new Address("test", "testsystemB", "testhostB", 5432);
        protected Address nonExistantAddress = new Address("test", "nosystem", "nohost", 0);

        public IActorRef Self { get { return TestActor; } }
        private TimeSpan DefaultTimeout { get { return Dilated(TestKitSettings.DefaultTimeout); } }
        #endregion

        #region Tests

        [Fact]
        public void TestTransport_must_return_an_Address_and_TaskCompletionSource_on_Listen()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(addressA, registry);

            //act
            var result = transportA.Listen();
            result.Wait(DefaultTimeout);

            //assert
            Assert.Equal(addressA, result.Result.Item1);
            Assert.NotNull(result.Result.Item2);

            var snapshot = registry.LogSnapshot();
            Assert.Equal(1, snapshot.Count);
            Assert.IsType<ListenAttempt>(snapshot[0]);
            Assert.Equal(addressA, ((ListenAttempt)snapshot[0]).BoundAddress);
        }

        [Fact]
        public void TestTransport_must_associate_successfully_with_another_TestTransport()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(addressA, registry);
            var transportB = new TestTransport(addressB, registry);

            //act

            //must complete returned promises to receive events
            var localConnectionFuture = transportA.Listen();
            localConnectionFuture.Wait(DefaultTimeout);
            localConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var remoteConnectionFuture = transportB.Listen();
            remoteConnectionFuture.Wait(DefaultTimeout);
            remoteConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var ready = registry.TransportsReady(addressA, addressB);
            Assert.True(ready);

            transportA.Associate(addressB);
            ExpectMsgPf<AssociationHandle>(DefaultTimeout, "Expect InboundAssociation from A",
                m => m.AsInstanceOf<InboundAssociation>().Association);

            //assert
            var associateAttempt = (registry.LogSnapshot().Single(x => x is AssociateAttempt)).AsInstanceOf<AssociateAttempt>();
            Assert.Equal(addressA, associateAttempt.LocalAddress);
            Assert.Equal(addressB, associateAttempt.RemoteAddress);
        }

        [Fact]
        public void TestTransport_fail_to_association_with_nonexisting_Address()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(addressA, registry);

            //act
            var result = transportA.Listen();
            result.Wait(DefaultTimeout);
            result.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            //assert
            XAssert.Throws<InvalidAssociationException>(() =>
            {
                var associateTask = transportA.Associate(nonExistantAddress);
                associateTask.Wait(DefaultTimeout);
                var fireException = associateTask.Result;
            });
        }

        [Fact]
        public void TestTransport_should_emulate_sending_PDUs()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(addressA, registry);
            var transportB = new TestTransport(addressB, registry);

            //act

            //must complete returned promises to receive events
            var localConnectionFuture = transportA.Listen();
            localConnectionFuture.Wait(DefaultTimeout);
            localConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var remoteConnectionFuture = transportB.Listen();
            remoteConnectionFuture.Wait(DefaultTimeout);
            remoteConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var ready = registry.TransportsReady(addressA, addressB);
            Assert.True(ready);

            var associate = transportA.Associate(addressB);
            var handleB = ExpectMsgPf<AssociationHandle>(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                var handle = o as InboundAssociation;
                if (handle != null && handle.Association.RemoteAddress.Equals(addressA)) return handle.Association;
                return null;
            });
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));

            associate.Wait(DefaultTimeout);
            var handleA = associate.Result;

            //Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));

            var akkaPDU = ByteString.CopyFromUtf8("AkkaPDU");

            var exists = registry.ExistsAssociation(addressA, addressB);
            Assert.True(exists);

            handleA.Write(akkaPDU);

            //assert
            ExpectMsgPf(DefaultTimeout, "Expect InboundPayload from A", o =>
            {
                var payload = o as InboundPayload;
                if (payload != null && payload.Payload.Equals(akkaPDU)) return akkaPDU;
                return null;
            });

            var writeAttempt = (registry.LogSnapshot().Single(x => x is WriteAttempt)).AsInstanceOf<WriteAttempt>();
            Assert.True(writeAttempt.Sender.Equals(addressA) && writeAttempt.Recipient.Equals(addressB)
                && writeAttempt.Payload.Equals(akkaPDU));
        }

        [Fact]
        public void TestTransport_should_emulate_disassociation()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(addressA, registry);
            var transportB = new TestTransport(addressB, registry);

            //act

            //must complete returned promises to receive events
            var localConnectionFuture = transportA.Listen();
            localConnectionFuture.Wait(DefaultTimeout);
            localConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var remoteConnectionFuture = transportB.Listen();
            remoteConnectionFuture.Wait(DefaultTimeout);
            remoteConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var ready = registry.TransportsReady(addressA, addressB);
            Assert.True(ready);

            var associate = transportA.Associate(addressB);
            var handleB = ExpectMsgPf<AssociationHandle>(DefaultTimeout, "Expect InboundAssociation from A", o =>
            {
                var handle = o as InboundAssociation;
                if (handle != null && handle.Association.RemoteAddress.Equals(addressA)) return handle.Association;
                return null;
            });
            handleB.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));

            associate.Wait(DefaultTimeout);
            var handleA = associate.Result;

            //Initialize handles
            handleA.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));

            var exists = registry.ExistsAssociation(addressA, addressB);
            Assert.True(exists);

            handleA.Disassociate();

            var msg = ExpectMsgPf(DefaultTimeout, "Expected Disassociated", o => o.AsInstanceOf<Disassociated>());

            //assert
            Assert.NotNull(msg);

            exists = registry.ExistsAssociation(addressA, addressB);
            Assert.True(!exists, "Association should no longer exist");

            var disassociateAttempt = registry.LogSnapshot().Single(x => x is DisassociateAttempt).AsInstanceOf<DisassociateAttempt>();
            Assert.True(disassociateAttempt.Requestor.Equals(addressA) && disassociateAttempt.Remote.Equals(addressB));
        }

        #endregion
    }
}

