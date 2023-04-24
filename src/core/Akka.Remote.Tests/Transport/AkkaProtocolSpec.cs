//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Google.Protobuf;
using Xunit;
using Xunit.Abstractions;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;
using static FluentAssertions.FluentActions;

namespace Akka.Remote.Tests.Transport
{
    public class AkkaProtocolSpec : AkkaSpec
    {
        #region Setup / Config

        private readonly Address _localAddress = new Address("test", "testsystem", "testhost", 1234);
        private readonly Address _localAkkaAddress = new Address("akka.test", "testsystem", "testhost", 1234);

        private readonly Address _remoteAddress = new Address("test", "testsystem2", "testhost2", 1234);
        private readonly Address _remoteAkkaAddress = new Address("akka.test", "testsystem2", "testhost2", 1234);

        private readonly AkkaPduCodec _codec;

        private readonly SerializedMessage _testMsg =
            new SerializedMessage { SerializerId = 0, Message = ByteString.CopyFromUtf8("foo") };

        private readonly ByteString _testEnvelope;
        private readonly ByteString _testMsgPdu;

        private readonly IHandleEvent _testHeartbeat;
        private readonly IHandleEvent _testPayload;
        private IHandleEvent TestDisassociate(DisassociateInfo info) => new InboundPayload(_codec.ConstructDisassociate(info));
        private IHandleEvent TestAssociate(int uid) => new InboundPayload(_codec.ConstructAssociate(new HandshakeInfo(_remoteAkkaAddress, uid)));
        private TimeSpan DefaultTimeout => Dilated(TestKitSettings.DefaultTimeout);

        public AkkaProtocolSpec(ITestOutputHelper helper)
            : base(@"akka.actor.provider = remote", helper)
        {
            _codec = new AkkaPduProtobuffCodec(Sys);
            _testEnvelope = _codec.ConstructMessage(_localAkkaAddress, TestActor, _testMsg);
            _testMsgPdu = _codec.ConstructPayload(_testEnvelope);

            _testHeartbeat = new InboundPayload(_codec.ConstructHeartbeat());
            _testPayload = new InboundPayload(_testMsgPdu);

            _config = ConfigurationFactory.ParseString(
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

                    shutdown-timeout = 5 s

                    startup-timeout = 5 s

                    use-passive-connections = on
                }}").WithFallback(Sys.Settings.Config);
        }

        private class Collaborators
        {
            public Collaborators(AssociationRegistry registry, TestTransport transport, TestAssociationHandle handle, TestFailureDetector failureDetector)
            {
                FailureDetector = failureDetector;
                Handle = handle;
                Transport = transport;
                Registry = registry;
            }

            public AssociationRegistry Registry { get; }

            public TestTransport Transport { get; }

            public TestAssociationHandle Handle { get; }

            public TestFailureDetector FailureDetector { get; }
        }

        private Collaborators GetCollaborators()
        {
            var registry = new AssociationRegistry();
            var transport = new TestTransport(_localAddress, registry);
            var handle = new TestAssociationHandle(_localAddress, _remoteAddress, transport, true);
            transport.WriteBehavior.PushConstant(true);
            return new Collaborators(registry, transport, handle, new TestFailureDetector());
        }

        private class TestFailureDetector : FailureDetector
        {
#pragma warning disable CS0420
            private volatile bool _isAvailable = true;
            public override bool IsAvailable => Volatile.Read(ref _isAvailable);
            public void SetAvailable(bool available) => Volatile.Write(ref _isAvailable, available);
            
            private volatile bool _called;
            public override bool IsMonitoring => Volatile.Read(ref _called);

            public override void HeartBeat()
            {
                Volatile.Write(ref _called, true);
            }
#pragma warning restore CS0420
        }

        private readonly Config _config;

        #endregion

        #region Tests

        [Fact]
        public async Task ProtocolStateActor_must_register_itself_as_reader_on_injected_handles()
        {
            var collaborators = GetCollaborators();
            Sys.ActorOf(ProtocolStateActor.InboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42), 
                wrappedHandle: collaborators.Handle,
                associationEventListener: new ActorAssociationEventListener(TestActor), 
                settings: new AkkaProtocolSettings(_config), 
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            await collaborators.Handle.ReadHandlerSource.Task.WithTimeout(DefaultTimeout);
        }

        [Fact]
        public async Task ProtocolStateActor_must_in_inbound_mode_accept_payload_after_Associate_PDU_received()
        {
            var collaborators = GetCollaborators();
            var reader = Sys.ActorOf(ProtocolStateActor.InboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                wrappedHandle: collaborators.Handle,
                associationEventListener: new ActorAssociationEventListener(TestActor), 
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            reader.Tell(TestAssociate(33), TestActor);

            await AwaitConditionAsync(() => Task.FromResult(collaborators.FailureDetector.IsMonitoring), DefaultTimeout);

            var wrappedHandle = await ExpectMsgOfAsync(DefaultTimeout, "expected InboundAssociation", o =>
            {
                var inbound = o.AsInstanceOf<InboundAssociation>();
                if (inbound == null) return null;
                var association = (AkkaProtocolHandle)inbound.Association;
                Assert.Equal(33, association.HandshakeInfo.Uid);
                return association;
            });
            Assert.NotNull(wrappedHandle);

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            Assert.True(collaborators.FailureDetector.IsMonitoring);

            // Heartbeat was sent in response to Associate
            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsHeartbeat(collaborators.Registry)), DefaultTimeout);

