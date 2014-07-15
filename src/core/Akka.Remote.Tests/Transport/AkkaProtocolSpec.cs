﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.Tests;
using Google.ProtocolBuffers;
using Xunit;

namespace Akka.Remote.Tests.Transport
{
    
    public class AkkaProtocolSpec : AkkaSpec, ImplicitSender
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

        public AkkaProtocolSpec()
        {
            testEnvelope = codec.ConstructMessage(localAkkaAddress, testActor, testMsg);
            testMsgPdu = codec.ConstructPayload(testEnvelope);

            testHeartbeat = new InboundPayload(codec.ConstructHeartbeat());
            testPayload = new InboundPayload(testMsgPdu);
        }

        public class Collaborators
        {
            public Collaborators(AssociationRegistry registry, TestTransport transport, TestAssociationHandle handle, TestFailureDetector failureDetector)
            {
                FailureDetector = failureDetector;
                Handle = handle;
                Transport = transport;
                Registry = registry;
            }

            public AssociationRegistry Registry { get; private set; }

            public TestTransport Transport { get; private set; }

            public TestAssociationHandle Handle { get; private set; }

            public TestFailureDetector FailureDetector { get; private set; }
        }

        public Collaborators GetCollaborators()
        {
            var registry = new AssociationRegistry();
            var transport = new TestTransport(localAddress, registry);
            var handle = new TestAssociationHandle(localAddress, remoteAddress, transport, true);
            transport.WriteBehavior.PushConstant(true);
            return new Collaborators(registry, transport, handle, new TestFailureDetector());
        }

        protected override string GetConfig()
        {
            return @"akka.test.default-timeout = 1.5 s";
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

        #region Tests

        [Fact]
        public async Task ProtocolStateActor_must_register_itself_as_reader_on_injected_handles()
        {
            var collaborators = GetCollaborators();
            sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(localAddress, 42), collaborators.Handle,
                    new ActorAssociationEventListener(testActor), new AkkaProtocolSettings(config), codec,
                    collaborators.FailureDetector));

