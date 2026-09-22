//-----------------------------------------------------------------------
// <copyright file="TestStage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Immutable;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Streams;
using Akka.Streams.Stage;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// Thread-safe mutable blackhole state shared among this transport's test stages
    /// (<see cref="OutboundTestStage"/> / <see cref="InboundTestStage"/>). One instance per
    /// <see cref="ArteryRemoting"/> transport, created ONLY when
    /// <c>akka.remote.artery.advanced.test-mode = on</c> (<see cref="ArterySettings.TestMode"/>) --
    /// with test-mode off neither this state nor any test stage exists at all.
    ///
    /// <para>
    /// <b>Semantics.</b> The state is a map of directed <c>from -&gt; {to}</c> blackhole pairs.
    /// BOTH the outbound and the inbound stage check the SAME key order,
    /// <c>(localAddress, remoteAddress)</c>. Consequences, for a <c>SetThrottle</c> command
    /// applied on node <c>a</c> targeting node <c>b</c>:
    /// <list type="bullet">
    /// <item><description><c>Direction.Send</c> adds <c>(a -&gt; b)</c>, which matches BOTH of
    /// <c>a</c>'s checks -- so <c>a</c> drops its outbound to <c>b</c> AND its inbound from
    /// <c>b</c>: a full bidirectional cut at <c>a</c>.</description></item>
    /// <item><description><c>Direction.Both</c> adds <c>(a -&gt; b)</c> and <c>(b -&gt; a)</c>;
    /// at node <c>a</c> the observable effect is the same full cut.</description></item>
    /// <item><description><c>Direction.Receive</c> adds only <c>(b -&gt; a)</c>, which matches
    /// NEITHER of <c>a</c>'s <c>(a, b)</c>-keyed checks -- a Receive-only blackhole produces no
    /// drops at the commanded node. No in-tree multi-node spec uses Receive-only over artery (the
    /// one that does, <c>TestConductorSpec</c>, is pinned to classic remoting).</description></item>
    /// </list>
    /// This is what lets the TestConductor route a <c>blackhole(a, b, Both)</c> command to node
    /// <c>a</c> ALONE and still produce a symmetric, both-sides-observable partition.
    /// </para>
    ///
    /// <para>
    /// <b>Healed pairs are fully removed.</b> <see cref="PassThrough"/> removes the destination
    /// from the key's set, and once that leaves the key's set empty the key itself is dropped
    /// from the map. That keeps <see cref="AnyBlackholePresent"/> in sync with reality: once every
    /// blackhole a node has ever set has been healed, it returns <see langword="false"/> again and
    /// <see cref="InboundTestStage"/>'s unknown-origin gate stops applying -- so a fresh incarnation
    /// of a peer (a new uid, an unknown origin until its handshake completes) is not blocked by a
    /// blackhole that no longer exists. While at least one blackhole is active anywhere on this
    /// transport, the gate stays on so a still-blackholed peer cannot be routed around by forging a
    /// new incarnation.
    /// </para>
    ///
    /// <para>
    /// <b>Threading.</b> Reads (<see cref="IsBlackhole"/> / <see cref="AnyBlackholePresent"/>, the
    /// per-element hot path when test-mode is on) are a single <c>Volatile.Read</c> of an
    /// immutable snapshot -- lock-free, allocation-free. Writes (rare: one per TestConductor
    /// command) CAS-loop via <c>ImmutableInterlocked.Update</c>.
    /// </para>
    /// </summary>
    internal sealed class SharedTestState
    {
        private ImmutableDictionary<Address, ImmutableHashSet<Address>> _blackholes =
            ImmutableDictionary<Address, ImmutableHashSet<Address>>.Empty;

        /// <summary>
        /// Whether any directed blackhole pair is currently active anywhere in the map. A key
        /// whose destination set has been fully healed is removed rather than left empty (see the
        /// type-level remarks), so this returns <see langword="false"/> once every blackhole this
        /// transport ever set has been passed through.
        /// </summary>
        public bool AnyBlackholePresent() => !Volatile.Read(ref _blackholes).IsEmpty;

        /// <summary>
        /// Whether the directed pair <paramref name="from"/> -&gt; <paramref name="to"/> is
        /// currently blackholed. Both test stages call this with the SAME key order:
        /// <c>(localAddress, remoteAddress)</c>.
        /// </summary>
        public bool IsBlackhole(Address from, Address to) =>
            Volatile.Read(ref _blackholes).TryGetValue(from, out var destinations) && destinations.Contains(to);

        /// <summary>
        /// Enables blackholing between <paramref name="a"/> and <paramref name="b"/> in the given
        /// direction: <c>Send</c> adds <c>(a -&gt; b)</c>, <c>Receive</c> adds <c>(b -&gt; a)</c>,
        /// <c>Both</c> adds both.
        /// </summary>
        public void Blackhole(Address a, Address b, ThrottleTransportAdapter.Direction direction)
        {
            switch (direction)
            {
                case ThrottleTransportAdapter.Direction.Send:
                    AddBlackhole(a, b);
                    break;
                case ThrottleTransportAdapter.Direction.Receive:
                    AddBlackhole(b, a);
                    break;
                case ThrottleTransportAdapter.Direction.Both:
                    AddBlackhole(a, b);
                    AddBlackhole(b, a);
                    break;
            }
        }

        /// <summary>
        /// Reverses <see cref="Blackhole"/> for the given direction. A key whose destination set
        /// becomes empty as a result is removed from the map entirely -- see the type-level
        /// remarks on <see cref="AnyBlackholePresent"/>.
        /// </summary>
        public void PassThrough(Address a, Address b, ThrottleTransportAdapter.Direction direction)
        {
            switch (direction)
            {
                case ThrottleTransportAdapter.Direction.Send:
                    RemoveBlackhole(a, b);
                    break;
                case ThrottleTransportAdapter.Direction.Receive:
                    RemoveBlackhole(b, a);
                    break;
                case ThrottleTransportAdapter.Direction.Both:
                    RemoveBlackhole(a, b);
                    RemoveBlackhole(b, a);
                    break;
            }
        }

        private void AddBlackhole(Address from, Address to) =>
            ImmutableInterlocked.Update(
                ref _blackholes,
                static (map, pair) => map.SetItem(
                    pair.From,
                    map.TryGetValue(pair.From, out var destinations)
                        ? destinations.Add(pair.To)
                        : ImmutableHashSet.Create(pair.To)),
                (From: from, To: to));

        private void RemoveBlackhole(Address from, Address to) =>
            ImmutableInterlocked.Update(
                ref _blackholes,
                static (map, pair) =>
                {
                    if (!map.TryGetValue(pair.From, out var destinations))
                        return map;

                    var updated = destinations.Remove(pair.To);

                    // Drop the key entirely once its destination set is empty so
                    // AnyBlackholePresent() reflects reality instead of leaving stale residue
                    // behind that keeps the unknown-origin gate on forever.
                    return updated.IsEmpty ? map.Remove(pair.From) : map.SetItem(pair.From, updated);
                },
                (From: from, To: to));
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Outbound half of artery test-mode failure injection: drops every outbound envelope while
    /// <c>(localAddress, remoteAddress)</c> is blackholed in the <see cref="SharedTestState"/>,
    /// passes everything through otherwise. Woven into the outbound pipelines ONLY when
    /// <see cref="ArterySettings.TestMode"/> is on -- placement per stream:
    /// <list type="bullet">
    /// <item><description>ordinary (single-lane and per-lane) and large streams: UPSTREAM of
    /// <see cref="OutboundHandshakeStage"/> -- so the handshake stage's own injected
    /// <see cref="HandshakeReq"/>s enter DOWNSTREAM of this stage and are never dropped
    /// here;</description></item>
    /// <item><description>control stream: DOWNSTREAM of <see cref="SystemMessageDeliveryStage"/>,
    /// immediately before the encoder -- system messages must not be dropped before the
    /// SystemMessageDelivery stage, so a blackholed system message is already recorded in the
    /// delivery stage's resend buffer and is re-delivered once <c>PassThrough</c> heals the
    /// link.</description></item>
    /// </list>
    /// </summary>
    internal sealed class OutboundTestStage : GraphStage<FlowShape<IOutboundEnvelope, IOutboundEnvelope>>
    {
        public OutboundTestStage(IOutboundContext context, SharedTestState state)
        {
            Context = context;
            State = state;
            Shape = new FlowShape<IOutboundEnvelope, IOutboundEnvelope>(In, Out);
        }

        public IOutboundContext Context { get; }

        public SharedTestState State { get; }

        public Inlet<IOutboundEnvelope> In { get; } = new("OutboundTestStage.in");
        public Outlet<IOutboundEnvelope> Out { get; } = new("OutboundTestStage.out");

        public override FlowShape<IOutboundEnvelope, IOutboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly OutboundTestStage _stage;

            public Logic(OutboundTestStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var envelope = Grab(_stage.In);
                if (_stage.State.IsBlackhole(_stage.Context.LocalAddress.Address, _stage.Context.RemoteAddress))
                {
                    Log.Debug(
                        "dropping outbound message [{0}] to [{1}] because of blackhole",
                        envelope.Message.GetType(), _stage.Context.RemoteAddress);
                    Pull(_stage.In); // drop message
                }
                else
                {
                    Push(_stage.Out, envelope);
                }
            }

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
        }
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Inbound half of artery test-mode failure injection. Woven into the inbound sink between
    /// the deserializing <see cref="ArteryInboundProcessingStage"/> and
    /// <see cref="InboundHandshakeStage"/> ONLY when <see cref="ArterySettings.TestMode"/> is on.
    ///
    /// <para>
    /// Per-envelope behavior:
    /// <list type="bullet">
    /// <item><description>origin uid resolves to a (handshake-completed) association: drop when
    /// <c>(localAddress, originAddress)</c> is blackholed, else pass;</description></item>
    /// <item><description>origin unknown (handshake not completed) and at least one blackhole is
    /// currently active anywhere on this transport: let a <see cref="HandshakeReq"/> through (we
    /// cannot yet know whether ITS origin is blackholed, and dropping it would wedge legitimate
    /// new associations), drop everything else -- including a
    /// <see cref="HandshakeRsp"/>;</description></item>
    /// <item><description>origin unknown and no blackhole is currently active: pass. This includes
    /// the case where every blackhole this transport ever set has since been healed -- a fresh
    /// incarnation of a peer is not blocked by a blackhole that no longer exists.</description></item>
    /// </list>
    /// The lanes&gt;1 ordinary-connection lane path performs the SAME known-origin check inside
    /// <see cref="ArteryInboundProcessingStage"/> (its lane traffic bypasses this stage; its
    /// unknown-origin traffic is already dropped by the lane path's own
    /// <see cref="IInboundContext.IsKnownOrigin"/> gate, independent of test-mode).
    /// </para>
    /// </summary>
    internal sealed class InboundTestStage : GraphStage<FlowShape<IInboundEnvelope, IInboundEnvelope>>
    {
        public InboundTestStage(IInboundContext context, SharedTestState state)
        {
            Context = context;
            State = state;
            Shape = new FlowShape<IInboundEnvelope, IInboundEnvelope>(In, Out);
        }

        public IInboundContext Context { get; }

        public SharedTestState State { get; }

        public Inlet<IInboundEnvelope> In { get; } = new("InboundTestStage.in");
        public Outlet<IInboundEnvelope> Out { get; } = new("InboundTestStage.out");

        public override FlowShape<IInboundEnvelope, IInboundEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : GraphStageLogic, IInHandler, IOutHandler
        {
            private readonly InboundTestStage _stage;

            public Logic(InboundTestStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandler(stage.In, this);
                SetHandler(stage.Out, this);
            }

            public void OnPush()
            {
                var envelope = Grab(_stage.In);
                var origin = _stage.Context.TryResolveOriginAddress(envelope.OriginUid);

                if (origin is not null)
                {
                    if (_stage.State.IsBlackhole(_stage.Context.LocalAddress.Address, origin))
                    {
                        Log.Debug(
                            "dropping inbound message [{0}] from [{1}] with UID [{2}] because of blackhole",
                            envelope.Message.GetType(), origin, envelope.OriginUid);
                        Pull(_stage.In); // drop message
                    }
                    else
                    {
                        Push(_stage.Out, envelope);
                    }

                    return;
                }

                // Unknown origin -- handshake not completed for this uid.
                if (_stage.State.AnyBlackholePresent())
                {
                    if (envelope.Message is HandshakeReq)
                    {
                        Log.Debug(
                            "inbound message [HandshakeReq] before handshake completed, cannot check if remote " +
                            "is blackholed, letting through");
                        Push(_stage.Out, envelope); // let it through
                    }
                    else
                    {
                        Log.Debug(
                            "dropping inbound message [{0}] with UID [{1}] because of blackhole",
                            envelope.Message.GetType(), envelope.OriginUid);
                        Pull(_stage.In); // drop message
                    }
                }
                else
                {
                    Push(_stage.Out, envelope);
                }
            }

            public void OnPull() => Pull(_stage.In);

            public void OnUpstreamFinish() => CompleteStage();

            public void OnUpstreamFailure(Exception e) => FailStage(e);

            public void OnDownstreamFinish(Exception cause) => InternalOnDownstreamFinish(cause);
        }
    }
}
