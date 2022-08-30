// //-----------------------------------------------------------------------
// // <copyright file="AkkaPduCodecCacheSpecs.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Configuration;
using FluentAssertions;
using Akka.Remote.Serialization;
using Akka.Remote.Transport;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Remote.Tests.Serialization
{
    /// <summary>
    /// Validates that the <see cref="AkkaPduCodec"/> and the <see cref="RemoteActorRefProvider"/>
    /// cache actor paths, addresses, and resolved <see cref="IActorRef"/>s correctly.
    /// </summary>
    public class AkkaPduCodecCacheSpecs : AkkaSpec
    {
        private static readonly Config Config = @"
                                   akka.loglevel = ERROR
                                   akka.actor.provider = remote
                                   akka.remote.dot-netty.tcp.port = 0";
        
        public AkkaPduCodecCacheSpecs(ITestOutputHelper output) : base(Config, output)
        {
            _sys2 = (ExtendedActorSystem)ActorSystem.Create("Sys2", Sys.Settings.Config);
            InitializeLogger(_sys2);
        }
        
        private readonly ExtendedActorSystem _sys2;
        private IActorRef _senderActorRef;
        private IActorRef _localReceiveRef;
        private RemoteActorRef _remoteReceiveRef;
        private RemoteActorRef _remoteSenderRef;
        private Address _addr1;
        private Address _addr2;
        private AkkaPduProtobuffCodec _recvCodec;
        private AkkaPduProtobuffCodec _sendCodec;
        
        private IRemoteActorRefProvider _rarp;
        
        /// <summary>
        /// The message we're going to serialize
        /// </summary>
        private readonly object _message = "foobar";

        private readonly Ack _lastAck = new Ack(-1);

        public override async Task InitializeAsync()
        {
            _senderActorRef =
                _sys2.ActorOf(act => { act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender)); },
                    "sender1");

            _localReceiveRef = Sys.ActorOf(act => { act.ReceiveAny((o, context) => context.Sender.Tell(context.Sender)); },
                "recv1");

            // create an association
            _remoteReceiveRef = (RemoteActorRef)(await _sys2.ActorSelection(new RootActorPath(RARP.For(Sys).Provider.DefaultAddress) / "user" /
                                                                            _localReceiveRef.Path.Name).ResolveOne(TimeSpan.FromSeconds(3)));
            
            _remoteSenderRef = (RemoteActorRef)(await Sys.ActorSelection(new RootActorPath(RARP.For(_sys2).Provider.DefaultAddress) / "user" /
                                                                           _senderActorRef.Path.Name).ResolveOne(TimeSpan.FromSeconds(3)));

            _recvCodec = new AkkaPduProtobuffCodec(Sys);
            _sendCodec = new AkkaPduProtobuffCodec(_sys2);
            
            _rarp = RARP.For(Sys).Provider;
            _addr1 = _rarp.DefaultAddress;
            _addr2 = RARP.For(_sys2).Provider.DefaultAddress;
            
            await base.InitializeAsync();
        }

        protected override async Task AfterAllAsync()
        {
            await ShutdownAsync(_sys2);
            await base.AfterAllAsync();
        }
        
        private Google.Protobuf.ByteString CreatePayloadPdu()
        {
            return _sendCodec.ConstructPayload(_sendCodec.ConstructMessage(_remoteReceiveRef.LocalAddressToUse, _remoteReceiveRef,
                MessageSerializer.Serialize(_sys2, _addr2, _message), _senderActorRef, null, _lastAck));
        }

        [Fact(DisplayName = "Skip - disabled.")]
        public void AkkaPduCodecShouldCacheAppropriateRefs()
        {
            // arrange - "send" from Sys2
            var payload = CreatePayloadPdu();

            // act - "receive" from Sys1
            var pdu = _recvCodec.DecodePdu(payload);
            if (pdu is Payload p)
            {
                var msg = _recvCodec.DecodeMessage(p.Bytes, _rarp, _addr1);
                var deserialize = MessageSerializer.Deserialize((ExtendedActorSystem)Sys, msg.MessageOption.SerializedMessage);
            }
            
            void AssertCacheStats(ActorSystem sys)
            {
                var resolveCache = ActorRefResolveThreadLocalCache.For(sys);
                var pathCache = ActorPathThreadLocalCache.For(sys);
                var addressCache = AddressThreadLocalCache.For(sys);

                resolveCache.Cache.Stats.Entries.Should().Be(2); // 2 receiver (recipient, sender)
                pathCache.Cache.Stats.Entries.Should().Be(4); // 2 sender, 2 receiver
                addressCache.Cache.Stats.Entries.Should().Be(1); // sending system's address
            }
            
            // assert
            AssertCacheStats(Sys);
        }
        
    }
}