            await AwaitCond(() => collaborators.Handle.ReadHandlerSource.Task.IsCompleted, DefaultTimeout);
        }

        [Fact]
        public async Task ProtocolStateActor_must_in_inbound_mode_accept_payload_after_Associate_PDU_received()
        {
            var collaborators = GetCollaborators();
            var reader =
                sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(localAddress, 42), collaborators.Handle,
                    new ActorAssociationEventListener(testActor), new AkkaProtocolSettings(config), codec,
                    collaborators.FailureDetector));

            reader.Tell(testAssociate(33), Self);

            await AwaitCond(() => collaborators.FailureDetector.called, DefaultTimeout);

            var wrappedHandle = expectMsgPF<AkkaProtocolHandle>(DefaultTimeout, "expected InboundAssociation", o =>
            {
                var inbound = o.AsInstanceOf<InboundAssociation>();
                if (inbound == null) return null;
                Assert.Equal(33, inbound.Association.AsInstanceOf<AkkaProtocolHandle>().HandshakeInfo.Uid);
                return inbound.Association.AsInstanceOf<AkkaProtocolHandle>();
            });
            Assert.NotNull(wrappedHandle);

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(testActor));

            Assert.True(collaborators.FailureDetector.called);

            // Heartbeat was sent in response to Associate
            await AwaitCond(() => LastActivityIsHeartbeat(collaborators.Registry), DefaultTimeout);

            reader.Tell(testPayload, Self);

            expectMsgPF<InboundPayload>("expected InboundPayload", o =>
            {
                var inbound = o.AsInstanceOf<InboundPayload>();
                Assert.Equal(testEnvelope, inbound.Payload);
                return null;
            });
        }

        [Fact]
        public async Task ProtocolStateActor_must_in_inbound_mode_disassociate_when_an_unexpected_message_arrives_instead_of_Associate()
        {
            var collaborators = GetCollaborators();

            var reader =
                sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(localAddress, 42), collaborators.Handle,
                    new ActorAssociationEventListener(testActor), new AkkaProtocolSettings(config), codec,
                    collaborators.FailureDetector));

            //a stray message will force a disassociate
            reader.Tell(testHeartbeat, Self);

            //this associate will now be ignored
            reader.Tell(testAssociate(33), Self);

            await AwaitCond(() =>
            {
                var snapshots = collaborators.Registry.LogSnapshot();
                return snapshots.Any(x => x is DisassociateAttempt);
            }, DefaultTimeout);
        }

        [Fact]
        public async Task ProtocolStateActor_must_in_outbound_mode_delay_readiness_until_handshake_finished()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader =
                sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(localAddress, 42), remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            await AwaitCond(() => LastActivityIsAssociate(collaborators.Registry,42), DefaultTimeout);
            Assert.True(collaborators.FailureDetector.called);

            //keeps sending heartbeats
            await AwaitCond(() => LastActivityIsHeartbeat(collaborators.Registry), DefaultTimeout);

            Assert.False(statusPromise.Task.IsCompleted);

            //finish the connection by sending back an associate message
            reader.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(localAkkaAddress, h.LocalAddress);
                    Assert.Equal(33, h.HandshakeInfo.Uid);
                })
                .Default(msg => Assert.True(false,"Did not receive expected AkkaProtocolHandle from handshake"));
        }

        [Fact]
        public async Task ProtocolStateActor_must_handle_explicit_disassociate_messages()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader =
                sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(localAddress, 42), remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            await AwaitCond(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            reader.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false,"Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(testActor));

            reader.Tell(testDisassociate(DisassociateInfo.Unknown), Self);

            expectMsgPF<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public async Task ProtocolStateActor_must_handle_transport_level_disassociations()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader =
                sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(localAddress, 42), remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            await AwaitCond(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            reader.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false,"Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(testActor));

            reader.Tell(new Disassociated(DisassociateInfo.Unknown));

            expectMsgPF<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public async Task ProtocolStateActor_must_disassociate_when_failure_detector_signals_failure()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var stateActor =
                sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(localAddress, 42), remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            await AwaitCond(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            stateActor.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false,"Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(testActor));

            //wait for one heartbeat
            await AwaitCond(() => LastActivityIsHeartbeat(collaborators.Registry), DefaultTimeout);

            collaborators.FailureDetector.isAvailable = false;

            expectMsgPF<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public async Task ProtocolStateActor_must_handle_correctly_when_the_handler_is_registered_only_after_the_association_is_already_closed()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var stateActor =
                sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(localAddress, 42), remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            await AwaitCond(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            stateActor.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false,"Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            stateActor.Tell(new Disassociated(DisassociateInfo.Unknown), Self);

            //handler tries to register after the association has closed
            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(testActor));

            expectMsgPF<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        #endregion

        #region Internal helper methods

        private bool LastActivityIsHeartbeat(AssociationRegistry associationRegistry)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            var rValue = false;
            associationRegistry.LogSnapshot().Last().Match()
                .With<WriteAttempt>(attempt =>
                {
                    if (attempt.Sender.Equals(localAddress) && attempt.Recipient.Equals(remoteAddress))
                    {
                        codec.DecodePdu(attempt.Payload)
                            .Match()
                            .With<Heartbeat>(h => rValue = true)
                            .Default(msg => rValue = false);
                    }
                });

            return rValue;
        }

        private bool LastActivityIsAssociate(AssociationRegistry associationRegistry, long uid)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            var rValue = false;
            associationRegistry.LogSnapshot().Last().Match()
                .With<WriteAttempt>(attempt =>
                {
                    if (attempt.Sender.Equals(localAddress) && attempt.Recipient.Equals(remoteAddress))
                    {
                        codec.DecodePdu(attempt.Payload)
                            .Match()
                            .With<Associate>(h => rValue = h.Info.Origin.Equals(localAddress) && h.Info.Uid == uid)
                            .Default(msg => rValue = false);
                    }
                });

            return rValue;
        }

        private bool LastActivityIsDisassociate(AssociationRegistry associationRegistry)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            var rValue = false;
            associationRegistry.LogSnapshot().Last().Match()
                .With<WriteAttempt>(attempt =>
                {
                    if (attempt.Sender.Equals(localAddress) && attempt.Recipient.Equals(remoteAddress))
                    {
                        codec.DecodePdu(attempt.Payload)
                            .Match()
                            .With<Disassociate>(h => rValue = true)
                            .Default(msg => rValue = false);
                    }
                });

            return rValue;
        }

        #endregion

        public ActorRef Self { get { return testActor; } }
    }
}
