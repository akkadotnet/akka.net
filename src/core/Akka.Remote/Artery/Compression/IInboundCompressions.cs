//-----------------------------------------------------------------------
// <copyright file="IInboundCompressions.cs" company="Akka.NET Project">
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
    /// Receiver-side compression coordinator, ported from Apache Pekko's
    /// <c>InboundCompressions</c> (Apache 2.0). One logical instance per inbound decode pipeline; it
    /// demultiplexes by the sending system's 64-bit origin UID, keeping a per-origin decompression
    /// table rotation (for resolving COMPRESSED indices) and per-origin heavy-hitter counters (for
    /// deciding which values to advertise back to that sender).
    ///
    /// <para>
    /// The .NET port keys everything on <b>strings</b> -- actor paths and manifests -- rather than
    /// Pekko's <c>ActorRef</c>/<c>String</c> split (see design.md). Both categories share this one
    /// interface, distinguished by the <c>ActorRef</c> vs <c>ClassManifest</c> method families.
    /// </para>
    ///
    /// <para>
    /// SCAFFOLD (feature/artery-ref-manifest-compression): only <see cref="NoInboundCompressions"/>
    /// (the disabled, off-by-default path) is implemented here. The real
    /// <c>InboundCompressionsImpl</c> -- heavy-hitter sketch, table building/rotation, and control-stream
    /// advertisement -- is a later task. Ownership/threading of that impl (stage-owned vs
    /// registry-owned per-UID) is an OPEN design question flagged for review in design.md.
    /// </para>
    /// </summary>
    internal interface IInboundCompressions
    {
        // ---- actor-ref (path-string) compression ----

        /// <summary>Record <paramref name="count"/> observations of a warm actor path from <paramref name="originUid"/> (heavy-hitter counting). Temporary/promise refs are excluded by the caller.</summary>
        void HitActorRef(long originUid, string actorPath, int count);

        /// <summary>Resolve a COMPRESSED actor-ref index against the decompression table selected by (<paramref name="originUid"/>, <paramref name="tableVersion"/>). Returns <see langword="false"/> for an unknown/stale table so the caller drops the message.</summary>
        bool TryDecompressActorRef(long originUid, byte tableVersion, int idx, out string actorPath);

        /// <summary>Activate an advertised actor-ref table once the sender's Ack (or first stamped message) confirms <paramref name="tableVersion"/>.</summary>
        void ConfirmActorRefAdvertisement(long originUid, byte tableVersion);

        /// <summary>Build and advertise (or resend) the next actor-ref compression table to every live origin. Triggered on the advertisement schedule.</summary>
        void RunNextActorRefAdvertisement();

        // ---- class-manifest compression ----

        /// <summary>Record <paramref name="count"/> observations of a non-empty class manifest from <paramref name="originUid"/>.</summary>
        void HitClassManifest(long originUid, string manifest, int count);

        /// <summary>Resolve a COMPRESSED manifest index against (<paramref name="originUid"/>, <paramref name="tableVersion"/>). Returns <see langword="false"/> for an unknown/stale table.</summary>
        bool TryDecompressClassManifest(long originUid, byte tableVersion, int idx, out string manifest);

        /// <summary>Activate an advertised manifest table once confirmed.</summary>
        void ConfirmClassManifestAdvertisement(long originUid, byte tableVersion);

        /// <summary>Build and advertise (or resend) the next manifest compression table to every live origin.</summary>
        void RunNextClassManifestAdvertisement();

        // ---- lifecycle ----

        /// <summary>Origin UIDs currently tracked (for scheduling/testing).</summary>
        IReadOnlyCollection<long> CurrentOriginUids { get; }

        /// <summary>Drop all compression state and cancel advertisement scheduling for an origin (quarantine / dead peer).</summary>
        void Close(long originUid);
    }

    /// <summary>
    /// INTERNAL API. The disabled path: no counting, no tables, no advertisement. This is the
    /// off-by-default behavior and the fallback whenever compression is turned off in configuration --
    /// the Decoder never sees a COMPRESSED tag because no peer was ever advertised a table.
    /// Mirrors Pekko's <c>NoInboundCompressions</c>.
    /// </summary>
    internal sealed class NoInboundCompressions : IInboundCompressions
    {
        public static readonly NoInboundCompressions Instance = new NoInboundCompressions();

        private NoInboundCompressions() { }

        public void HitActorRef(long originUid, string actorPath, int count) { }

        public bool TryDecompressActorRef(long originUid, byte tableVersion, int idx, out string actorPath)
        {
            if (idx == CompressionTable<string>.NotCompressedId)
                throw new ArgumentException("Attempted decompression of illegal compression id -1.", nameof(idx));
            actorPath = string.Empty;
            return false;
        }

        public void ConfirmActorRefAdvertisement(long originUid, byte tableVersion) { }

        public void RunNextActorRefAdvertisement() { }

        public void HitClassManifest(long originUid, string manifest, int count) { }

        public bool TryDecompressClassManifest(long originUid, byte tableVersion, int idx, out string manifest)
        {
            if (idx == CompressionTable<string>.NotCompressedId)
                throw new ArgumentException("Attempted decompression of illegal compression id -1.", nameof(idx));
            manifest = string.Empty;
            return false;
        }

        public void ConfirmClassManifestAdvertisement(long originUid, byte tableVersion) { }

        public void RunNextClassManifestAdvertisement() { }

        public IReadOnlyCollection<long> CurrentOriginUids => Array.Empty<long>();

        public void Close(long originUid) { }
    }
}
