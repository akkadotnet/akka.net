//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Serialization;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Tests.Transport
{
    public class AkkaProtocolSpec : AkkaSpec
    {
        #region Setup / Config

        private readonly Address _localAddress = new Address("test", "testsystem", "testhost", 1234);
        private readonly Address _localAkkaAddress = new Address("akka.test", "testsystem", "testhost", 1234);

        private readonly Address _remoteAddress = new Address("test", "testsystem2", "testhost2", 1234);
        private readonly Address _remoteAkkaAddress = new Address("akka.test", "testsystem2", "testhost2", 1234);

        private AkkaPduCodec codec;

        private readonly SerializedMessage testMsg =
            new SerializedMessage { SerializerId = 0, Message = ByteString.CopyFromUtf8("foo") };

        private ByteString testEnvelope;
        private ByteString testMsgPdu;

        private IHandleEvent testHeartbeat;
        private IHandleEvent testPayload;
        private IHandleEvent testDisassociate(DisassociateInfo info) { return new InboundPayload(codec.ConstructDisassociate(info)); }
        private IHandleEvent testAssociate(int uid) { return new InboundPayload(codec.ConstructAssociate(new HandshakeInfo(_remoteAkkaAddress, uid))); }
        private TimeSpan DefaultTimeout => Dilated(TestKitSettings.DefaultTimeout);

        public AkkaProtocolSpec(ITestOutputHelper helper)
            : base(@"akka.actor.provider = remote", helper)
        {
            codec = new AkkaPduProtobuffCodec(Sys);
            testEnvelope = codec.ConstructMessage(_localAkkaAddress, TestActor, testMsg);
            testMsgPdu = codec.ConstructPayload(testEnvelope);

            testHeartbeat = new InboundPayload(codec.ConstructHeartbeat());
            testPayload = new InboundPayload(testMsgPdu);

            config = ConfigurationFactory.ParseString(
                @"akka{
                    remote {

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
                }}").WithFallback(Sys.Settings.Config);
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
            var transport = new TestTransport(_localAddress, registry);
            var handle = new TestAssociationHandle(_localAddress, _remoteAddress, transport, true);
            transport.WriteBehavior.PushConstant(true);
            return new Collaborators(registry, transport, handle, new TestFailureDetector());
        }

        public class TestFailureDetector : FailureDetector
        {
            internal volatile bool isAvailable = true;
            public override bool IsAvailable => isAvailable;

            internal volatile bool called = false;
            public override bool IsMonitoring => called;

            public override void HeartBeat()
            {
                called = true;
            }
        }

        private Config config;

        #endregion

        #region Tests

        [Fact]
        public void ProtocolStateActor_must_register_itself_as_reader_on_injected_handles()
        {
            var collaborators = GetCollaborators();
            Sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(_localAddress, 42), collaborators.Handle,
                new ActorAssociationEventListener(TestActor), new AkkaProtocolSettings(config), codec,
                collaborators.FailureDetector));

            AwaitCondition(() => collaborators.Handle.ReadHandlerSource.Task.IsCompleted, DefaultTimeout);
        }

        [Fact]
        public void ProtocolStateActor_must_in_inbound_mode_accept_payload_after_Associate_PDU_received()
        {
            var collaborators = GetCollaborators();
            var reader =
                Sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(_localAddress, 42), collaborators.Handle,
                    new ActorAssociationEventListener(TestActor), new AkkaProtocolSettings(config), codec,
                    collaborators.FailureDetector));

            reader.Tell(testAssociate(33), Self);

            AwaitCondition(() => collaborators.FailureDetector.called, DefaultTimeout);

            var wrappedHandle = ExpectMsgPf<AkkaProtocolHandle>(DefaultTimeout, "expected InboundAssociation", o =>
            {
                var inbound = o.AsInstanceOf<InboundAssociation>();
                if (inbound == null) return null;
                Assert.Equal(33, inbound.Association.AsInstanceOf<AkkaProtocolHandle>().HandshakeInfo.Uid);
                return inbound.Association.AsInstanceOf<AkkaProtocolHandle>();
            });
            Assert.NotNull(wrappedHandle);

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            Assert.True(collaborators.FailureDetector.called);

            // Heartbeat was sent in response to Associate
            AwaitCondition(() => LastActivityIsHeartbeat(collaborators.Registry), DefaultTimeout);

            reader.Tell(testPayload, Self);
            ExpectMsg<InboundPayload>(inbound =>
            {
                Assert.Equal(testEnvelope, inbound.Payload);
            }, hint: "expected InboundPayload");
        }

        [Fact]
        public void ProtocolStateActor_must_in_inbound_mode_disassociate_when_an_unexpected_message_arrives_instead_of_Associate()
        {
            var collaborators = GetCollaborators();

            var reader =
                Sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(_localAddress, 42), collaborators.Handle,
                    new ActorAssociationEventListener(TestActor), new AkkaProtocolSettings(config), codec,
                    collaborators.FailureDetector));

            //a stray message will force a disassociate
            reader.Tell(testHeartbeat, Self);

            //this associate will now be ignored
            reader.Tell(testAssociate(33), Self);

            AwaitCondition(() =>
            {
                var snapshots = collaborators.Registry.LogSnapshot();
                return snapshots.Any(x => x is DisassociateAttempt);
            }, DefaultTimeout);
        }

        [Fact]
        public void ProtocolStateActor_must_in_outbound_mode_delay_readiness_until_handshake_finished()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader =
                Sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(_localAddress, 42), _remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            AwaitCondition(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);
            Assert.True(collaborators.FailureDetector.called);

            //keeps sending heartbeats
            AwaitCondition(() => LastActivityIsHeartbeat(collaborators.Registry), DefaultTimeout);

            Assert.False(statusPromise.Task.IsCompleted);

            //finish the connection by sending back an associate message
            reader.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                    Assert.Equal(33, h.HandshakeInfo.Uid);
                })
                .Default(msg => Assert.True(false, "Did not receive expected AkkaProtocolHandle from handshake"));
        }

        [Fact]
        public void ProtocolStateActor_must_handle_explicit_disassociate_messages()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader =
                Sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(_localAddress, 42), _remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            AwaitCondition(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            reader.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false, "Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            reader.Tell(testDisassociate(DisassociateInfo.Unknown), Self);

            ExpectMsgPf<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public void ProtocolStateActor_must_handle_transport_level_disassociations()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader =
                Sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(_localAddress, 42), _remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            AwaitCondition(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            reader.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false, "Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            reader.Tell(new Disassociated(DisassociateInfo.Unknown));

            ExpectMsgPf<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public void ProtocolStateActor_must_disassociate_when_failure_detector_signals_failure()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var stateActor =
                Sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(_localAddress, 42), _remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            AwaitCondition(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            stateActor.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false, "Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            //wait for one heartbeat
            AwaitCondition(() => LastActivityIsHeartbeat(collaborators.Registry), DefaultTimeout);

            collaborators.FailureDetector.isAvailable = false;

            ExpectMsgPf<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public void ProtocolStateActor_must_handle_correctly_when_the_handler_is_registered_only_after_the_association_is_already_closed()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var stateActor =
                Sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(_localAddress, 42), _remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(config), codec, collaborators.FailureDetector));

            AwaitCondition(() => LastActivityIsAssociate(collaborators.Registry, 42), DefaultTimeout);

            stateActor.Tell(testAssociate(33), Self);

            statusPromise.Task.Wait(TimeSpan.FromSeconds(3));
            statusPromise.Task.Result.Match()
                .With<AkkaProtocolHandle>(h =>
                {
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                })
                .Default(msg => Assert.True(false, "Did not receive expected AkkaProtocolHandle from handshake"));
            var wrappedHandle = statusPromise.Task.Result.AsInstanceOf<AkkaProtocolHandle>();

            stateActor.Tell(new Disassociated(DisassociateInfo.Unknown), Self);

            //handler tries to register after the association has closed
            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            ExpectMsgPf<Disassociated>("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public void ProtocolStateActor_must_give_up_outbound_after_connection_timeout()
        {
            var collaborators = GetCollaborators();
            collaborators.Handle.Writeable = false;
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();

            var conf2 = ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.connection-timeout = 500 ms")
                .WithFallback(config);

            var stateActor =
                Sys.ActorOf(ProtocolStateActor.OutboundProps(new HandshakeInfo(_localAddress, 42), _remoteAddress,
                    statusPromise, collaborators.Transport,
                    new AkkaProtocolSettings(conf2), codec, collaborators.FailureDetector));

            Watch(stateActor);

            // inner exception will be a TimeoutException
            Intercept<AggregateException>(() =>
            {
                statusPromise.Task.Wait(TimeSpan.FromSeconds(5)).Should().BeTrue();
            });
            ExpectTerminated(stateActor);
        }

        [Fact]
        public void ProtocolStateActor_must_give_up_inbound_after_connection_timeout()
        {
            var collaborators = GetCollaborators();
            collaborators.Handle.Writeable = false;
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var conf2 = ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.connection-timeout = 500 ms")
                .WithFallback(config);

            var reader =
                Sys.ActorOf(ProtocolStateActor.InboundProps(new HandshakeInfo(_localAddress, 42), collaborators.Handle,
                    new ActorAssociationEventListener(TestActor), new AkkaProtocolSettings(conf2), codec,
                    collaborators.FailureDetector));

            Watch(reader);
            ExpectTerminated(reader);
        }

        #endregion

        #region Internal helper methods

        private bool LastActivityIsHeartbeat(AssociationRegistry associationRegistry)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            var rValue = false;
            if (associationRegistry.LogSnapshot().Last() is WriteAttempt)
            {
                var attempt = (WriteAttempt)associationRegistry.LogSnapshot().Last();
                if (attempt.Sender.Equals(_localAddress) && attempt.Recipient.Equals(_remoteAddress))
                {
                    codec.DecodePdu(attempt.Payload)
                        .Match()
                        .With<Heartbeat>(h => rValue = true)
                        .Default(msg => rValue = false);
                }
            }

            return rValue;
        }

        private bool LastActivityIsAssociate(AssociationRegistry associationRegistry, long uid)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            var rValue = false;
            if (associationRegistry.LogSnapshot().Last() is WriteAttempt)
            {
                var attempt = (WriteAttempt)associationRegistry.LogSnapshot().Last();
                if (attempt.Sender.Equals(_localAddress) && attempt.Recipient.Equals(_remoteAddress))
                {
                    codec.DecodePdu(attempt.Payload)
                        .Match()
                        .With<Associate>(h => rValue = h.Info.Origin.Equals(_localAddress) && h.Info.Uid == uid)
                        .Default(msg => rValue = false);
                }
            }

            return rValue;
        }

        private bool LastActivityIsDisassociate(AssociationRegistry associationRegistry)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            var rValue = false;
            if (associationRegistry.LogSnapshot().Last() is WriteAttempt)
            {
                var attempt = (WriteAttempt)associationRegistry.LogSnapshot().Last();
                if (attempt.Sender.Equals(_localAddress) && attempt.Recipient.Equals(_remoteAddress))
                    codec.DecodePdu(attempt.Payload)
                        .Match()
                        .With<Disassociate>(h => rValue = true)
                        .Default(msg => rValue = false);
            }

            return rValue;
        }

        #endregion

        public IActorRef Self { get { return TestActor; } }
    }
}

