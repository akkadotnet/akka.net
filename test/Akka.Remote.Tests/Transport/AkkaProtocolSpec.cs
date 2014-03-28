using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Tests;
using Google.ProtocolBuffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Remote.Tests.Transport
{
    [TestClass]
    public class AkkaProtocolSpec : AkkaSpec
    {
        #region Setup / Config

        Address localAddress = new Address("test", "testsystem", "testhost", 1234);
        Address localAkkaAddress = new Address("akka.test", "testsystem", "testhost", 1234);

        Address remoteAddress = new Address("test", "testsystem2", "testhost2", 1234);
        Address remoteAkkaAddress = new Address("akka.test", "testsystem2", "testhost2", 1234);

        AkkaPduCodec codec = new AkkaPduProtobuffCodec();

        SerializedMessage testMsg =
            SerializedMessage.CreateBuilder().SetSerializerId(0).SetMessage(ByteString.CopyFromUtf8("foo")).Build();

        private ByteString testEnvelope;
        private ByteString testMsgPdu;
        
        private IHandleEvent testHeartbeat;
        private IHandleEvent testPayload;
        private IHandleEvent testDisassociate(DisassociateInfo info) { return new InboundPayload(codec.ConstructDisassociate(info)); }
        private IHandleEvent testAssociate(int uid) { return new InboundPayload(codec.ConstructAssociate(new HandshakeInfo(remoteAkkaAddress, uid))); }

        [TestInitialize]
        public override void Setup()
        {
            testEnvelope = codec.ConstructMessage(localAkkaAddress, testActor, testMsg);
            testMsgPdu = codec.ConstructPayload(testEnvelope);

            testHeartbeat = new InboundPayload(codec.ConstructHeartbeat());
            testPayload = new InboundPayload(testMsgPdu);

            base.Setup();
        }


        public class TestFailureDetector : FailureDetector
        {
            internal volatile bool isAvailable = true;
            public override bool IsAvailable
            {
                get { return isAvailable; }
            }

            internal volatile bool called = false;
            public override bool IsMonitoring
            {
                get { return called; }
            }

            public override void HeartBeat()
            {
                called = true;
            }
        }

        private Config config = ConfigurationFactory.ParseString(
        @"akka.remote {

            transport-failure-detector {
              implementation-class = ""Akka.Remote.PhiAccrualFailureDetector, Akka.Remote""
              threshold = 7.0
              max-sample-size = 100
              min-std-deviation = 100 ms
              acceptable-heartbeat-pause = 3 s
              heartbeat-interval = 1 s
            }

            backoff-interval = 1 s

            require-cookie = off

            secure-cookie = ""abcde""

            shutdown-timeout = 5 s

            startup-timeout = 5 s

            use-passive-connections = on
        }");

        #endregion

    }
}
