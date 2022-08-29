// //-----------------------------------------------------------------------
// // <copyright file="AkkaPduCodecBenchmark.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Transport;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;

namespace Akka.Benchmarks.Remoting
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class AkkaPduCodecBenchmark
    {
        public const int Operations = 10_000;

        private ExtendedActorSystem _sys1;
        private IRemoteActorRefProvider _rarp;

        private Config _config = @"akka.actor.provider = remote
                                     akka.remote.dot-netty.tcp.port = 0";

        private IActorRef _senderActorRef;
        private IActorRef _receiveRef;

        private Address _addr1;
        private AkkaPduProtobuffCodec _codec;

        /// <summary>
        /// The message we're going to serialize
        /// </summary>
        private readonly object _message = "foobar";

        private readonly Ack _lastAck = new Ack(-1);

        private ByteString _fullDecode;
        private ByteString _pduDecoded;
        private Akka.Remote.Serialization.Proto.Msg.Payload _payloadDecoded;

        [GlobalSetup]
        public async Task Setup()
        {
            _sys1 = (ExtendedActorSystem)ActorSystem.Create("BenchSys", _config);

            var es = (ExtendedActorSystem)_sys1;
            _rarp = RARP.For(_sys1).Provider;
            _addr1 = es.Provider.DefaultAddress;

            _senderActorRef =
                _sys1.ActorOf(act => { act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender)); },
                    "sender1");

            _receiveRef = _sys1.ActorOf(act => { act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender)); },
                "recv1");

            _codec = new AkkaPduProtobuffCodec(_sys1);
            _fullDecode = CreatePayloadPdu();
            _pduDecoded = ((Payload)_codec.DecodePdu(_fullDecode)).Bytes;
            _payloadDecoded = _codec.DecodeMessage(_pduDecoded, _rarp, _addr1).MessageOption.SerializedMessage;
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            await _sys1.Terminate();
        }

        /// <summary>
        /// Simulates the write-side of the wire
        /// </summary>
        [Benchmark(OperationsPerInvoke = Operations)]
        public void WritePayloadPdu()
        {
            for (var i = 0; i < Operations; i++)
            {
                CreatePayloadPdu();
            }
        }

        /// <summary>
        /// Simulates the read-side of the wire
        /// </summary>
        [Benchmark(OperationsPerInvoke = Operations)]
        public void DecodePayloadPdu()
        {
            for (var i = 0; i < Operations; i++)
            {
                var pdu = _codec.DecodePdu(_fullDecode);
                if (pdu is Payload p)
                {
                    var msg = _codec.DecodeMessage(p.Bytes, _rarp, _addr1);
                    var deserialize = MessageSerializer.Deserialize(_sys1, msg.MessageOption.SerializedMessage);
                }
            }
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void DecodePduOnly()
        {
            for (var i = 0; i < Operations; i++)
            {
                var pdu = _codec.DecodePdu(_fullDecode);
            }
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void DecodeMessageOnly()
        {
            for (var i = 0; i < Operations; i++)
            {
                var msg = _codec.DecodeMessage(_pduDecoded, _rarp, _addr1);
            }
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void DeserializePayloadOnly()
        {
            for (var i = 0; i < Operations; i++)
            {
                var deserialize = MessageSerializer.Deserialize(_sys1, _payloadDecoded);
            }
        }

        private ByteString CreatePayloadPdu()
        {
            return _codec.ConstructPayload(_codec.ConstructMessage(_addr1, _receiveRef,
                MessageSerializer.Serialize(_sys1, _addr1, _message), _senderActorRef, null, _lastAck));
        }
    }
}