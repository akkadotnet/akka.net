//-----------------------------------------------------------------------
// <copyright file="ConformanceModel.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Cluster.Conformance
{
    /// <summary>Where a <see cref="ConformanceEvent"/> originated.</summary>
    public enum ConformanceSource
    {
        /// <summary>A low-level membership-protocol wire message captured by the reference node's recorder.</summary>
        Protocol,

        /// <summary>A high-level membership transition observed on the reference node's cluster event stream.</summary>
        Membership,

        /// <summary>An application-level routing observation (e.g. a reply to a cluster broadcast router).</summary>
        Routing
    }

    /// <summary>Direction of a protocol message relative to the reference node.</summary>
    public enum ConformanceDirection
    {
        /// <summary>Not applicable (e.g. a membership transition).</summary>
        None,

        /// <summary>Received by the reference node from the peer.</summary>
        Inbound,

        /// <summary>Sent by the reference node to the peer.</summary>
        Outbound
    }

    /// <summary>
    /// A single, ordered entry in a conformance trace: either a captured protocol wire message or an
    /// observed membership transition, as seen by the reference seed node.
    /// </summary>
    public sealed class ConformanceEvent
    {
        public ConformanceEvent(
            long seq,
            DateTime timestampUtc,
            ConformanceSource source,
            ConformanceDirection direction,
            string kind,
            Address? peer,
            string detail)
        {
            Seq = seq;
            TimestampUtc = timestampUtc;
            Source = source;
            Direction = direction;
            Kind = kind;
            Peer = peer;
            Detail = detail;
        }

        /// <summary>Arrival order across both sources; establishes a single total order for the trace.</summary>
        public long Seq { get; }

        /// <summary>When the reference node recorded the event.</summary>
        public DateTime TimestampUtc { get; }

        /// <summary>Whether this is a protocol wire message or a membership transition.</summary>
        public ConformanceSource Source { get; }

        /// <summary>For protocol messages, whether the reference node sent or received it.</summary>
        public ConformanceDirection Direction { get; }

        /// <summary>
        /// The kind of event, e.g. <c>InitJoin</c>, <c>Welcome</c>, <c>Gossip</c> (protocol) or
        /// <c>MemberUp</c>, <c>MemberExited</c>, <c>UnreachableMember</c> (membership).
        /// </summary>
        public string Kind { get; }

        /// <summary>The peer (node-under-test) the event concerns, when known.</summary>
        public Address? Peer { get; }

        /// <summary>Free-form detail (roles, version, member count, status, ...).</summary>
        public string Detail { get; }

        /// <inheritdoc/>
        public override string ToString()
        {
            var dir = Direction == ConformanceDirection.None ? "" : Direction.ToString();
            return $"#{Seq,-3} [{Source,-10}] {dir,-8} {Kind,-18} peer={(Peer is null ? "-" : Peer.ToString())}"
                   + (string.IsNullOrEmpty(Detail) ? "" : $" {Detail}");
        }
    }

    /// <summary>
    /// A thread-safe, append-only, ordered collection of <see cref="ConformanceEvent"/>s captured by
    /// a <see cref="ReferenceSeed"/>. A single recorder actor writes; tests read snapshots.
    /// </summary>
    public sealed class ConformanceTrace
    {
        private readonly object _gate = new();
        private readonly List<ConformanceEvent> _events = new();
        private long _seq;

        /// <summary>Appends an event, assigning it the next sequence number. Returns the stored event.</summary>
        internal ConformanceEvent Append(
            ConformanceSource source,
            ConformanceDirection direction,
            string kind,
            Address? peer,
            string detail)
        {
            lock (_gate)
            {
                var evt = new ConformanceEvent(++_seq, DateTime.UtcNow, source, direction, kind, peer, detail);
                _events.Add(evt);
                return evt;
            }
        }

        /// <summary>An ordered snapshot of every event captured so far.</summary>
        public IReadOnlyList<ConformanceEvent> Snapshot()
        {
            lock (_gate)
            {
                return _events.ToList();
            }
        }

        /// <summary>Events whose <see cref="ConformanceEvent.Peer"/> matches <paramref name="peer"/> (by address, ignoring UID).</summary>
        public IReadOnlyList<ConformanceEvent> ForPeer(Address peer)
        {
            lock (_gate)
            {
                return _events.Where(e => Equals(e.Peer, peer)).ToList();
            }
        }

        /// <summary>True if any captured event matches the given kind (and optional peer / source).</summary>
        public bool Has(string kind, Address? peer = null, ConformanceSource? source = null)
        {
            lock (_gate)
            {
                return _events.Any(e =>
                    string.Equals(e.Kind, kind, StringComparison.Ordinal)
                    && (peer is null || Equals(e.Peer, peer))
                    && (source is null || e.Source == source));
            }
        }

        /// <summary>
        /// True if any captured event matches the given kind and direction (and optional peer). Used to
        /// distinguish, e.g., gossip the node-under-test SENT (inbound to the reference node) from gossip
        /// the reference node sent to it.
        /// </summary>
        public bool HasDirected(string kind, ConformanceDirection direction, Address? peer = null)
        {
            lock (_gate)
            {
                return _events.Any(e =>
                    string.Equals(e.Kind, kind, StringComparison.Ordinal)
                    && e.Direction == direction
                    && (peer is null || Equals(e.Peer, peer)));
            }
        }

        /// <summary>
        /// The sequence number of the first event matching <paramref name="kind"/> (and optional peer),
        /// or <c>-1</c> if none. Useful for asserting ordering between protocol phases.
        /// </summary>
        public long FirstSeqOf(string kind, Address? peer = null)
        {
            lock (_gate)
            {
                var match = _events.FirstOrDefault(e =>
                    string.Equals(e.Kind, kind, StringComparison.Ordinal)
                    && (peer is null || Equals(e.Peer, peer)));
                return match?.Seq ?? -1;
            }
        }

        /// <summary>Number of captured events matching <paramref name="kind"/> (and optional peer).</summary>
        public int Count(string kind, Address? peer = null)
        {
            lock (_gate)
            {
                return _events.Count(e =>
                    string.Equals(e.Kind, kind, StringComparison.Ordinal)
                    && (peer is null || Equals(e.Peer, peer)));
            }
        }

        /// <summary>A human-readable, one-event-per-line rendering of the whole trace.</summary>
        public string Render()
        {
            lock (_gate)
            {
                return string.Join(Environment.NewLine, _events.Select(e => e.ToString()));
            }
        }
    }
}
