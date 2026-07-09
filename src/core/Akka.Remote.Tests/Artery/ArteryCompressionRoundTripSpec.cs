//-----------------------------------------------------------------------
// <copyright file="ArteryCompressionRoundTripSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
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
    /// End-to-end, two-ActorSystem verification of the FULL receiver-driven compression loop
    /// (design.md "artery-ref-manifest-compression", Stage 2b-ii): B observes A's inbound traffic,
    /// advertises a table back to A, A installs it and starts stamping COMPRESSED tags, and B resolves
    /// them -- all with correct end-to-end delivery. The complementary hard invariant is that with
    /// compression OFF the wire is byte-identical: no advertisement, no table, no compression event.
    ///
    /// <para>
    /// The observable "table advertised/installed/in-use" signals (design.md Q7) are: A's installed
    /// OUTBOUND table (via the <c>OutboundActorRefCompressionTableFor</c> transport seam) proves
    /// advertise+install; a RECEIVER-side <see cref="ArteryInboundCompressionEvent"/> with phase
    /// <see cref="ArteryInboundCompressionPhase.Resolved"/> on B's EventStream proves a COMPRESSED tag
    /// actually crossed the wire and decoded.
    /// </para>
    /// </summary>
    public sealed class ArteryCompressionRoundTripSpec : AkkaSpec
    {
        public ArteryCompressionRoundTripSpec(ITestOutputHelper output) : base(output)
        {
        }

        private static Config ArteryConfig(bool compressionEnabled) =>
            ConfigurationFactory.ParseString($$"""
                akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"
                akka.remote.artery.enabled = on
                akka.remote.artery.canonical.hostname = "127.0.0.1"
                akka.remote.artery.canonical.port = 0
                akka.remote.artery.advanced.compression.enabled = {{(compressionEnabled ? "on" : "off")}}
                # Fast advertisement so the loop closes within a test-friendly window (default is 1 minute).
                akka.remote.artery.advanced.compression.advertisement-interval = 200ms
                """);

        private static Address AddressOf(ActorSystem system) => RARP.For(system).Provider.DefaultAddress;
        private static int BoundPort(ActorSystem system) => AddressOf(system).Port!.Value;
        private static string EchoPath(ActorSystem system, string name) =>
            $"akka://{system.Name}@127.0.0.1:{BoundPort(system)}/user/{name}";
        private static ArteryRemoting TransportFor(ActorSystem system) => (ArteryRemoting)RARP.For(system).Provider.Transport;

        private sealed class Echo : ReceiveActor
        {
            public Echo() => ReceiveAny(msg => Sender.Tell(msg));
        }

        [Fact(DisplayName = "Round trip: B advertises, A installs, subsequent frames carry COMPRESSED tags that B resolves, with correct delivery")]
        public async Task Should_complete_advertise_install_resolve_loop()
        {
            var systemA = ActorSystem.Create("ArteryRoundTripA", ArteryConfig(compressionEnabled: true));
            var systemB = ActorSystem.Create("ArteryRoundTripB", ArteryConfig(compressionEnabled: true));
            var cts = new CancellationTokenSource();
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var bAddress = AddressOf(systemB);

                // Observe (on B) every receiver-side compression lifecycle event.
                var compressionEvents = CreateTestProbe(systemB, "compression-events");
                systemB.EventStream.Subscribe(compressionEvents.Ref, typeof(ArteryInboundCompressionEvent));

                var echoRef = await systemA.ActorSelection(EchoPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var sender = CreateTestProbe(systemA, "ping-sender");

                // A real actor ref (the probe) as sender + a real recipient (echo): both are compressible
                // heavy hitters B will observe and advertise back. Keep a steady stream of pings flowing so
                // that (a) B accumulates heavy hitters, (b) A -- once it installs the advertised table --
                // has traffic to stamp COMPRESSED, and (c) delivery is continuously exercised.
                var pinger = Task.Run(async () =>
                {
                    var i = 0;
                    while (!cts.IsCancellationRequested)
                    {
                        echoRef.Tell($"ping-{i++}", sender.Ref);
                        try { await Task.Delay(50, cts.Token); } catch (TaskCanceledException) { return; }
                    }
                });

                // Delivery works from the very first exchange (LITERAL, pre-advertisement).
                await sender.ExpectMsgAsync<string>(m => m.StartsWith("ping-"), TimeSpan.FromSeconds(10));

                // SIGNAL 1 (advertise + install): A installs the OUTBOUND actor-ref table B advertised.
                await AwaitAssertAsync(() =>
                {
                    TransportFor(systemA).OutboundActorRefCompressionTableFor(bAddress)
                        .Dictionary.Should().NotBeEmpty("B should have advertised an actor-ref table and A installed it");
                }, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(200));

                // SIGNAL 2 (in use on the wire): A now stamps COMPRESSED tags and B resolves them. The
                // Resolved event proves a COMPRESSED tag crossed the wire and decoded end-to-end.
                var resolved = (ArteryInboundCompressionEvent)await compressionEvents.FishForMessageAsync(
                    m => m is ArteryInboundCompressionEvent { Phase: ArteryInboundCompressionPhase.Resolved, IsManifest: false },
                    TimeSpan.FromSeconds(20));
                resolved.Version.Should().BeGreaterThan(0);
                resolved.OriginUid.Should().NotBe(0, "the resolved frame carries the sending system's (A's) origin UID");

                // Delivery still correct AFTER compression is active (COMPRESSED frames decode + dispatch).
                await sender.ExpectMsgAsync<string>(m => m.StartsWith("ping-"), TimeSpan.FromSeconds(10));

                // The installed table carries the version B resolved against -- a real, non-empty,
                // in-use compression table (its keys are serialization-format paths incl. the #uid
                // fragment, so we assert on the version/size rather than reconstructing a key string).
                var installed = TransportFor(systemA).OutboundActorRefCompressionTableFor(bAddress);
                installed.Dictionary.Should().NotBeEmpty();
                installed.Version.Should().BeGreaterThan(0);
            }
            finally
            {
                cts.Cancel();
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }

        [Fact(DisplayName = "Compression OFF: no advertisement, no table, no compression event -- byte-identical to a no-compression build")]
        public async Task Should_stay_byte_identical_when_disabled()
        {
            var systemA = ActorSystem.Create("ArteryRoundTripOffA", ArteryConfig(compressionEnabled: false));
            var systemB = ActorSystem.Create("ArteryRoundTripOffB", ArteryConfig(compressionEnabled: false));
            try
            {
                systemB.ActorOf(Props.Create(() => new Echo()), "echo");
                var bAddress = AddressOf(systemB);

                var compressionEvents = CreateTestProbe(systemB, "compression-events");
                systemB.EventStream.Subscribe(compressionEvents.Ref, typeof(ArteryInboundCompressionEvent));

                var echoRef = await systemA.ActorSelection(EchoPath(systemB, "echo")).ResolveOne(TimeSpan.FromSeconds(10));
                var sender = CreateTestProbe(systemA, "ping-sender");

                // Send a sustained burst (more than one advertisement interval's worth) so that, WERE the
                // machinery active, it would certainly have advertised + resolved by now.
                for (var i = 0; i < 40; i++)
                {
                    echoRef.Tell($"ping-{i}", sender.Ref);
                    await sender.ExpectMsgAsync<string>(m => m.StartsWith("ping-"), TimeSpan.FromSeconds(10));
                }

                // No compression event EVER fires (no observation, no advertisement, no resolution).
                await compressionEvents.ExpectNoMsgAsync(TimeSpan.FromSeconds(2));

                // A's outbound table stays Empty -> every tag on the wire is LITERAL, byte-identical.
                var table = TransportFor(systemA).OutboundActorRefCompressionTableFor(bAddress);
                table.Dictionary.Should().BeEmpty();
                table.Compress(EchoPath(systemB, "echo")).Should().Be(CompressionTable<string>.NotCompressedId);
            }
            finally
            {
                await systemA.Terminate().AwaitWithTimeout(10.Seconds());
                await systemB.Terminate().AwaitWithTimeout(10.Seconds());
            }
        }
    }
}
