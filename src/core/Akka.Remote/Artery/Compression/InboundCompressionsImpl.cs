//-----------------------------------------------------------------------
// <copyright file="InboundCompressionsImpl.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Event;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The live RECEIVER-side compression coordinator (design.md "artery-ref-manifest-compression",
    /// Stage 2b-ii), ported (behavior) from Apache Pekko's <c>InboundCompressionsImpl</c> (Apache 2.0).
    /// It demultiplexes by the sending system's 64-bit origin UID, owning a per-origin pair of
    /// <see cref="InboundCompression"/> rotation state machines (one for actor-refs, one for
    /// class-manifests -- both string-keyed in the .NET port). It (1) observes decoded values into those
    /// machines' heavy-hitter sets (<see cref="HitActorRef"/>/<see cref="HitClassManifest"/>), (2) builds
    /// and advertises versioned tables back to each origin on a schedule
    /// (<see cref="RunNextActorRefAdvertisement"/>/<see cref="RunNextClassManifestAdvertisement"/>),
    /// (3) activates an advertised table on the origin's Ack or its first stamped message
    /// (<see cref="ConfirmActorRefAdvertisement"/> / the flip inside <see cref="TryDecompressActorRef"/>),
    /// and (4) resolves COMPRESSED indices back to their values on decode.
    ///
    /// <para>
    /// <b>THREADING (design.md Q1, Option A -- the load-bearing decision).</b> This type holds
    /// <b>NO locks</b>. It is <b>owned by a single inbound decode stage</b>
    /// (<see cref="ArteryInboundProcessingStage"/>) and every method is invoked <b>only on that stage's
    /// interpreter thread</b>: observation and decode run in the stage's <c>OnPush</c>, the advertisement
    /// pass runs in the stage's <c>OnTimer</c> (a <c>TimerGraphStageLogic</c> timer fires on the stage
    /// thread), and an Ack -- which arrives on a DIFFERENT (control-stream) thread -- is marshaled onto
    /// the stage thread via <c>GetAsyncCallback</c> before <see cref="ConfirmActorRefAdvertisement"/> is
    /// called. The per-origin dictionary is therefore never touched concurrently. The transport seam it
    /// calls out through (<see cref="IInboundCompressionContext"/>) is independently thread-safe.
    /// </para>
    /// </summary>
    internal sealed class InboundCompressionsImpl : IInboundCompressions
    {
        private readonly IInboundCompressionContext _context;
        private readonly int _actorRefsMax;
        private readonly int _manifestsMax;
        private readonly ILoggingAdapter _log;

        // Stage-owned, single-threaded: created on demand per origin UID; never touched off the stage thread.
        private readonly Dictionary<long, OriginState> _origins = new();

        public InboundCompressionsImpl(IInboundCompressionContext context, int actorRefsMax, int manifestsMax, ILoggingAdapter log)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _actorRefsMax = actorRefsMax;
            _manifestsMax = manifestsMax;
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>The transport seam (send / resolve / publish / subscribe), exposed so the owning decode stage can wire its Ack subscription.</summary>
        public IInboundCompressionContext Context => _context;

        // ==================== observation ====================

        /// <inheritdoc/>
        public void HitActorRef(long originUid, string actorPath, int count)
        {
            if (_actorRefsMax <= 0 || string.IsNullOrEmpty(actorPath))
                return; // actor-ref compression disabled for this system, or nothing to count

            var state = GetOrCreate(originUid);
            state.Refs.Hit(actorPath, count);
            state.AliveRefs = true; // new observation since the last advertisement (Pekko's `alive`)
        }

        /// <inheritdoc/>
        public void HitClassManifest(long originUid, string manifest, int count)
        {
            if (_manifestsMax <= 0 || string.IsNullOrEmpty(manifest))
                return;

            var state = GetOrCreate(originUid);
            state.Manifests.Hit(manifest, count);
            state.AliveManifests = true;
        }

        // ==================== decode resolution ====================

        /// <inheritdoc/>
        public bool TryDecompressActorRef(long originUid, byte tableVersion, int idx, out string actorPath)
        {
            actorPath = string.Empty;
            if (idx < 0)
                return false; // defensive: a COMPRESSED tag never carries NotCompressedId (-1); clean miss, never throw

            var state = GetOrCreate(originUid);
            var before = state.Refs.ActiveVersion;
            var hit = state.Refs.Decompress(tableVersion, idx, out actorPath);
            PublishFlipIfActivated(originUid, state.Refs, before, isManifest: false);
            if (hit)
                PublishFirstResolve(originUid, tableVersion, ref state.LastResolvedRefVersion, isManifest: false);
            return hit;
        }

        /// <inheritdoc/>
        public bool TryDecompressClassManifest(long originUid, byte tableVersion, int idx, out string manifest)
        {
            manifest = string.Empty;
            if (idx < 0)
                return false;

            var state = GetOrCreate(originUid);
            var before = state.Manifests.ActiveVersion;
            var hit = state.Manifests.Decompress(tableVersion, idx, out manifest);
            PublishFlipIfActivated(originUid, state.Manifests, before, isManifest: true);
            if (hit)
                PublishFirstResolve(originUid, tableVersion, ref state.LastResolvedManifestVersion, isManifest: true);
            return hit;
        }

        // ==================== confirmation (Ack, trigger #1) ====================

        /// <inheritdoc/>
        public void ConfirmActorRefAdvertisement(long originUid, byte tableVersion)
        {
            if (!_origins.TryGetValue(originUid, out var state))
                return; // Ack for an origin we no longer track -- ignore (Pekko's `case null => // ignore`)

            var before = state.Refs.ActiveVersion;
            state.Refs.ConfirmAdvertisement(tableVersion, gaveUp: false);
            PublishFlipIfActivated(originUid, state.Refs, before, isManifest: false);
        }

        /// <inheritdoc/>
        public void ConfirmClassManifestAdvertisement(long originUid, byte tableVersion)
        {
            if (!_origins.TryGetValue(originUid, out var state))
                return;

            var before = state.Manifests.ActiveVersion;
            state.Manifests.ConfirmAdvertisement(tableVersion, gaveUp: false);
            PublishFlipIfActivated(originUid, state.Manifests, before, isManifest: true);
        }

        // ==================== advertisement pass (timer) ====================

        /// <inheritdoc/>
        public void RunNextActorRefAdvertisement() => RunAdvertisementPass(isManifest: false);

        /// <inheritdoc/>
        public void RunNextClassManifestAdvertisement() => RunAdvertisementPass(isManifest: true);

        private void RunAdvertisementPass(bool isManifest)
        {
            List<long>? toClose = null;

            // Snapshot the keys: an advertisement pass may Close (remove) unresolvable origins.
            foreach (var originUid in SnapshotOriginKeys())
            {
                if (!_origins.TryGetValue(originUid, out var state))
                    continue;

                var remoteAddress = _context.ResolveAdvertisableOrigin(originUid);
                if (remoteAddress is null)
                {
                    // No (non-quarantined) association: too early, or dead. Drop this origin's state
                    // (Pekko's `remove :+= inbound.originUid; remove.foreach(close)`).
                    (toClose ??= new List<long>()).Add(originUid);
                    continue;
                }

                AdvertiseFor(originUid, state, remoteAddress, isManifest);
            }

            if (toClose is not null)
                foreach (var uid in toClose)
                    Close(uid);
        }

        private void AdvertiseFor(long originUid, OriginState state, Address remoteAddress, bool isManifest)
        {
            var comp = isManifest ? state.Manifests : state.Refs;

            if (comp.AdvertisementInProgress is not null)
            {
                // An advertisement is already awaiting confirmation: RESEND it (up to MaxResendCount),
                // then GIVE UP (flip anyway). BuildNextAdvertisement drives that lifecycle and returns
                // the table to resend, or null on give-up. `alive` does NOT gate a resend (Pekko).
                var before = comp.ActiveVersion;
                var resend = comp.BuildNextAdvertisement();
                if (resend is not null)
                    _context.SendControl(remoteAddress, BuildAdvertisementMessage(originUid, resend, isManifest));
                else
                    // Gave up: BuildNextAdvertisement flipped to the table internally -- surface the activation.
                    PublishFlipIfActivated(originUid, comp, before, isManifest);
                return;
            }

            // No advertisement in progress. Only build a NEW one when there is something new to say
            // (Pekko's `alive`) AND the table is non-empty ("do not re-advertise an empty or unchanged
            // table"). `alive` was set by the last observation and is cleared here on a successful send.
            var alive = isManifest ? state.AliveManifests : state.AliveRefs;
            if (!alive || comp.HeavyHitterCount == 0)
                return;

            var table = comp.BuildNextAdvertisement();
            if (table is null)
                return; // unreachable when no advertisement is in progress, but stay defensive

            _context.SendControl(remoteAddress, BuildAdvertisementMessage(originUid, table, isManifest));
            if (isManifest)
                state.AliveManifests = false;
            else
                state.AliveRefs = false;

            _context.PublishEvent(new ArteryInboundCompressionEvent(
                originUid, table.Version, isManifest, ArteryInboundCompressionPhase.Advertised, table.Dictionary.Count));

            if (_log.IsDebugEnabled)
                _log.Debug(
                    "Advertised {0} compression table version [{1}] ({2} entries) to origin [{3}] at [{4}].",
                    isManifest ? "manifest" : "actor-ref", table.Version, table.Dictionary.Count, originUid, remoteAddress);
        }

        // ==================== lifecycle ====================

        /// <inheritdoc/>
        public IReadOnlyCollection<long> CurrentOriginUids => new List<long>(_origins.Keys);

        /// <inheritdoc/>
        public void Close(long originUid) => _origins.Remove(originUid);

        // ==================== internals ====================

        private OriginState GetOrCreate(long originUid)
        {
            if (!_origins.TryGetValue(originUid, out var state))
            {
                state = new OriginState(originUid, _actorRefsMax, _manifestsMax);
                _origins[originUid] = state;
            }

            return state;
        }

        /// <summary>Copies the origin keys into a fresh array so the advertisement pass can safely <see cref="Close"/> entries mid-iteration.</summary>
        private long[] SnapshotOriginKeys()
        {
            var keys = new long[_origins.Count];
            _origins.Keys.CopyTo(keys, 0);
            return keys;
        }

        /// <summary>Publishes an <see cref="ArteryInboundCompressionPhase.Activated"/> event if the active version changed (a table was flipped in).</summary>
        private void PublishFlipIfActivated(long originUid, InboundCompression comp, byte beforeVersion, bool isManifest)
        {
            var after = comp.ActiveVersion;
            if (after == beforeVersion)
                return;

            _context.PublishEvent(new ArteryInboundCompressionEvent(
                originUid, after, isManifest, ArteryInboundCompressionPhase.Activated));

            if (_log.IsDebugEnabled)
                _log.Debug(
                    "Activated {0} compression table version [{1}] for origin [{2}].",
                    isManifest ? "manifest" : "actor-ref", after, originUid);
        }

        /// <summary>Publishes a <see cref="ArteryInboundCompressionPhase.Resolved"/> event the first time a COMPRESSED tag resolves at a given version (once per origin/category/version).</summary>
        private void PublishFirstResolve(long originUid, byte version, ref byte? lastResolvedVersion, bool isManifest)
        {
            if (lastResolvedVersion == version)
                return;

            lastResolvedVersion = version;
            _context.PublishEvent(new ArteryInboundCompressionEvent(
                originUid, version, isManifest, ArteryInboundCompressionPhase.Resolved));
        }

        private ICompressionAdvertisement BuildAdvertisementMessage(long originUid, CompressionTable<string> table, bool isManifest)
        {
            // The advertised value list is dense 0..N-1 (position == index); rebuild it from the
            // value->index dictionary (design.md Decision 5 single-ordered-list wire form).
            var entries = new string[table.Dictionary.Count];
            foreach (var kv in table.Dictionary)
                entries[kv.Value] = kv.Key;

            var carrier = new CompressionAdvertisementTable(entries);

            // `From` is THIS system (the advertiser); `OriginUid` is the system that will USE the table
            // for outbound -- i.e. the origin we observed and are advertising back to (Pekko).
            return isManifest
                ? new ClassManifestCompressionAdvertisement(_context.LocalAddress, originUid, table.Version, carrier)
                : new ActorRefCompressionAdvertisement(_context.LocalAddress, originUid, table.Version, carrier);
        }

        /// <summary>Per-origin bundle: the two rotation state machines plus the receiver-side `alive`/first-resolve bookkeeping the decode stage owns.</summary>
        private sealed class OriginState
        {
            public OriginState(long originUid, int actorRefsMax, int manifestsMax)
            {
                Refs = new InboundCompression(originUid, Math.Max(actorRefsMax, 0));
                Manifests = new InboundCompression(originUid, Math.Max(manifestsMax, 0));
            }

            public InboundCompression Refs { get; }
            public InboundCompression Manifests { get; }

            /// <summary>Whether a new actor-ref heavy hitter has been observed since the last advertisement (Pekko's `alive`).</summary>
            public bool AliveRefs;

            /// <summary>Manifest counterpart of <see cref="AliveRefs"/>.</summary>
            public bool AliveManifests;

            /// <summary>The last actor-ref table version a Resolved event was published for (dedupes the per-version signal).</summary>
            public byte? LastResolvedRefVersion;

            /// <summary>Manifest counterpart of <see cref="LastResolvedRefVersion"/>.</summary>
            public byte? LastResolvedManifestVersion;
        }
    }
}
