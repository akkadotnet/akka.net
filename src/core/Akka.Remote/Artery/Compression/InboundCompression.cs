//-----------------------------------------------------------------------
// <copyright file="InboundCompression.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The receiver-side, per-origin compression <b>rotation state machine</b>, ported (behavior) from
    /// Apache Pekko's <c>InboundCompression[T]</c> + its inner <c>InboundCompression.Tables[T]</c>
    /// (Apache 2.0). One instance tracks compression for a single sending system (origin UID) and a
    /// single category (actor-refs OR class-manifests -- both are string-keyed in the .NET port, so one
    /// class serves both, instantiated twice per origin by the decode stage that will own it later).
    ///
    /// <para>
    /// <b>What this is.</b> The correctness-critical core: it (1) observes inbound values into a
    /// frequency sketch + heavy-hitter set (<see cref="Hit"/>), (2) builds/advertises/resends a versioned
    /// compression table from those heavy hitters (<see cref="BuildNextAdvertisement"/>), (3) activates
    /// an advertised table on confirmation (<see cref="ConfirmAdvertisement"/>), and (4) resolves a
    /// COMPRESSED index back to its value against the rotating tables (<see cref="Decompress"/>),
    /// keeping up to <see cref="KeepOldTables"/> superseded tables so in-flight messages at an older
    /// version still decode.
    /// </para>
    ///
    /// <para>
    /// <b>What this is NOT (deferred to the decode-stage wiring, a later task).</b> No GraphStage,
    /// timer, actor, or scheduler dependency; no control-stream send; no association / liveness
    /// (<c>alive</c>) gating; no <c>getAsyncCallback</c> marshaling. This type is pure, synchronous
    /// logic. Per Pekko's own note, "access to this class must be externally synchronised" -- the
    /// decode stage guarantees single-threaded ownership, so this type contains <b>no locks</b>.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberate deviation from Pekko (a miss never throws).</b> Pekko's <c>decompressInternal</c>
    /// throws <c>UnknownCompressedIdException</c> when a resolved table lacks the requested index. Here,
    /// every unresolvable decode -- disabled/unknown/stale version, or an out-of-range index within a
    /// resolved table -- returns a clean MISS (<see langword="false"/>). The decode stage drops such a
    /// message and lets a fresh table be re-advertised, which is exactly what Pekko's
    /// <c>UnknownCompressedIdException</c> handler ultimately does anyway.
    /// </para>
    /// </summary>
    internal sealed class InboundCompression
    {
        /// <summary>
        /// Number of superseded decompression tables retained so a message still in flight at an older
        /// table version can be decoded. Ported verbatim from Pekko's <c>KeepOldTablesNumber = 3</c>.
        /// </summary>
        public const int KeepOldTables = 3;

        /// <summary>
        /// Maximum number of times an unconfirmed advertisement is resent before giving up and flipping
        /// to it anyway (so the system is never wedged waiting on a lost Ack). Ported verbatim from
        /// Pekko's <c>maxResendCount = 3</c>.
        /// </summary>
        public const int MaxResendCount = 3;

        private readonly long _originUid;
        private readonly IFrequencySketch<string> _frequencySketch;
        private readonly TopHeavyHitters<string> _heavyHitters;

        private Tables _tables;
        private int _resendCount;

        /// <summary>Creates a per-origin inbound compression with the MVP bounded sketch and a heavy-hitter set of <paramref name="maxHeavyHitters"/> entries.</summary>
        /// <param name="originUid">The 64-bit UID of the origin system this instance decodes messages from / advertises tables to.</param>
        /// <param name="maxHeavyHitters">Maximum heavy hitters retained (Pekko's <c>actor-refs.max</c> / <c>manifests.max</c>; default 256).</param>
        public InboundCompression(long originUid, int maxHeavyHitters = TopHeavyHitters<string>.DefaultMax)
            : this(originUid, new BoundedFrequencySketch<string>(), new TopHeavyHitters<string>(maxHeavyHitters))
        {
        }

        /// <summary>Creates a per-origin inbound compression with an injected sketch and heavy-hitter set (test seam).</summary>
        public InboundCompression(long originUid, IFrequencySketch<string> frequencySketch, TopHeavyHitters<string> heavyHitters)
        {
            _originUid = originUid;
            _frequencySketch = frequencySketch ?? throw new ArgumentNullException(nameof(frequencySketch));
            _heavyHitters = heavyHitters ?? throw new ArgumentNullException(nameof(heavyHitters));
            _tables = Tables.Empty(originUid, KeepOldTables);
            _resendCount = 0;
        }

        /// <summary>The origin UID this instance is dedicated to.</summary>
        public long OriginUid => _originUid;

        // ==================== observation ====================

        /// <summary>
        /// Records <paramref name="count"/> observations of <paramref name="value"/> into the frequency
        /// sketch and offers it to the heavy-hitter set. Null/empty values are excluded (Pekko excludes
        /// the empty manifest; temporary/promise actor refs are filtered by the caller before this point).
        /// </summary>
        public void Hit(string value, int count = 1)
        {
            if (string.IsNullOrEmpty(value))
                return; // exclude empty/null -- never a compression candidate
            if (count <= 0)
                return;

            var frequency = _frequencySketch.Add(value, count);
            _heavyHitters.Update(value, frequency);
        }

        // ==================== decode ====================

        /// <summary>
        /// Resolves a COMPRESSED <paramref name="index"/> stamped with table <paramref name="version"/>
        /// back to its value. Ported from Pekko's <c>decompressInternal</c>:
        /// <list type="bullet">
        /// <item>disabled version (<see cref="DecompressionTable{T}.DisabledVersion"/>) -&gt; miss.</item>
        /// <item>version resolves to the active or a retained old table -&gt; return that table's value at
        /// <paramref name="index"/> (out-of-range index -&gt; miss, never throw).</item>
        /// <item>version does not resolve but equals the in-progress advertisement's version -&gt; this is
        /// the <b>first inbound message stamped with the new table</b> (confirmation trigger #2): flip to
        /// it (<see cref="ConfirmAdvertisement"/>) and retry once.</item>
        /// <item>otherwise the version is unknown/greater (e.g. a table built for a previous incarnation of
        /// this system) -&gt; miss, so the caller drops and a fresh table is re-advertised.</item>
        /// </list>
        /// </summary>
        /// <returns><see langword="true"/> and sets <paramref name="value"/> on a hit; <see langword="false"/> (a MISS) otherwise. Never throws on a miss.</returns>
        public bool Decompress(byte version, int index, out string value)
        {
            value = string.Empty;

            if (version == DecompressionTable<string>.DisabledVersion)
                return false; // compression disabled for this frame -- bail out early (Pekko)

            // At most one flip-and-retry: the trigger-#2 flip clears advertisementInProgress, so the retry
            // can never re-enter the flip branch. Bounded loop replaces Pekko's tail-recursion + throw guard.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var selected = _tables.SelectTable(version);
                if (selected is not null)
                {
                    // Resolved a table. A dense table (built by CompressionTable.Invert) has no null slots
                    // in [0, Length); an out-of-range index is a corrupt/stale frame -> clean miss.
                    if ((uint)index < (uint)selected.Length)
                    {
                        value = selected.Get(index);
                        return true;
                    }

                    return false;
                }

                var inProgress = _tables.AdvertisementInProgress;
                if (inProgress is not null && version == inProgress.Version)
                {
                    // Confirmation trigger #2: the sender is already stamping the advertised version, so it
                    // must have installed the table even though the Ack has not (yet) arrived. Flip and retry.
                    ConfirmAdvertisement(version, gaveUp: false);
                    continue;
                }

                // Unknown/greater version: previous-incarnation table. Drop; a fresh table will be advertised.
                return false;
            }

            return false; // defensive: unreachable (the flip clears the in-progress advertisement)
        }

        // ==================== confirmation ====================

        /// <summary>
        /// Activates the in-progress advertised table when it is confirmed. Called on the sender's
        /// explicit Ack (confirmation trigger #1, <paramref name="gaveUp"/> = <see langword="false"/>) and
        /// on give-up after exhausting resends (<paramref name="gaveUp"/> = <see langword="true"/> -- flip
        /// anyway so a lost Ack cannot wedge the rotation). Ported from Pekko's <c>confirmAdvertisement</c>:
        /// a version that does not match the in-progress advertisement (stale/duplicate Ack, or nothing in
        /// progress) is a no-op. <paramref name="gaveUp"/> does not change the flip -- it is informational.
        /// </summary>
        public void ConfirmAdvertisement(byte version, bool gaveUp)
        {
            var inProgress = _tables.AdvertisementInProgress;
            if (inProgress is not null && version == inProgress.Version)
                _tables = _tables.StartUsingNextTable(_originUid);
            // else: no advertisement in progress, or a stale/duplicate confirmation -> already confirmed, ignore.
        }

        // ==================== advertisement build / resend ====================

        /// <summary>
        /// Drives the advertisement lifecycle, ported from Pekko's <c>runNextTableAdvertisement</c> minus
        /// the association/liveness gating (owned by the decode stage later):
        /// <list type="bullet">
        /// <item><b>No advertisement in progress:</b> build the next compression table from the current
        /// heavy hitters at the next version (<c>NextTable.Version</c>, which always equals
        /// <see cref="CompressionTable{T}.IncrementVersion"/> of the active version), stash its inverse as
        /// the pending next table, mark it in progress, reset the resend counter, and return it to advertise.</item>
        /// <item><b>Advertisement in progress, within the resend budget:</b> increment the resend counter and
        /// return the same table to resend (the advertisement can be lost).</item>
        /// <item><b>Advertisement in progress, resend budget exhausted (&gt; <see cref="MaxResendCount"/>):</b>
        /// give up -- flip to it anyway (<see cref="ConfirmAdvertisement"/> with <c>gaveUp = true</c>) and
        /// return <see langword="null"/> (nothing to send).</item>
        /// </list>
        /// </summary>
        /// <returns>The <see cref="CompressionTable{T}"/> to advertise (fresh build or resend), or <see langword="null"/> when the advertisement was given up.</returns>
        public CompressionTable<string>? BuildNextAdvertisement()
        {
            var inProgress = _tables.AdvertisementInProgress;
            if (inProgress is null)
            {
                // Build from the current heavy hitters at the next version. NextTable.Version is maintained
                // as IncrementVersion(ActiveTable.Version) by StartUsingNextTable, so this is Pekko-faithful.
                var version = _tables.NextTable.Version;
                var mappings = new Dictionary<string, int>(_heavyHitters.Count);
                var idx = 0;
                foreach (var hitter in _heavyHitters.Items)
                    mappings[hitter] = idx++; // dense 0..N-1

                var table = new CompressionTable<string>(_originUid, version, mappings);

                // Hand the inverted table to the inbound side as the pending next table so it is ready the
                // moment the advertisement is confirmed, and record it as in progress (Pekko).
                _tables = _tables.WithAdvertisementInProgress(table.Invert(), table);
                _resendCount = 0;
                return table;
            }

            // Advertisement already in progress: resend, then give up.
            _resendCount++;
            if (_resendCount <= MaxResendCount)
                return inProgress; // resend the same table

            // Exhausted the resend budget: flip anyway so we are not wedged waiting on a lost Ack.
            ConfirmAdvertisement(inProgress.Version, gaveUp: true);
            return null;
        }

        // ==================== inspection (test / diagnostics) ====================

        /// <summary>The version of the currently active decompression table.</summary>
        internal byte ActiveVersion => _tables.ActiveTable.Version;

        /// <summary>The version the next built table will carry (== <see cref="CompressionTable{T}.IncrementVersion"/> of <see cref="ActiveVersion"/>).</summary>
        internal byte NextVersion => _tables.NextTable.Version;

        /// <summary>The advertisement currently awaiting confirmation, or <see langword="null"/> if none is in progress.</summary>
        internal CompressionTable<string>? AdvertisementInProgress => _tables.AdvertisementInProgress;

        /// <summary>The versions of the retained old tables, newest first (always 1..<see cref="KeepOldTables"/> entries).</summary>
        internal IReadOnlyList<byte> OldTableVersions
        {
            get
            {
                var versions = new byte[_tables.OldTables.Count];
                for (var i = 0; i < versions.Length; i++)
                    versions[i] = _tables.OldTables[i].Version;
                return versions;
            }
        }

        /// <summary>The current resend count for the in-progress advertisement.</summary>
        internal int ResendCount => _resendCount;

        /// <summary>Number of heavy hitters currently tracked.</summary>
        internal int HeavyHitterCount => _heavyHitters.Count;

        /// <summary>
        /// The rotating set of decompression tables for one origin, ported from Pekko's immutable
        /// <c>InboundCompression.Tables[T]</c> case class. Every mutation produces a fresh instance so the
        /// hot decode path always reads a consistent snapshot.
        /// </summary>
        private sealed class Tables
        {
            private Tables(
                IReadOnlyList<DecompressionTable<string>> oldTables,
                DecompressionTable<string> activeTable,
                DecompressionTable<string> nextTable,
                CompressionTable<string>? advertisementInProgress,
                int keepOldTables)
            {
                OldTables = oldTables;
                ActiveTable = activeTable;
                NextTable = nextTable;
                AdvertisementInProgress = advertisementInProgress;
                KeepOldTablesCount = keepOldTables;
            }

            /// <summary>Retained superseded tables, newest first; always holds 1..<see cref="KeepOldTablesCount"/> entries (starts with the single disabled table).</summary>
            public IReadOnlyList<DecompressionTable<string>> OldTables { get; }

            /// <summary>The active decompression table.</summary>
            public DecompressionTable<string> ActiveTable { get; }

            /// <summary>The pending next table; becomes active on the next <see cref="StartUsingNextTable"/>.</summary>
            public DecompressionTable<string> NextTable { get; }

            /// <summary>The advertised compression table awaiting confirmation, or <see langword="null"/>.</summary>
            public CompressionTable<string>? AdvertisementInProgress { get; }

            /// <summary>Retention cap for <see cref="OldTables"/> (<see cref="KeepOldTables"/>).</summary>
            public int KeepOldTablesCount { get; }

            /// <summary>
            /// The initial state: a single disabled old table (version <see cref="DecompressionTable{T}.DisabledVersion"/>),
            /// an empty active table at version 0, an empty next table at version 1, and no advertisement.
            /// Mirrors Pekko's <c>Tables.empty</c>.
            /// </summary>
            public static Tables Empty(long originUid, int keepOldTables) =>
                new Tables(
                    oldTables: new[] { new DecompressionTable<string>(originUid, DecompressionTable<string>.DisabledVersion, Array.Empty<string?>()) },
                    activeTable: new DecompressionTable<string>(originUid, 0, Array.Empty<string?>()),
                    nextTable: new DecompressionTable<string>(originUid, 1, Array.Empty<string?>()),
                    advertisementInProgress: null,
                    keepOldTables: keepOldTables);

            /// <summary>
            /// Finds the table matching <paramref name="version"/>: the active table first, then the retained
            /// old tables. Returns <see langword="null"/> if no retained table carries that version. Ported
            /// from Pekko's <c>selectTable</c>.
            /// </summary>
            public DecompressionTable<string>? SelectTable(byte version)
            {
                if (ActiveTable.Version == version)
                    return ActiveTable;

                foreach (var table in OldTables)
                {
                    if (table.Version == version)
                        return table;
                }

                return null;
            }

            /// <summary>
            /// Rotates the tables: the current active table is prepended to the old tables (capped at
            /// <see cref="KeepOldTablesCount"/>), the next table becomes active, a fresh empty next table is
            /// created at the incremented version (<c>127 -&gt; 0</c> wrap), and the advertisement is cleared.
            /// Ported from Pekko's <c>startUsingNextTable</c>.
            /// </summary>
            public Tables StartUsingNextTable(long originUid)
            {
                var newOld = new List<DecompressionTable<string>>(KeepOldTablesCount) { ActiveTable };
                foreach (var table in OldTables)
                {
                    if (newOld.Count >= KeepOldTablesCount)
                        break;
                    newOld.Add(table);
                }

                var newNext = new DecompressionTable<string>(
                    originUid,
                    CompressionTable<string>.IncrementVersion(NextTable.Version),
                    Array.Empty<string?>());

                return new Tables(newOld, NextTable, newNext, advertisementInProgress: null, KeepOldTablesCount);
            }

            /// <summary>Records a freshly built advertisement: stashes its inverse as the pending next table and marks it in progress.</summary>
            public Tables WithAdvertisementInProgress(DecompressionTable<string> nextTable, CompressionTable<string> advertisementInProgress) =>
                new Tables(OldTables, ActiveTable, nextTable, advertisementInProgress, KeepOldTablesCount);
        }
    }
}
