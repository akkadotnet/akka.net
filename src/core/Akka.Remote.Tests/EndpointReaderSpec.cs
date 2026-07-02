//-----------------------------------------------------------------------
// <copyright file="EndpointReaderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;
using Akka.TestKit;
using Akka.Util.Internal;
using Google.Protobuf;
using Xunit;
using SerializedMessage = Akka.Remote.Serialization.Proto.Msg.Payload;

namespace Akka.Remote.Tests
{
    public class EndpointReaderSpec : AkkaSpec
    {
        private static readonly Address LocalAddress = new("akka.tcp", "EndpointReaderSpec", "127.0.0.1", 2551);
        private static readonly Address RemoteAddress = new("akka.tcp", "EndpointReaderSpecRemote", "127.0.0.1", 2552);

        private readonly AkkaPduProtobuffCodec _codec;

        public EndpointReaderSpec(ITestOutputHelper output)
            : base(ConfigurationFactory.ParseString("akka.actor.provider = remote"), output)
        {
            _codec = new AkkaPduProtobuffCodec(Sys);
        }

        [Fact(DisplayName = "EndpointReader should decode sequence payload while reading")]
        public async Task EndpointReader_should_decode_sequence_payload_while_reading()
        {
            var (reader, dispatchProbe, ackProbe) = CreateReader();
            var expectedAck = Ack();
            var expectedMessage = SerializedPayload();

            reader.Tell(new InboundSequencePayload(new ReadOnlySequence<byte>(
                _codec.ConstructMessage(LocalAddress, CreateFixedActorRef("recipient"), expectedMessage, ackOption: expectedAck).Memory)));

            var ack = await ackProbe.ExpectMsgAsync<Ack>();
            AssertAck(expectedAck, ack);

            var dispatched = await dispatchProbe.ExpectMsgAsync<Dispatched>();
            Assert.Equal(expectedMessage, dispatched.Message);
        }

        [Fact(DisplayName = "EndpointReader should decode sequence ACKs while not reading")]
        public async Task EndpointReader_should_decode_sequence_acks_while_not_reading()
        {
            var (reader, dispatchProbe, ackProbe) = CreateReader();
            var expectedAck = Ack();

            reader.Tell(new EndpointWriter.StopReading(TestActor, TestActor));
            await ExpectMsgAsync<EndpointWriter.StoppedReading>();

            reader.Tell(new InboundSequencePayload(new ReadOnlySequence<byte>(
                _codec.ConstructMessage(LocalAddress, CreateFixedActorRef("recipient"), SerializedPayload(), ackOption: expectedAck).Memory)));

            var ack = await ackProbe.ExpectMsgAsync<Ack>();
            AssertAck(expectedAck, ack);
            await dispatchProbe.ExpectNoMsgAsync(RemainingOrDefault);
        }

        private (IActorRef Reader, TestProbe DispatchProbe, TestProbe AckProbe) CreateReader()
        {
            var dispatchProbe = CreateTestProbe();
            var ackProbe = CreateTestProbe();
            var transport = new AkkaProtocolTransport(
                new TestTransport(LocalAddress, new AssociationRegistry()),
                Sys,
                new AkkaProtocolSettings(Sys.Settings.Config),
                _codec);

            var reader = Sys.ActorOf(EndpointReader.ReaderProps(
                LocalAddress,
                RemoteAddress,
                transport,
                RARP.For(Sys).Provider.RemoteSettings,
                _codec,
                new ProbeInboundMessageDispatcher(dispatchProbe.Ref),
                inbound: false,
                uid: 1,
                receiveBuffers: new ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState>(),
                reliableDeliverySupervisor: ackProbe.Ref));

            return (reader, dispatchProbe, ackProbe);
        }

        private IActorRef CreateFixedActorRef(string name)
        {
            return new FixedActorRef(
                new RootActorPath(LocalAddress) / "user" / name,
                Sys.AsInstanceOf<ExtendedActorSystem>().Provider);
        }

        private static SerializedMessage SerializedPayload()
        {
            return new SerializedMessage
            {
                SerializerId = 123,
                MessageManifest = ByteString.CopyFromUtf8("manifest-a"),
                Message = ByteString.CopyFrom(1, 2, 3, 4)
            };
        }

        private static Ack Ack()
        {
            return new Ack(new SeqNo(10), new[] { new SeqNo(11), new SeqNo(12) });
        }

        private static void AssertAck(Ack expected, Ack actual)
        {
            Assert.Equal(expected.CumulativeAck, actual.CumulativeAck);
            Assert.Equal(expected.Nacks.ToArray(), actual.Nacks.ToArray());
        }

        private sealed class ProbeInboundMessageDispatcher : IInboundMessageDispatcher
        {
            private readonly IActorRef _probe;

            public ProbeInboundMessageDispatcher(IActorRef probe)
            {
                _probe = probe;
            }

            public void Dispatch(IInternalActorRef recipient, Address recipientAddress, SerializedMessage message,
                IActorRef senderOption = null!)
            {
                _probe.Tell(new Dispatched(message));
            }
        }

        private sealed class Dispatched
        {
            public Dispatched(SerializedMessage message)
            {
                Message = message;
            }

            public SerializedMessage Message { get; }
        }

        private sealed class FixedActorRef : MinimalActorRef
        {
            public FixedActorRef(ActorPath path, IActorRefProvider provider)
            {
                Path = path;
                Provider = provider;
            }

            public override ActorPath Path { get; }

            public override IActorRefProvider Provider { get; }
        }
    }
}
