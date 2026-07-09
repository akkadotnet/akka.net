//-----------------------------------------------------------------------
// <copyright file="ArteryCompressionDispatchSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Artery;
using Akka.Remote.Artery.Compression;
using Akka.TestKit;
using Akka.TestKit.Extensions;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// SENDER-side dispatch of the compression advertisement protocol (design.md
    /// "artery-ref-manifest-compression", Stage 2a item 3). When system A receives an
    /// <see cref="ActorRefCompressionAdvertisement"/> from a peer B over the control stream and
    /// compression is ENABLED, A builds the outbound table from the advertised ordered list, installs
    /// it as A's OUTBOUND table for B, and replies with an
    /// <see cref="ActorRefCompressionAdvertisementAck"/>. When compression is OFF the advertisement is
    /// IGNORED -- no table installed, no Ack (the hard off-by-default invariant).
    ///
    /// <para>
    /// The receiver-side machinery (observation, table build/advertise, rotation, Ack confirmation) is
    /// Stage 2b -- here the advertisement is injected by having B send it directly over the real
    /// control stream, with no receiver-side generation involved.
    /// </para>
    /// </summary>
    public sealed class ArteryCompressionDispatchSpec : AkkaSpec
    {
        public ArteryCompressionDispatchSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(bool compressionEnabled) =>
            ConfigurationFactory.ParseString($"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.compression.enabled = {(compressionEnabled ? "on" : "off")}
                """);

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;

        private static int BoundPort(ActorSystem system) => AddressOf(system).Port!.Value;

        private static string EchoSelectionPath(ActorSystem system, string localName) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{localName}";

        private static ArteryRemoting TransportFor(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(msg => Sender.Tell(msg));
        }

        /// <summary>Forwards ONLY compression Ack control messages to a probe (filters out heartbeats etc.).</summary>
        private sealed class CompressionAckSubscriber : IControlMessageSubscriber
        {
            private readonly IActorRef _probe;
            public CompressionAckSubscriber(IActorRef probe) => _probe = probe;

            public void ControlMessageReceived(long originUid, object message)
            {
                if (message is ActorRefCompressionAdvertisementAck or ClassManifestCompressionAdvertisementAck)
                    _probe.Tell(message);
            }
        }

        private async Task WarmUpAssociationAsync(ActorSystem systemA, ActorSystem systemB)
        {
            systemB.ActorOf(Props.Create(() => new Echo()), "echo");

            // A real ordinary round trip establishes the handshake (and therefore both systems'
            // control connections) both ways -- see design.md "Connection cardinality".
            var echoRef = await systemA.ActorSelection(EchoSelectionPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
            var warmup = CreateTestProbe(systemA);
            echoRef.Tell("warmup", warmup.Ref);
            await warmup.ExpectMsgAsync("warmup", TimeSpan.FromSeconds(10));
        }

        [Fact(DisplayName = "Compression enabled: receiving an actor-ref advertisement installs the outbound table and replies with an Ack")]
        public async Task Should_install_table_and_ack_when_enabled()
        {
            var systemA = ActorSystem.Create("ArteryCompressA", ArteryConfig(compressionEnabled: true));
            var systemB = ActorSystem.Create("ArteryCompressB", ArteryConfig(compressionEnabled: true));
            try
            {
                await WarmUpAssociationAsync(systemA, systemB);

                var aAddress = AddressOf(systemA);
                var bAddress = AddressOf(systemB);

                // Observe (on B) the Ack that A sends back after installing the table.
                var ackProbe = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new CompressionAckSubscriber(ackProbe.Ref));

                // Two target paths A would send to B; index 0 and 1 in the advertised table.
                var pathOne = $"akka://{systemA.Name}@127.0.0.1:{BoundPort(systemA)}/user/one";
                var pathTwo = $"akka://{systemA.Name}@127.0.0.1:{BoundPort(systemA)}/user/two";
                var advertisement = new ActorRefCompressionAdvertisement(
                    From: new UniqueAddress(bAddress, 1L),   // B (the advertiser); From is not consumed by 2a dispatch
                    OriginUid: 4242L,                        // "the system that will use the table" (informational in 2a)
                    TableVersion: 5,
                    Entries: new CompressionAdvertisementTable(new[] { pathOne, pathTwo }));

                // B sends the advertisement to A over the real control stream (the envelope carries B's
                // real uid, which is how A keys the install onto its association to B).
                TransportFor(systemB).SendControlToAddress(aAddress, advertisement);

                // A replies with an Ack: From = A's own address, version echoed.
                var ack = await ackProbe.ExpectMsgAsync<ActorRefCompressionAdvertisementAck>(TimeSpan.FromSeconds(10));
                ack.From.Address.Should().Be(aAddress);
                ack.TableVersion.Should().Be((byte)5);

                // A installed the outbound actor-ref table for B (position == index).
                await AwaitAssertAsync(() =>
                {
                    var table = TransportFor(systemA).OutboundActorRefCompressionTableFor(bAddress);
                    table.Version.Should().Be((byte)5);
                    table.Compress(pathOne).Should().Be(0);
                    table.Compress(pathTwo).Should().Be(1);
                }, TimeSpan.FromSeconds(5));

                // The manifest table was NOT touched by an actor-ref advertisement.
                TransportFor(systemA).OutboundManifestCompressionTableFor(bAddress).Dictionary.Should().BeEmpty();
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Compression enabled: a class-manifest advertisement installs the manifest table and acks")]
        public async Task Should_install_manifest_table_and_ack_when_enabled()
        {
            var systemA = ActorSystem.Create("ArteryCompressManifestA", ArteryConfig(compressionEnabled: true));
            var systemB = ActorSystem.Create("ArteryCompressManifestB", ArteryConfig(compressionEnabled: true));
            try
            {
                await WarmUpAssociationAsync(systemA, systemB);

                var aAddress = AddressOf(systemA);
                var bAddress = AddressOf(systemB);

                var ackProbe = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new CompressionAckSubscriber(ackProbe.Ref));

                var advertisement = new ClassManifestCompressionAdvertisement(
                    From: new UniqueAddress(bAddress, 1L),
                    OriginUid: 4242L,
                    TableVersion: 9,
                    Entries: new CompressionAdvertisementTable(new[] { "My.Manifest, Asm" }));

                TransportFor(systemB).SendControlToAddress(aAddress, advertisement);

                var ack = await ackProbe.ExpectMsgAsync<ClassManifestCompressionAdvertisementAck>(TimeSpan.FromSeconds(10));
                ack.TableVersion.Should().Be((byte)9);

                await AwaitAssertAsync(() =>
                {
                    var table = TransportFor(systemA).OutboundManifestCompressionTableFor(bAddress);
                    table.Version.Should().Be((byte)9);
                    table.Compress("My.Manifest, Asm").Should().Be(0);
                }, TimeSpan.FromSeconds(5));
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Compression OFF: an advertisement is IGNORED -- no table installed, no Ack")]
        public async Task Should_ignore_advertisement_when_disabled()
        {
            var systemA = ActorSystem.Create("ArteryCompressOffA", ArteryConfig(compressionEnabled: false));
            var systemB = ActorSystem.Create("ArteryCompressOffB", ArteryConfig(compressionEnabled: false));
            try
            {
                await WarmUpAssociationAsync(systemA, systemB);

                var aAddress = AddressOf(systemA);
                var bAddress = AddressOf(systemB);

                var ackProbe = CreateTestProbe(systemB);
                TransportFor(systemB).SubscribeControl(new CompressionAckSubscriber(ackProbe.Ref));

                var pathOne = $"akka://{systemA.Name}@127.0.0.1:{BoundPort(systemA)}/user/one";
                var advertisement = new ActorRefCompressionAdvertisement(
                    From: new UniqueAddress(bAddress, 1L),
                    OriginUid: 4242L,
                    TableVersion: 5,
                    Entries: new CompressionAdvertisementTable(new[] { pathOne }));

                TransportFor(systemB).SendControlToAddress(aAddress, advertisement);

                // No Ack ever comes back (the filtering subscriber forwards only Acks, so heartbeats
                // do not create false positives).
                await ackProbe.ExpectNoMsgAsync(TimeSpan.FromSeconds(2));

                // And A's outbound table stays Empty (nothing compressed -> byte-identical LITERAL).
                var table = TransportFor(systemA).OutboundActorRefCompressionTableFor(bAddress);
                table.Dictionary.Should().BeEmpty();
                table.Compress(pathOne).Should().Be(CompressionTable<string>.NotCompressedId);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
