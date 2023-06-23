//-----------------------------------------------------------------------
// <copyright file="AkkaPduCodecBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Serialization;
using Akka.Remote.Transport;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Loggers;
using Google.Protobuf;

namespace Akka.Benchmarks.Remoting
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class AkkaPduCodecBenchmark
    {
        public const int Operations = 10_000;

        private ExtendedActorSystem _sys1;
        private ExtendedActorSystem _sys2;
        private IRemoteActorRefProvider _rarp;

        private Config _config = @"akka.actor.provider = remote
                                     akka.remote.dot-netty.tcp.port = 0";

        private IActorRef _senderActorRef;
        private IActorRef _localReceiveRef;
        private RemoteActorRef _remoteReceiveRef;
        private RemoteActorRef _remoteSenderRef;

        private Address _addr1;
        private Address _addr2;
        private AkkaPduProtobuffCodec _recvCodec;
        private AkkaPduProtobuffCodec _sendCodec;

        /// <summary>
        /// The message we're going to serialize
        /// </summary>
        private readonly object _message = "foobar";

        private readonly Ack _lastAck = new(-1);

        private ByteString _fullDecode;
        private ByteString _pduDecoded;
        private Akka.Remote.Serialization.Proto.Msg.Payload _payloadDecoded;

        [GlobalSetup]
        public async Task Setup()
        {
            _sys1 = (ExtendedActorSystem)ActorSystem.Create("BenchSys", _config);
            _sys2 = (ExtendedActorSystem)ActorSystem.Create("BenchSys", _config);
            _rarp = RARP.For(_sys1).Provider;
            _addr1 = _rarp.DefaultAddress;
            _addr2 = RARP.For(_sys2).Provider.DefaultAddress;

            _senderActorRef =
                _sys2.ActorOf(act => { act.ReceiveAny((_, context) => context.Sender.Tell(context.Sender)); },
                    "sender1");

            _localReceiveRef = _sys1.ActorOf(act => { act.ReceiveAny((_, context) => context.Sender.Tell(context.Sender)); },
                "recv1");

            // create an association
            _remoteReceiveRef = (RemoteActorRef)(await _sys2.ActorSelection(new RootActorPath(RARP.For(_sys1).Provider.DefaultAddress) / "user" /
                                 _localReceiveRef.Path.Name).ResolveOne(TimeSpan.FromSeconds(3)));
            
            _remoteSenderRef = (RemoteActorRef)(await _sys1.ActorSelection(new RootActorPath(RARP.For(_sys2).Provider.DefaultAddress) / "user" /
                                                          _senderActorRef.Path.Name).ResolveOne(TimeSpan.FromSeconds(3)));

            _recvCodec = new AkkaPduProtobuffCodec(_sys1);
            _sendCodec = new AkkaPduProtobuffCodec(_sys2);
            _fullDecode = CreatePayloadPdu();
            _pduDecoded = ((Payload)_recvCodec.DecodePdu(_fullDecode)).Bytes;
            _payloadDecoded = _recvCodec.DecodeMessage(_pduDecoded, _rarp, _addr1).MessageOption.SerializedMessage;
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {

            void PrintCacheStats(string prefix, ActorSystem sys)
            {
                var resolveCache = ActorRefResolveThreadLocalCache.For(sys);
                var pathCache = ActorPathThreadLocalCache.For(sys);
                var addressCache = AddressThreadLocalCache.For(sys);

                ConsoleLogger.Default.WriteLine(LogKind.Result,
                    $"[{prefix}] ResolveCache entries: [{resolveCache.Cache.Stats.Entries}]");
                ConsoleLogger.Default.WriteLine(LogKind.Result,
                    $"[{prefix}] PathCache entries: [{pathCache.Cache.Stats.Entries}]");
                ConsoleLogger.Default.WriteLine(LogKind.Result,
                    $"[{prefix}] AddressCache entries: [{addressCache.Cache.Stats.Entries}]");
            }
                
            PrintCacheStats("Addr1", _sys1);
            PrintCacheStats("Addr2", _sys2);
            
            var resolveCache = ActorRefResolveThreadLocalCache.For(_sys1);
            var pathCache = ActorPathThreadLocalCache.For(_sys1);
            var addressCache = AddressThreadLocalCache.For(_sys1);

            var senderResolveCache = ActorRefResolveThreadLocalCache.For(_sys2);
            var senderPathCache = ActorPathThreadLocalCache.For(_sys2);
            var senderAddressCache = AddressThreadLocalCache.For(_sys2);

            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr1] Used ResolveCache for recipient? {resolveCache.Cache.TryGet(_remoteReceiveRef.Path.ToSerializationFormat(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr1] Used PathCache for recipient? {pathCache.Cache.TryGet(_remoteReceiveRef.Path.ToSerializationFormat(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr1] Used ResolveCache for sender? {resolveCache.Cache.TryGet(_remoteSenderRef.Path.ToSerializationFormat(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr1] Used PathCache for sender? {pathCache.Cache.TryGet(_remoteSenderRef.Path.ToSerializationFormat(), out _)}");
            
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr2] Used ResolveCache for recipient? {senderResolveCache.Cache.TryGet(_remoteReceiveRef.Path.ToSerializationFormat(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr2] Used PathCache for recipient? {senderPathCache.Cache.TryGet(_remoteReceiveRef.Path.ToSerializationFormat(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr2] Used ResolveCache for sender? {senderResolveCache.Cache.TryGet(_senderActorRef.Path.ToSerializationFormat(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr2] Used PathCache for sender? {senderPathCache.Cache.TryGet(_senderActorRef.Path.ToSerializationFormat(), out _)}");
            
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr1] Used AddressCache for sys1? {addressCache.Cache.TryGet(_addr2.ToString(), out _)}");
            ConsoleLogger.Default.WriteLine(LogKind.Result, $"[Addr2] Used AddressCache for sys2? {senderAddressCache.Cache.TryGet(_addr1.ToString(), out _)}");

            await Task.WhenAll(_sys1.Terminate(), _sys2.Terminate());
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
                var pdu = _recvCodec.DecodePdu(_fullDecode);
                if (pdu is Payload p)
                {
                    var msg = _recvCodec.DecodeMessage(p.Bytes, _rarp, _addr1);
                    var deserialize = MessageSerializer.Deserialize(_sys1, msg.MessageOption.SerializedMessage);
                }
            }
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void DecodePduOnly()
        {
            for (var i = 0; i < Operations; i++)
            {
                var pdu = _recvCodec.DecodePdu(_fullDecode);
            }
        }

        [Benchmark(OperationsPerInvoke = Operations)]
        public void DecodeMessageOnly()
        {
            for (var i = 0; i < Operations; i++)
            {
                var msg = _recvCodec.DecodeMessage(_pduDecoded, _rarp, _addr1);
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
            return _sendCodec.ConstructPayload(_sendCodec.ConstructMessage(_remoteReceiveRef.LocalAddressToUse, _remoteReceiveRef,
                MessageSerializer.Serialize(_sys2, _addr2, _message), _senderActorRef, null, _lastAck));
        }
    }
}
