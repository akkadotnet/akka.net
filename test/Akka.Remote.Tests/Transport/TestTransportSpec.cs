using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Tests;
using Google.ProtocolBuffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests.Transport{

    [TestClass]
    public class TestTransportSpec : AkkaSpec, ImplicitSender
    {
        #region Setup / Teardown

        protected Address addressA = new Address("test", "testsystemA", "testhostA", 4321);
        protected Address addressB = new Address("test", "testsystemB", "testhostB", 5432);
        protected Address nonExistantAddress = new Address("test", "nosystem", "nohost", 0);

        public ActorRef Self { get { return testActor; } }

        #endregion

        #region Tests

        [TestMethod]
        public void TestTransport_must_return_an_Address_and_TaskCompletionSource_on_Listen()
        {
            //arrange
            var registry = new AssociationRegistry();
            var transportA = new TestTransport(addressA, registry);

            //act
            var result = transportA.Listen();
            result.Wait(DefaultTimeout);

            //assert
            Assert.AreEqual(addressA, result.Result.Item1);
            Assert.IsNotNull(result.Result.Item2);

            var snapshot = registry.LogSnapshot();
            Assert.AreEqual(1, snapshot.Count);
            Assert.IsInstanceOfType(snapshot[0], typeof(ListenAttempt));
            Assert.AreEqual(addressA, ((ListenAttempt)snapshot[0]).BoundAddress);
        }

        [TestMethod]
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
            Assert.IsTrue(ready);

            transportA.Associate(addressB);
            expectMsgPF<AssociationHandle>(DefaultTimeout, "Expect InboundAssociation from A",
                m => m.AsInstanceOf<InboundAssociation>().Association);

            //assert
            var associateAttempt = (registry.LogSnapshot().Single(x => x is AssociateAttempt)).AsInstanceOf<AssociateAttempt>();
            Assert.AreEqual(addressA, associateAttempt.LocalAddress);
            Assert.AreEqual(addressB, associateAttempt.RemoteAddress);
        }

        [TestMethod]
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
            intercept<InvalidAssociationException>(() =>
            {
                var associateTask = transportA.Associate(nonExistantAddress);
                associateTask.Wait(DefaultTimeout);
                var fireException = associateTask.Result;
            });
        }

        [TestMethod]
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
            Assert.IsTrue(ready);

            var associate = transportA.Associate(addressB);
            var handleB = expectMsgPF<AssociationHandle>(DefaultTimeout, "Expect InboundAssociation from A", o =>
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
            handleA.ReadHandlerSource.Task.Wait(DefaultTimeout);

            var akkaPDU = ByteString.CopyFromUtf8("AkkaPDU");

            var exists = registry.ExistsAssociation(addressA, addressB);
            Assert.IsTrue(exists);

            handleA.Write(akkaPDU);

            //assert
            expectMsgPF(DefaultTimeout, "Expect InboundPayload from A", o =>
            {
                var payload = o as InboundPayload;
                if (payload != null && payload.Payload.Equals(akkaPDU)) return akkaPDU;
                return null;
            });

            var writeAttempt = (registry.LogSnapshot().Single(x => x is WriteAttempt)).AsInstanceOf<WriteAttempt>();
            Assert.IsTrue(writeAttempt.Sender.Equals(addressA) && writeAttempt.Recipient.Equals(addressB)
                && writeAttempt.Payload.Equals(akkaPDU));
        }

        #endregion
    }
}
