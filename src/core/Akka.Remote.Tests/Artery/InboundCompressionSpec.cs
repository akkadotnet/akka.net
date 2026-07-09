//-----------------------------------------------------------------------
// <copyright file="InboundCompressionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using Akka.Remote.Artery.Compression;
using FluentAssertions;
using Xunit;

namespace Akka.Remote.Tests.Artery
{
    /// <summary>
    /// Exhaustive unit tests for the receiver-side, per-origin compression rotation state machine
    /// (<see cref="InboundCompression"/>), ported from Apache Pekko's <c>InboundCompression[T]</c> +
    /// <c>InboundCompression.Tables[T]</c> -- see
    /// <c>openspec/changes/artery-ref-manifest-compression/design.md</c> (the "Verified Pekko
    /// compression model" section and Q2/Q3). These cover the correctness-critical core in isolation:
    /// both confirmation triggers (explicit Ack and first-stamped-message), <c>KeepOldTables = 3</c>
    /// retention, the <c>127 -&gt; 0</c> version wrap, the unknown/greater-version miss (restarted
    /// incarnation), and the resend-then-give-up advertisement lifecycle. Pure logic -- no actor system,
    /// no timers, no stage wiring.
    /// </summary>
    public class InboundCompressionSpec
    {
        private const long OriginUid = 0x1234_5678_9ABC_DEF0L;
        private const byte Disabled = DecompressionTable<string>.DisabledVersion; // 0xFF

        private static string Ref(string name) => $"akka://sys/user/{name}";

        /// <summary>Builds an advertisement, confirms it via an explicit Ack, and returns the advertised table.</summary>
        private static CompressionTable<string> RotateViaAck(InboundCompression ic, params string[] hits)
        {
            foreach (var hit in hits)
                ic.Hit(hit);
            var adv = ic.BuildNextAdvertisement();
            adv.Should().NotBeNull("a fresh advertisement is built when none is in progress");
            ic.ConfirmAdvertisement(adv!.Version, gaveUp: false);
            return adv;
        }

        // ============================================================
        // Confirmation trigger #1: explicit Ack
        // ============================================================

        [Fact(DisplayName = "Build -> advertise -> confirm via Ack activates the table, retains the previous, and resolves indices")]
        public void Should_activate_advertised_table_on_Ack_and_retain_previous()
        {
            var ic = new InboundCompression(OriginUid);
            ic.Hit(Ref("alpha"));
            ic.Hit(Ref("beta"));

            // Build the first advertisement -- at version 1, not yet active.
            var adv = ic.BuildNextAdvertisement();
            adv.Should().NotBeNull();
            adv!.Version.Should().Be(1);
            ic.ActiveVersion.Should().Be(0, "the advertisement is only pending until confirmed");
            ic.AdvertisementInProgress.Should().NotBeNull();

            // Explicit Ack (trigger #1) flips it in.
            ic.ConfirmAdvertisement(1, gaveUp: false);

            ic.ActiveVersion.Should().Be(1);
            ic.NextVersion.Should().Be(2, "the next table version advances past the newly active one");
            ic.AdvertisementInProgress.Should().BeNull("the advertisement is confirmed");

            // The previously active empty@0 table is retained (newest first), ahead of the disabled sentinel.
            ic.OldTableVersions.Should().Equal(new byte[] { 0, Disabled });

            // Indices from the advertised table resolve against the now-active table.
            var idxAlpha = adv.Compress(Ref("alpha"));
            var idxBeta = adv.Compress(Ref("beta"));
            idxAlpha.Should().BeGreaterOrEqualTo(0);
            idxBeta.Should().BeGreaterOrEqualTo(0);

            ic.Decompress(1, idxAlpha, out var alpha).Should().BeTrue();
            alpha.Should().Be(Ref("alpha"));
            ic.Decompress(1, idxBeta, out var beta).Should().BeTrue();
            beta.Should().Be(Ref("beta"));
        }

        [Fact(DisplayName = "A confirmation whose version does not match the in-progress advertisement is a no-op")]
        public void Should_ignore_confirmation_with_mismatched_version()
        {
            var ic = new InboundCompression(OriginUid);
            ic.Hit(Ref("z"));
            ic.BuildNextAdvertisement(); // in progress @1

            ic.ConfirmAdvertisement(99, gaveUp: false); // stale / wrong version

            ic.ActiveVersion.Should().Be(0, "a mismatched confirmation must not flip the table");
            ic.AdvertisementInProgress.Should().NotBeNull();
            ic.AdvertisementInProgress!.Version.Should().Be(1);
        }

