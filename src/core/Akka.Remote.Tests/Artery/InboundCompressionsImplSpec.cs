//-----------------------------------------------------------------------
// <copyright file="InboundCompressionsImplSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery;
using Akka.Remote.Artery.Compression;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Deterministic coordinator-level tests for the RECEIVER-side inbound compression coordinator
    /// (<see cref="InboundCompressionsImpl"/>, design.md "artery-ref-manifest-compression" Stage 2b-ii).
    /// These drive the coordinator directly against a fake <see cref="IInboundCompressionContext"/> that
    /// captures every advertisement sent and every observability event published -- giving exact,
    /// non-racy control over the Ack timing and table-version state that a two-system test cannot
    /// provide without production test-hooks or wall-clock timing. The pure per-origin rotation itself is
    /// exhaustively covered by <see cref="InboundCompressionSpec"/>; here we cover the coordinator's
    /// glue: observe -&gt; advertise, the two confirmation triggers, the resend/give-up lifecycle, the
    /// unchanged-table (`alive`) gate, and the restarted-incarnation drop-and-re-advertise edge.
    /// </summary>
    public class InboundCompressionsImplSpec
    {
        private const long OriginUid = 0x0BAD_F00D_1234_5678L;

        private static string Ref(string name) => $"akka://Remote@127.0.0.1:2552/user/{name}";

        /// <summary>Fake transport seam: always advertisable (unless overridden), captures sends + events.</summary>
        private sealed class FakeContext : IInboundCompressionContext
        {
            public FakeContext(UniqueAddress local) => LocalAddress = local;

            public UniqueAddress LocalAddress { get; }
            public Func<long, Address?> Resolver { get; set; } = _ => new Address("akka", "Remote", "127.0.0.1", 2552);
            public List<(Address To, object Message)> Sent { get; } = new();
            public List<ArteryInboundCompressionEvent> Events { get; } = new();

            public Address? ResolveAdvertisableOrigin(long originUid) => Resolver(originUid);
            public void SendControl(Address to, object message) => Sent.Add((to, message));
            public void PublishEvent(object evt) => Events.Add((ArteryInboundCompressionEvent)evt);
            public void SubscribeControl(IControlMessageSubscriber subscriber) { }
            public void UnsubscribeControl(IControlMessageSubscriber subscriber) { }
        }

        private static (InboundCompressionsImpl impl, FakeContext ctx) NewImpl(int refsMax = 256, int manifestsMax = 256)
        {
            var ctx = new FakeContext(new UniqueAddress(new Address("akka", "Local", "127.0.0.1", 1), 99L));
            var impl = new InboundCompressionsImpl(ctx, refsMax, manifestsMax, NoLogger.Instance);
            return (impl, ctx);
        }

        private static ActorRefCompressionAdvertisement LastRefAdvertisement(FakeContext ctx) =>
            ctx.Sent.Select(s => s.Message).OfType<ActorRefCompressionAdvertisement>().Last();

        [Fact(DisplayName = "Observe then advertise: hits become a versioned actor-ref advertisement with dense entries + Advertised event")]
        public void Should_build_and_advertise_from_hits()
        {
            var (impl, ctx) = NewImpl();

            impl.HitActorRef(OriginUid, Ref("alpha"), 3);
            impl.HitActorRef(OriginUid, Ref("beta"), 1);

            impl.RunNextActorRefAdvertisement();

            ctx.Sent.Should().HaveCount(1);
            var adv = LastRefAdvertisement(ctx);
            adv.From.Should().Be(ctx.LocalAddress);
            adv.OriginUid.Should().Be(OriginUid);
            adv.TableVersion.Should().Be(1);
            adv.Table.Should().BeEquivalentTo(new[] { Ref("alpha"), Ref("beta") });

            // The advertised entries form a dense 0..N-1 table the SENDER can install and invert.
            var outbound = CompressionTable<string>.FromAdvertisement(adv.OriginUid, adv.TableVersion, adv.Table);
            outbound.Compress(Ref("alpha")).Should().BeGreaterOrEqualTo(0);
            outbound.Compress(Ref("beta")).Should().BeGreaterOrEqualTo(0);

            ctx.Events.Should().ContainSingle(e =>
                e.Phase == ArteryInboundCompressionPhase.Advertised && e.Version == 1 && !e.IsManifest && e.EntryCount == 2);
        }

        [Fact(DisplayName = "Unchanged table (alive gate): confirmed table is not re-advertised until a NEW hit arrives")]
        public void Should_not_readvertise_unchanged_table()
        {
            var (impl, ctx) = NewImpl();

            impl.HitActorRef(OriginUid, Ref("alpha"), 1);
            impl.RunNextActorRefAdvertisement();           // advertise v1 (now in progress)
            impl.ConfirmActorRefAdvertisement(OriginUid, 1); // Ack -> active v1, nothing in progress
            var sentAfterConfirm = ctx.Sent.Count;

            // No new observation since the advertisement -> `alive` is false -> no re-advertisement.
            impl.RunNextActorRefAdvertisement();
            ctx.Sent.Count.Should().Be(sentAfterConfirm, "an unchanged table must not be re-advertised");

            // A new heavy hitter re-arms `alive` -> the next pass advertises v2 including it.
            impl.HitActorRef(OriginUid, Ref("gamma"), 5);
            impl.RunNextActorRefAdvertisement();
            var adv = LastRefAdvertisement(ctx);
            adv.TableVersion.Should().Be(2);
            adv.Table.Should().Contain(Ref("gamma"));
        }

        [Fact(DisplayName = "Ack-loss -> trigger #2: without any Ack, the first COMPRESSED frame at the advertised version activates + resolves")]
        public void Should_activate_on_first_stamped_message_when_ack_lost()
        {
            var (impl, ctx) = NewImpl();

            impl.HitActorRef(OriginUid, Ref("alpha"), 1);
            impl.HitActorRef(OriginUid, Ref("beta"), 1);
            impl.RunNextActorRefAdvertisement(); // advertise v1, IN PROGRESS -- deliberately NEVER confirmed
            var adv = LastRefAdvertisement(ctx);
            adv.TableVersion.Should().Be(1);
            var idxAlpha = adv.Table.ToList().IndexOf(Ref("alpha"));

            ctx.Events.Any(e => e.Phase == ArteryInboundCompressionPhase.Activated).Should().BeFalse(
                "the advertisement is not confirmed yet");

            // First inbound message stamped with v1 (the Ack never arrived): must flip to it + resolve.
            var hit = impl.TryDecompressActorRef(OriginUid, tableVersion: 1, idx: idxAlpha, out var resolved);
            hit.Should().BeTrue();
            resolved.Should().Be(Ref("alpha"));

            ctx.Events.Should().Contain(e => e.Phase == ArteryInboundCompressionPhase.Activated && e.Version == 1 && !e.IsManifest);
            ctx.Events.Should().Contain(e => e.Phase == ArteryInboundCompressionPhase.Resolved && e.Version == 1 && !e.IsManifest);
        }

        [Fact(DisplayName = "Restarted incarnation: an unknown/greater version is dropped (miss, never throws) and a fresh table is re-advertised")]
        public void Should_drop_unknown_version_and_readvertise()
        {
            var (impl, ctx) = NewImpl();

            impl.HitActorRef(OriginUid, Ref("alpha"), 1);
            impl.RunNextActorRefAdvertisement();
            impl.ConfirmActorRefAdvertisement(OriginUid, 1); // active v1

            // A frame stamped with a version this system never built (as if from a table built for a
            // PREVIOUS incarnation of this system): a clean MISS, never an exception -> caller drops it.
            Action decode = () => impl.TryDecompressActorRef(OriginUid, tableVersion: 42, idx: 0, out _);
            decode.Should().NotThrow();
            impl.TryDecompressActorRef(OriginUid, tableVersion: 42, idx: 0, out _).Should().BeFalse();

            // The coordinator remains healthy: fresh observations produce a fresh advertisement (v2).
            var beforeCount = ctx.Sent.Count;
            impl.HitActorRef(OriginUid, Ref("delta"), 9);
            impl.RunNextActorRefAdvertisement();
            ctx.Sent.Count.Should().BeGreaterThan(beforeCount);
            LastRefAdvertisement(ctx).TableVersion.Should().Be(2);
            LastRefAdvertisement(ctx).Table.Should().Contain(Ref("delta"));
        }

        [Fact(DisplayName = "Resend then give up: an unconfirmed advertisement is resent MaxResendCount times, then flipped in anyway")]
        public void Should_resend_then_give_up()
        {
            var (impl, ctx) = NewImpl();

            impl.HitActorRef(OriginUid, Ref("alpha"), 1);

            impl.RunNextActorRefAdvertisement(); // initial send (v1)
            for (var i = 0; i < InboundCompression.MaxResendCount; i++)
                impl.RunNextActorRefAdvertisement(); // 3 resends

            var refAdvertisements = ctx.Sent.Select(s => s.Message).OfType<ActorRefCompressionAdvertisement>().ToList();
            refAdvertisements.Should().HaveCount(1 + InboundCompression.MaxResendCount, "1 initial + MaxResendCount resends");
            refAdvertisements.Should().OnlyContain(a => a.TableVersion == 1);

            // One more pass exhausts the budget -> give up (flip in anyway) -> no further send, Activated fires.
            impl.RunNextActorRefAdvertisement();
            ctx.Sent.Select(s => s.Message).OfType<ActorRefCompressionAdvertisement>().Should()
                .HaveCount(1 + InboundCompression.MaxResendCount, "give-up sends nothing further");
            ctx.Events.Should().Contain(e => e.Phase == ArteryInboundCompressionPhase.Activated && e.Version == 1);
        }

        [Fact(DisplayName = "No association: an unresolvable origin is dropped from tracking (no advertisement)")]
        public void Should_close_origin_when_not_advertisable()
        {
            var (impl, ctx) = NewImpl();
            ctx.Resolver = _ => null; // origin never resolves to a (non-quarantined) association

            impl.HitActorRef(OriginUid, Ref("alpha"), 1);
            impl.CurrentOriginUids.Should().Contain(OriginUid);

            impl.RunNextActorRefAdvertisement();

            ctx.Sent.Should().BeEmpty("nothing is advertised to an unresolvable origin");
            impl.CurrentOriginUids.Should().NotContain(OriginUid, "the origin's state is dropped (Pekko close-on-no-association)");
        }

        [Fact(DisplayName = "Manifest advertisement is independent of the actor-ref advertisement")]
        public void Should_advertise_manifests_independently()
        {
            var (impl, ctx) = NewImpl();

            impl.HitClassManifest(OriginUid, "My.Manifest, Asm", 4);
            impl.RunNextClassManifestAdvertisement();

            var adv = ctx.Sent.Select(s => s.Message).OfType<ClassManifestCompressionAdvertisement>().Single();
            adv.TableVersion.Should().Be(1);
            adv.Table.Should().Equal(new[] { "My.Manifest, Asm" });

            // The actor-ref pass, with no ref hits, advertises nothing.
            impl.RunNextActorRefAdvertisement();
            ctx.Sent.Select(s => s.Message).OfType<ActorRefCompressionAdvertisement>().Should().BeEmpty();
        }
    }
}
