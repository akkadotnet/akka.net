//-----------------------------------------------------------------------
// <copyright file="ClusterProtocolRecorder.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading;
using Akka.Actor;
using Akka.Event;

namespace Akka.Cluster
{
    /// <summary>
    /// Direction of a recorded cluster membership-protocol interaction, relative to the node that
    /// owns the recorder (the "reference" node in a conformance test).
    /// </summary>
    internal enum ClusterProtocolDirection
    {
        /// <summary>A protocol message this node received from a peer.</summary>
        Inbound,

        /// <summary>A protocol message this node sent to a peer.</summary>
        Outbound
    }

    /// <summary>
    /// A single, structured record of a cluster membership-protocol interaction as observed by the
    /// node that has protocol recording enabled (<c>akka.cluster.protocol-recorder = on</c>).
    /// <para>
    /// These events are published to the system <see cref="EventStream"/> so that a cluster
    /// conformance harness can subscribe and assemble an ordered trace of the wire exchange between
    /// a reference node and a node under test (worker). They are <b>only</b> emitted when recording
    /// is explicitly enabled; with the default configuration the cluster behaves exactly as before
    /// and produces no such events.
    /// </para>
    /// </summary>
    internal sealed class ClusterProtocolEvent
    {
        public ClusterProtocolEvent(
            long sequenceNr,
            long timestampTicks,
            ClusterProtocolDirection direction,
            string kind,
            Address self,
            Address? peer,
            string detail)
        {
            SequenceNr = sequenceNr;
            TimestampTicks = timestampTicks;
            Direction = direction;
            Kind = kind;
            Self = self;
            Peer = peer;
            Detail = detail;
        }

        /// <summary>Monotonically increasing sequence number (per recorder) establishing a total order.</summary>
        public long SequenceNr { get; }

        /// <summary>UTC timestamp, in ticks, of when the interaction was recorded.</summary>
        public long TimestampTicks { get; }

        /// <summary>Whether this node sent or received the message.</summary>
        public ClusterProtocolDirection Direction { get; }

        /// <summary>
        /// Protocol message kind, e.g. <c>InitJoin</c>, <c>InitJoinAck</c>, <c>Join</c>,
        /// <c>Welcome</c>, <c>Leave</c>, <c>ExitingConfirmed</c>, <c>Gossip</c>.
        /// </summary>
        public string Kind { get; }

        /// <summary>The address of the recording (reference) node.</summary>
        public Address Self { get; }

        /// <summary>The address of the peer (node under test) involved, when known.</summary>
        public Address? Peer { get; }

        /// <summary>Free-form, human-readable detail (roles, app version, member count, ...).</summary>
        public string Detail { get; }

        /// <inheritdoc/>
        public override string ToString()
            => $"#{SequenceNr} {Direction,-8} {Kind,-16} self={Self} peer={(Peer is null ? "-" : Peer.ToString())}"
               + (string.IsNullOrEmpty(Detail) ? "" : $" {Detail}");
    }

    /// <summary>
    /// Records cluster membership-protocol interactions seen by a node. A no-op implementation is
    /// used unless recording is enabled, so the normal cluster hot-path carries no overhead.
    /// </summary>
    internal interface IClusterProtocolRecorder
    {
        /// <summary>Whether recording is active. When <c>false</c>, <see cref="Record"/> does nothing.</summary>
        bool Enabled { get; }

        /// <summary>
        /// Records a single protocol interaction. Cheap no-op when recording is disabled.
        /// </summary>
        /// <param name="direction">Whether the message was sent or received by this node.</param>
        /// <param name="kind">The protocol message kind.</param>
        /// <param name="peer">The peer involved, when known.</param>
        /// <param name="detail">Optional human-readable detail.</param>
        void Record(ClusterProtocolDirection direction, string kind, Address? peer, string detail = "");
    }

    /// <summary>
    /// The default recorder: does nothing. The cluster behaves exactly as if no recording code
    /// were present.
    /// </summary>
    internal sealed class NoOpClusterProtocolRecorder : IClusterProtocolRecorder
    {
        public static readonly NoOpClusterProtocolRecorder Instance = new();

        private NoOpClusterProtocolRecorder() { }

        public bool Enabled => false;

        public void Record(ClusterProtocolDirection direction, string kind, Address? peer, string detail = "")
        {
            // intentionally empty
        }
    }

    /// <summary>
    /// A recorder that publishes every interaction to the system <see cref="EventStream"/> (consumed
    /// by the conformance harness) and additionally logs it at INFO under a stable, machine-parseable
    /// prefix (<see cref="LogPrefix"/>) so the exchange is visible in ordinary cluster logs.
    /// </summary>
    internal sealed class EventStreamClusterProtocolRecorder : IClusterProtocolRecorder
    {
        /// <summary>Stable prefix on every logged protocol line; safe to grep for.</summary>
        public const string LogPrefix = "CLUSTER-PROTOCOL";

        private readonly EventStream _eventStream;
        private readonly ILoggingAdapter _log;
        private readonly Address _self;
        private long _sequenceNr;

        public EventStreamClusterProtocolRecorder(EventStream eventStream, ILoggingAdapter log, Address self)
        {
            _eventStream = eventStream;
            _log = log;
            _self = self;
        }

        public bool Enabled => true;

        public void Record(ClusterProtocolDirection direction, string kind, Address? peer, string detail = "")
        {
            var seq = Interlocked.Increment(ref _sequenceNr);
            var evt = new ClusterProtocolEvent(seq, DateTime.UtcNow.Ticks, direction, kind, _self, peer, detail);

            // Published for in-process conformance harnesses to collect into an ordered trace.
            _eventStream.Publish(evt);

            // Also surfaced in normal logs so the protocol exchange is human-visible.
            if (_log.IsInfoEnabled)
                _log.Info("{0} {1}", LogPrefix, evt);
        }
    }

    /// <summary>
    /// Creates the appropriate <see cref="IClusterProtocolRecorder"/> for a node based on the
    /// <c>akka.cluster.protocol-recorder</c> configuration flag (default off).
    /// </summary>
    internal static class ClusterProtocolRecorderFactory
    {
        public const string ConfigPath = "akka.cluster.protocol-recorder";

        public static IClusterProtocolRecorder Create(Cluster cluster, EventStream eventStream, ILoggingAdapter log)
        {
            var enabled = cluster.System.Settings.Config.GetBoolean(ConfigPath, false);
            if (!enabled)
                return NoOpClusterProtocolRecorder.Instance;

            return new EventStreamClusterProtocolRecorder(eventStream, log, cluster.SelfAddress);
        }
    }
}