        [Fact(DisplayName = "A confirmation with no advertisement in progress is a no-op")]
        public void Should_ignore_confirmation_when_nothing_in_progress()
        {
            var ic = new InboundCompression(OriginUid);

            ic.ConfirmAdvertisement(1, gaveUp: false);

            ic.ActiveVersion.Should().Be(0);
            ic.OldTableVersions.Should().ContainSingle().Which.Should().Be(Disabled);
        }

        // ============================================================
        // Confirmation trigger #2: first inbound message stamped with the new version
        // ============================================================

        [Fact(DisplayName = "A Decompress at the in-progress version flips the table without an Ack (belt-and-suspenders trigger #2)")]
        public void Should_activate_advertised_table_on_first_stamped_message()
        {
            var ic = new InboundCompression(OriginUid);
            ic.Hit(Ref("gamma"));

            var adv = ic.BuildNextAdvertisement();
            adv!.Version.Should().Be(1);
            ic.ActiveVersion.Should().Be(0);
            ic.AdvertisementInProgress.Should().NotBeNull();

            var idx = adv.Compress(Ref("gamma"));

            // No explicit Ack -- the very first message stamped with version 1 both resolves AND flips.
            ic.Decompress(1, idx, out var value).Should().BeTrue();
            value.Should().Be(Ref("gamma"));

            ic.ActiveVersion.Should().Be(1, "the first stamped message activates the advertised table");
            ic.AdvertisementInProgress.Should().BeNull("trigger #2 confirms the advertisement");

            // Subsequent messages resolve directly against the now-active table.
            ic.Decompress(1, idx, out var again).Should().BeTrue();
            again.Should().Be(Ref("gamma"));
        }

        // ============================================================
        // KeepOldTables = 3 retention window
        // ============================================================

        [Fact(DisplayName = "After >= 4 rotations exactly 3 old tables are kept; in-window versions resolve, older-than-window misses")]
        public void Should_keep_exactly_three_old_tables_and_resolve_within_window()
        {
            var ic = new InboundCompression(OriginUid);

            // Rotate 5 times, each introducing a distinct value; capture (version, index, value) per rotation.
            var records = new List<(byte version, int index, string value)>();
            for (var r = 1; r <= 5; r++)
            {
                var value = Ref($"r{r}");
                ic.Hit(value);
                var adv = ic.BuildNextAdvertisement();
                var index = adv!.Compress(value);
                ic.ConfirmAdvertisement(adv.Version, gaveUp: false);
                records.Add((adv.Version, index, value));
            }

            // active@5, oldTables = [4,3,2] (versions 1 and the disabled sentinel have fallen out of the window).
            ic.ActiveVersion.Should().Be(5);
            ic.OldTableVersions.Should().HaveCount(InboundCompression.KeepOldTables);
            ic.OldTableVersions.Should().Equal(new byte[] { 4, 3, 2 });

            // Active version resolves.
            var latest = records[4];
            ic.Decompress(latest.version, latest.index, out var latestValue).Should().BeTrue();
            latestValue.Should().Be(latest.value);

            // Oldest still-in-window version (2) resolves via an old table.
            var inWindow = records[1];
            inWindow.version.Should().Be(2);
            ic.Decompress(inWindow.version, inWindow.index, out var inWindowValue).Should().BeTrue();
            inWindowValue.Should().Be(inWindow.value);

            // Older-than-window version (1) misses -- caller drops, no throw.
            var evicted = records[0];
            evicted.version.Should().Be(1);
            Action decodeEvicted = () => ic.Decompress(evicted.version, evicted.index, out _);
            decodeEvicted.Should().NotThrow();
            ic.Decompress(evicted.version, evicted.index, out _).Should().BeFalse();
        }

        // ============================================================
        // Version wraparound 127 -> 0
        // ============================================================

