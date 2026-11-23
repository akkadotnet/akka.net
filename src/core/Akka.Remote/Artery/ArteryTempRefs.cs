//-----------------------------------------------------------------------
// <copyright file="ArteryTempRefs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using Akka.Actor;
using Akka.Util;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Helpers for temporary Ask refs (<c>&lt;system&gt;/temp/&lt;Base64Encode(counter)&gt;</c>) on the Artery
    /// path: recognising the canonical temp path of a literal, and the lane rule for temp refs.
    ///
    /// <para>
    /// <b>Lane rule.</b> Every representation of the same temp ref -- the numeric record, the literal
    /// path in a frame, and a plain <see cref="RemoteActorRef"/> built from that literal -- selects its
    /// lane from the temp id alone (<see cref="ArteryLaneKey.ForTemporary"/>), so per-recipient ordering
    /// holds regardless of which representation a given sender used. Temp refs carry no uid, so without
    /// this rule they would all ride lane 0 (outbound) or be spread by a string hash (inbound).
    /// </para>
    /// </summary>
    internal static class ArteryTempRefs
    {
        private const string TempSegment = "/temp";

        /// <summary>
        /// Extracts the temp id from a serialized actor path of the form
        /// <c>scheme://system@host:port/temp/&lt;canonical name&gt;</c> (nothing else below the root, no
        /// uid fragment). Allocation-free; returns <see langword="false"/> for every other path.
        /// </summary>
        public static bool TryGetTempIdFromLiteral(string path, out long tempId)
        {
            tempId = -1;
            var lastSlash = path.LastIndexOf('/');
            var segmentStart = lastSlash - TempSegment.Length;
            if (lastSlash <= 0 || lastSlash == path.Length - 1 || segmentStart < 0)
                return false;

            if (string.CompareOrdinal(path, segmentStart, TempSegment, 0, TempSegment.Length) != 0)
                return false;

            // "/temp" must sit directly under the root: the only '/' characters before it are the
            // two of "scheme://".
            var schemeEnd = path.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd < 0 || path.IndexOf('/', schemeEnd + 3) != segmentStart)
                return false;

            return Base64Encoding.TryBase64DecodeLong(path.AsSpan(lastSlash + 1), out tempId);
        }

        /// <summary>
        /// Extracts the temp id from an <see cref="ActorPath"/> of the form <c>/temp/&lt;canonical name&gt;</c>
        /// that carries no uid. Reads only <c>Name</c> and <c>Parent</c>, so it allocates nothing.
        /// </summary>
        public static bool TryGetTempId(ActorPath path, out long tempId)
        {
            tempId = -1;
            if (path.Uid != ActorCell.UndefinedUid)
                return false;

            return path.Parent is { Name: "temp", Parent: RootActorPath }
                && Base64Encoding.TryBase64DecodeLong(path.Name.AsSpan(), out tempId);
        }
    }
}
