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

namespace Akka.Benchmarks.Remoting
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class AkkaPduCodecBenchmark
    {
        public const int Operations = 10_000;
        
        private ExtendedActorSystem _sys1;
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

        [GlobalSetup]
        public async Task Setup()
        {
            _sys1 = (ExtendedActorSystem)ActorSystem.Create("BenchSys", _config);

            var es = (ExtendedActorSystem)_sys1;
            _addr1 = es.Provider.DefaultAddress;
            
            _senderActorRef = _sys1.ActorOf(act =>
            {
                act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender));
            }, "sender1");
            
            _receiveRef = _sys1.ActorOf(act =>
            {
                act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender));
            }, "recv1");

            _codec = new AkkaPduProtobuffCodec(_sys1);
        }
        
        [GlobalCleanup]
        public async Task Cleanup()
        {
            await _sys1.Terminate();
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void WritePayloadPdu()
        {
            for (var i = 0; i < Operations; i++)
            {
                var pdu = _codec.ConstructPayload(_codec.ConstructMessage(_addr1, _receiveRef,
                    MessageSerializer.Serialize(_sys1, _addr1, _message), _senderActorRef, null, _lastAck));
            }
        }
    }
}