        [Fact(DisplayName = "Table versions cycle 0..127 and wrap 127 -> 0, never exceeding 127")]
        public void Should_wrap_table_version_from_127_to_0()
        {
            var ic = new InboundCompression(OriginUid);

            // Drive enough build+confirm rotations to pass version 127 and observe the wrap.
            var activeVersions = new List<byte>();
            for (var r = 0; r < 130; r++)
            {
                var adv = ic.BuildNextAdvertisement(); // empty table -- only the version matters here
                adv.Should().NotBeNull();
                ic.ConfirmAdvertisement(adv!.Version, gaveUp: false);
                activeVersions.Add(ic.ActiveVersion);
            }

            // No version ever leaves the 0..127 range.
            activeVersions.Should().OnlyContain(v => v <= CompressionTable<string>.MaxVersion);

            // The active version reaches 127 and the very next rotation wraps to 0 (not 128).
            var idx127 = activeVersions.IndexOf(127);
            idx127.Should().BeGreaterOrEqualTo(0, "the sequence must reach version 127");
            idx127.Should().BeLessThan(activeVersions.Count - 1);
            activeVersions[idx127 + 1].Should().Be(0, "version 127 wraps to 0");

            // NextVersion is always IncrementVersion(ActiveVersion) -- verify the post-wrap invariant.
            ic.NextVersion.Should().Be(CompressionTable<string>.IncrementVersion(ic.ActiveVersion));
        }

        // ============================================================
        // Unknown / greater version -> miss (restarted incarnation)
        // ============================================================

        [Fact(DisplayName = "An unknown/greater table version is a clean miss (no throw) so a fresh table can be re-advertised")]
        public void Should_miss_on_unknown_or_greater_version_without_throwing()
        {
            var ic = new InboundCompression(OriginUid);
            RotateViaAck(ic, Ref("x")); // active@1

            // A version beyond anything known (e.g. a table built for a previous incarnation of this system).
            Action decodeGreater = () => ic.Decompress(5, 0, out _);
            decodeGreater.Should().NotThrow();

            ic.Decompress(5, 0, out var miss1).Should().BeFalse();
            miss1.Should().Be(string.Empty);
            ic.Decompress(100, 0, out _).Should().BeFalse();
            ic.Decompress(127, 3, out _).Should().BeFalse();

            // The miss leaves state untouched -- still active@1, no advertisement conjured.
            ic.ActiveVersion.Should().Be(1);
            ic.AdvertisementInProgress.Should().BeNull();
        }

        [Fact(DisplayName = "An out-of-range index within a resolved table is a miss, never a throw")]
        public void Should_miss_on_out_of_range_index_within_resolved_table()
        {
            var ic = new InboundCompression(OriginUid);
            var adv = RotateViaAck(ic, Ref("only")); // active@1 with a single entry at index 0

            Action decodeOob = () => ic.Decompress(1, 999, out _);
            decodeOob.Should().NotThrow();
            ic.Decompress(1, 999, out _).Should().BeFalse();

            // The valid index still resolves.
            var idx = adv.Compress(Ref("only"));
            ic.Decompress(1, idx, out var value).Should().BeTrue();
            value.Should().Be(Ref("only"));
        }

        // ============================================================
        // Advertisement lifecycle: resend up to 3, then give up (flip anyway)
        // ============================================================

        [Fact(DisplayName = "An unconfirmed advertisement is resent up to 3 times, then given up on and flipped anyway")]
        public void Should_resend_up_to_three_times_then_give_up_and_flip()
        {
            var ic = new InboundCompression(OriginUid);
            ic.Hit(Ref("y"));

            // Fresh build @1.
            var first = ic.BuildNextAdvertisement();
            first.Should().NotBeNull();
            first!.Version.Should().Be(1);
            ic.ResendCount.Should().Be(0);
            ic.AdvertisementInProgress.Should().NotBeNull();

            // Resends 1..3 return the SAME table (it can be lost, so it is re-advertised verbatim).
            for (var expectedResend = 1; expectedResend <= InboundCompression.MaxResendCount; expectedResend++)
            {
                var resend = ic.BuildNextAdvertisement();
                resend.Should().BeSameAs(first, "an in-progress advertisement is resent, not rebuilt");
                ic.ResendCount.Should().Be(expectedResend);
                ic.AdvertisementInProgress.Should().NotBeNull();
                ic.ActiveVersion.Should().Be(0, "resending must not flip the table");
            }

            // The 4th attempt exceeds maxResendCount -> give up: flip anyway, nothing left to send.
            var gaveUp = ic.BuildNextAdvertisement();
            gaveUp.Should().BeNull("giving up returns no table to advertise");
            ic.ResendCount.Should().Be(4);
            ic.AdvertisementInProgress.Should().BeNull("the advertisement is confirmed via give-up");
            ic.ActiveVersion.Should().Be(1, "give-up flips to the advertised table so the rotation is not wedged");

            // The flipped-in table is usable.
            var idx = first.Compress(Ref("y"));
            ic.Decompress(1, idx, out var value).Should().BeTrue();
            value.Should().Be(Ref("y"));
        }