            reader.Tell(_testPayload, TestActor);
            await ExpectMsgAsync<InboundPayload>(inbound =>
            {
                Assert.Equal(_testEnvelope, inbound.Payload);
            }, hint: "expected InboundPayload");
        }

        [Fact]
        public async Task ProtocolStateActor_must_in_inbound_mode_disassociate_when_an_unexpected_message_arrives_instead_of_Associate()
        {
            var collaborators = GetCollaborators();

            var reader = Sys.ActorOf(ProtocolStateActor.InboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                wrappedHandle: collaborators.Handle,
                associationEventListener: new ActorAssociationEventListener(TestActor),
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            //a stray message will force a disassociate
            reader.Tell(_testHeartbeat, TestActor);

            //this associate will now be ignored
            reader.Tell(TestAssociate(33), TestActor);

            await AwaitConditionAsync(() =>
            {
                var snapshots = collaborators.Registry.LogSnapshot();
                return Task.FromResult(snapshots.Any(x => x is DisassociateAttempt));
            }, DefaultTimeout);
        }

        [Fact]
        public async Task ProtocolStateActor_must_in_outbound_mode_delay_readiness_until_handshake_finished()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader = Sys.ActorOf(ProtocolStateActor.OutboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                remoteAddress: _remoteAddress,
                statusCompletionSource: statusPromise,
                transport: collaborators.Transport,
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsAssociate(collaborators.Registry, 42)), DefaultTimeout);
            
            await AwaitConditionAsync(() => Task.FromResult(collaborators.FailureDetector.IsMonitoring), DefaultTimeout);

            //keeps sending heartbeats
            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsHeartbeat(collaborators.Registry)), DefaultTimeout);

            Assert.False(statusPromise.Task.IsCompleted);

            //finish the connection by sending back an associate message
            reader.Tell(TestAssociate(33), TestActor);

            await statusPromise.Task.WithTimeout(3.Seconds());
            switch (statusPromise.Task.Result)
            {
                case AkkaProtocolHandle h:
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                    Assert.Equal(33, h.HandshakeInfo.Uid);
                    break;
                default:
                    throw new Exception("Did not receive expected AkkaProtocolHandle from handshake");
            }
        }

        [Fact]
        public async Task ProtocolStateActor_must_handle_explicit_disassociate_messages()
        {
            var collaborators = GetCollaborators();
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();
            var reader = Sys.ActorOf(ProtocolStateActor.OutboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                remoteAddress: _remoteAddress,
                statusCompletionSource: statusPromise, 
                transport: collaborators.Transport,
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsAssociate(collaborators.Registry, 42)), DefaultTimeout);

            reader.Tell(TestAssociate(33), TestActor);

            await statusPromise.Task.WithTimeout(3.Seconds());
            var result = statusPromise.Task.Result;
            switch (result)
            {
                case AkkaProtocolHandle h:
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                    break;
                default:
                    throw new Exception("Did not receive expected AkkaProtocolHandle from handshake");
            }

            var wrappedHandle = (AkkaProtocolHandle) result;
            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            reader.Tell(TestDisassociate(DisassociateInfo.Unknown), TestActor);

            await ExpectMsgOfAsync("expected Disassociated(DisassociateInfo.Unknown", o =>
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
            var reader = Sys.ActorOf(ProtocolStateActor.OutboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                remoteAddress: _remoteAddress,
                statusCompletionSource: statusPromise,
                transport: collaborators.Transport,
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsAssociate(collaborators.Registry, 42)), DefaultTimeout);

            reader.Tell(TestAssociate(33), TestActor);

            await statusPromise.Task.WithTimeout(TimeSpan.FromSeconds(3));
            var result = statusPromise.Task.Result;
            switch (result)
            {
                case AkkaProtocolHandle h:
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                    break;
                default:
                    throw new Exception("Did not receive expected AkkaProtocolHandle from handshake");
            }
            var wrappedHandle = (AkkaProtocolHandle)result;

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            reader.Tell(new Disassociated(DisassociateInfo.Unknown));

            await ExpectMsgOfAsync("expected Disassociated(DisassociateInfo.Unknown", o =>
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
            var stateActor = Sys.ActorOf(ProtocolStateActor.OutboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                remoteAddress: _remoteAddress,
                statusCompletionSource: statusPromise,
                transport: collaborators.Transport,
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsAssociate(collaborators.Registry, 42)), DefaultTimeout);

            stateActor.Tell(TestAssociate(33), TestActor);

            await statusPromise.Task.WithTimeout(TimeSpan.FromSeconds(3));
            var result = statusPromise.Task.Result;
            switch (result)
            {
                case AkkaProtocolHandle h:
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                    break;
                default:
                    throw new Exception("Did not receive expected AkkaProtocolHandle from handshake");
            }
            var wrappedHandle = (AkkaProtocolHandle)result;

            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            //wait for one heartbeat
            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsHeartbeat(collaborators.Registry)), DefaultTimeout);

            collaborators.FailureDetector.SetAvailable(false);

            await ExpectMsgOfAsync("expected Disassociated(DisassociateInfo.Unknown", o =>
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
            var stateActor = Sys.ActorOf(ProtocolStateActor.OutboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                remoteAddress: _remoteAddress,
                statusCompletionSource: statusPromise, 
                transport: collaborators.Transport,
                settings: new AkkaProtocolSettings(_config),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            await AwaitConditionAsync(() => Task.FromResult(LastActivityIsAssociate(collaborators.Registry, 42)), DefaultTimeout);

            stateActor.Tell(TestAssociate(33), TestActor);

            await statusPromise.Task.WithTimeout(TimeSpan.FromSeconds(3));
            var result = statusPromise.Task.Result;
            switch (result)
            {
                case AkkaProtocolHandle h:
                    Assert.Equal(_remoteAkkaAddress, h.RemoteAddress);
                    Assert.Equal(_localAkkaAddress, h.LocalAddress);
                    break;
                default:
                    throw new Exception("Did not receive expected AkkaProtocolHandle from handshake");
            }
            var wrappedHandle = (AkkaProtocolHandle)result;

            stateActor.Tell(new Disassociated(DisassociateInfo.Unknown), TestActor);

            //handler tries to register after the association has closed
            wrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(TestActor));

            await ExpectMsgOfAsync("expected Disassociated(DisassociateInfo.Unknown", o =>
            {
                var disassociated = o.AsInstanceOf<Disassociated>();

                Assert.NotNull(disassociated);
                Assert.Equal(DisassociateInfo.Unknown, disassociated.Info);

                return disassociated;
            });
        }

        [Fact]
        public async Task ProtocolStateActor_must_give_up_outbound_after_connection_timeout()
        {
            var collaborators = GetCollaborators();
            collaborators.Handle.Writeable = false;
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var statusPromise = new TaskCompletionSource<AssociationHandle>();

            var conf2 = ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.connection-timeout = 500 ms")
                .WithFallback(_config);

            var stateActor = Sys.ActorOf(ProtocolStateActor.OutboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                remoteAddress: _remoteAddress,
                statusCompletionSource: statusPromise,
                transport: collaborators.Transport,
                settings: new AkkaProtocolSettings(conf2),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            Watch(stateActor);

            await Awaiting(async () =>
            {
                await statusPromise.Task.WithTimeout(TimeSpan.FromSeconds(5));
            }).Should().ThrowAsync<TimeoutException>();
            
            await ExpectTerminatedAsync(stateActor);
        }

        [Fact]
        public async Task ProtocolStateActor_must_give_up_inbound_after_connection_timeout()
        {
            var collaborators = GetCollaborators();
            collaborators.Handle.Writeable = false;
            collaborators.Transport.AssociateBehavior.PushConstant(collaborators.Handle);

            var conf2 = ConfigurationFactory.ParseString("akka.remote.dot-netty.tcp.connection-timeout = 500 ms")
                .WithFallback(_config);

            var reader = Sys.ActorOf(ProtocolStateActor.InboundProps(
                handshakeInfo: new HandshakeInfo(_localAddress, 42),
                wrappedHandle: collaborators.Handle,
                associationEventListener: new ActorAssociationEventListener(TestActor),
                settings: new AkkaProtocolSettings(conf2),
                codec: _codec,
                failureDetector: collaborators.FailureDetector));

            Watch(reader);
            await ExpectTerminatedAsync(reader);
        }

        #endregion

        #region Internal helper methods

        private bool LastActivityIsHeartbeat(AssociationRegistry associationRegistry)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            if (!(associationRegistry.LogSnapshot().Last() is WriteAttempt attempt)) return false;
            if (!attempt.Sender.Equals(_localAddress) || !attempt.Recipient.Equals(_remoteAddress)) return false;
            return _codec.DecodePdu(attempt.Payload) switch
            {
                Heartbeat _ => true,
                _ => false
            };
        }

        private bool LastActivityIsAssociate(AssociationRegistry associationRegistry, long uid)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            if (!(associationRegistry.LogSnapshot().Last() is WriteAttempt attempt)) return false;
            if (!attempt.Sender.Equals(_localAddress) || !attempt.Recipient.Equals(_remoteAddress)) return false;
            return _codec.DecodePdu(attempt.Payload) switch
            {
                Associate h => h.Info.Origin.Equals(_localAddress) && h.Info.Uid == uid,
                _ => false
            };
        }

        private bool LastActivityIsDisassociate(AssociationRegistry associationRegistry)
        {
            if (associationRegistry.LogSnapshot().Count == 0) return false;
            if (!(associationRegistry.LogSnapshot().Last() is WriteAttempt attempt)) return false;
            if (!attempt.Sender.Equals(_localAddress) || !attempt.Recipient.Equals(_remoteAddress)) return false;
            return _codec.DecodePdu(attempt.Payload) switch
            {
                Disassociate _ => true,
                _ => false
            };
        }

        #endregion
    }
}

