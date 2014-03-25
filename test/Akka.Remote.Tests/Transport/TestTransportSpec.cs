using System.Threading;
using Akka.Actor;
using Akka.Remote.Transport;
using Akka.Tests;
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
            var cts = new CancellationTokenSource(DefaultTimeout);
            var result = transportA.Listen();
            result.Wait((cts.Token));

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
            var cts1 = new CancellationTokenSource(DefaultTimeout);
            var localConnectionFuture = transportA.Listen();
            localConnectionFuture.Wait((cts1.Token));
            localConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var cts2 = new CancellationTokenSource(DefaultTimeout);
            var remoteConnectionFuture = transportB.Listen();
            remoteConnectionFuture.Wait(cts2.Token);
            remoteConnectionFuture.Result.Item2.SetResult(new ActorAssociationEventListener(Self));

            var ready = registry.TransportsReady(addressA, addressB);
            Assert.IsTrue(ready);

            var cts3 = new CancellationTokenSource(DefaultTimeout);
            var associationHandle = transportA.Associate(addressB);
            associationHandle.Wait(cts3.Token);
            expectMsgPF<InboundAssociation>(DefaultTimeout, "Expect InboundAssociation from A",
                m => m.Association.RemoteAddress.Equals(addressA));

            //assert
            Assert.IsTrue(registry.LogSnapshot().Contains(new AssociateAttempt(addressA, addressB)));
        }

        #endregion
    }
}