        [Fact(DisplayName = "After giving up, the next build advertises a fresh table at the incremented version")]
        public void Should_build_fresh_advertisement_after_give_up()
        {
            var ic = new InboundCompression(OriginUid);
            ic.Hit(Ref("g"));

            // Exhaust: build + 4 more calls to trigger give-up.
            ic.BuildNextAdvertisement(); // build @1
            for (var i = 0; i < InboundCompression.MaxResendCount + 1; i++)
                ic.BuildNextAdvertisement();

            ic.ActiveVersion.Should().Be(1);
            ic.AdvertisementInProgress.Should().BeNull();
            ic.NextVersion.Should().Be(2);

            // A fresh build now advertises version 2 and resets the resend counter.
            var fresh = ic.BuildNextAdvertisement();
            fresh.Should().NotBeNull();
            fresh!.Version.Should().Be(2);
            ic.ResendCount.Should().Be(0);
            ic.AdvertisementInProgress.Should().NotBeNull();
        }

        // ============================================================
        // Disabled / empty state
        // ============================================================

        [Fact(DisplayName = "The disabled/empty initial state resolves nothing and never throws")]
        public void Should_resolve_nothing_in_disabled_or_empty_state()
        {
            var ic = new InboundCompression(OriginUid);

            // Disabled version sentinel -- bail out early.
            ic.Decompress(Disabled, 0, out var disabled).Should().BeFalse();
            disabled.Should().Be(string.Empty);

            // Empty active table @0 -- any index is a miss.
            ic.Decompress(0, 0, out _).Should().BeFalse();
            ic.Decompress(0, 5, out _).Should().BeFalse();

            // The empty next table @1 is not selectable (no advertisement in progress) -- miss.
            ic.Decompress(1, 0, out _).Should().BeFalse();

            // Initial state is intact and pristine.
            ic.ActiveVersion.Should().Be(0);
            ic.NextVersion.Should().Be(1);
            ic.HeavyHitterCount.Should().Be(0);
            ic.AdvertisementInProgress.Should().BeNull();
            ic.OldTableVersions.Should().ContainSingle().Which.Should().Be(Disabled);
        }

        // ============================================================
        // Observation (Hit)
        // ============================================================

        [Fact(DisplayName = "Hit excludes null/empty values and counts real observations toward the heavy-hitter set")]
        public void Should_exclude_null_and_empty_hits()
        {
            var ic = new InboundCompression(OriginUid);

            ic.Hit(string.Empty);
            ic.Hit(null!);
            ic.Hit("real", count: 0);   // non-positive count is ignored
            ic.Hit("real", count: -3);
            ic.HeavyHitterCount.Should().Be(0, "empty/null/non-positive-count observations are not candidates");

            ic.Hit(Ref("real"));
            ic.HeavyHitterCount.Should().Be(1);

            // A built advertisement contains only the real hitter.
            var adv = ic.BuildNextAdvertisement();
            adv!.Compress(Ref("real")).Should().BeGreaterOrEqualTo(0);
            adv.Dictionary.Count.Should().Be(1);
        }

        [Fact(DisplayName = "The advertised version always equals IncrementVersion(active version) across rotations")]
        public void Should_advertise_at_increment_of_active_version()
        {
            var ic = new InboundCompression(OriginUid);

            for (var r = 0; r < 6; r++)
            {
                var expected = CompressionTable<string>.IncrementVersion(ic.ActiveVersion);
                var adv = ic.BuildNextAdvertisement();
                adv!.Version.Should().Be(expected, "the next table is built one version past the active one");
                ic.ConfirmAdvertisement(adv.Version, gaveUp: false);
                ic.ActiveVersion.Should().Be(expected);
            }
        }
    }
}
