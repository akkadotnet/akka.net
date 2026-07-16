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
    /// (<see cref="OutboundTestStage"/> / <see cref="InboundTestStage"/>), the port of Pekko's
    /// <c>SharedTestState</c> (<c>remote/.../artery/TestStage.scala</c>). One instance per
    /// <see cref="ArteryRemoting"/> transport, created ONLY when
    /// <c>akka.remote.artery.advanced.test-mode = on</c> (<see cref="ArterySettings.TestMode"/>) --
    /// with test-mode off neither this state nor any test stage exists at all.
    ///
    /// <para>
    /// <b>Semantics (verbatim Pekko parity -- do not "fix").</b> The state is a map of directed
    /// <c>from -&gt; {to}</c> blackhole pairs. BOTH the outbound and the inbound stage check the
    /// SAME key order, <c>(localAddress, remoteAddress)</c> -- see
    /// <c>TestStage.scala</c>'s <c>OutboundTestStage</c>/<c>InboundTestStage</c>. Consequences,
    /// for a <c>SetThrottle</c> command applied on node <c>a</c> targeting node <c>b</c>:
    /// <list type="bullet">
    /// <item><description><c>Direction.Send</c> adds <c>(a -&gt; b)</c>, which matches BOTH of
    /// <c>a</c>'s checks -- so <c>a</c> drops its outbound to <c>b</c> AND its inbound from
    /// <c>b</c>: a full bidirectional cut at <c>a</c>.</description></item>
    /// <item><description><c>Direction.Both</c> adds <c>(a -&gt; b)</c> and <c>(b -&gt; a)</c>;
    /// at node <c>a</c> the observable effect is the same full cut.</description></item>
    /// <item><description><c>Direction.Receive</c> adds only <c>(b -&gt; a)</c>, which matches
    /// NEITHER of <c>a</c>'s <c>(a, b)</c>-keyed checks -- a Receive-only blackhole produces no
    /// drops at the commanded node. This mirrors Pekko exactly; no in-tree multi-node spec uses
    /// Receive-only over artery (the one that does, <c>TestConductorSpec</c>, is pinned to
    /// classic remoting).</description></item>
    /// </list>
    /// This is what lets the TestConductor route a <c>blackhole(a, b, Both)</c> command to node
    /// <c>a</c> ALONE and still produce a symmetric, both-sides-observable partition.
    /// </para>
    ///
    /// <para>
    /// <b>Empty sets are retained (Pekko parity).</b> <see cref="PassThrough"/> removes the
    /// destination from the key's set but keeps the (now possibly empty) key in the map, exactly
    /// like Pekko's <c>removeBlackhole</c> (<c>blackholes.updated(from, destinations - to)</c>) --
    /// so <see cref="AnyBlackholePresent"/> stays <see langword="true"/> once any blackhole has
    /// ever been set, and <see cref="InboundTestStage"/>'s unknown-origin gating stays active for
    /// the remainder of the test run.
    /// </para>
    ///
    /// <para>
    /// <b>Threading.</b> Reads (<see cref="IsBlackhole"/> / <see cref="AnyBlackholePresent"/>, the
    /// per-element hot path when test-mode is on) are a single <c>Volatile.Read</c> of an
    /// immutable snapshot -- lock-free, allocation-free. Writes (rare: one per TestConductor
    /// command) CAS-loop via <c>ImmutableInterlocked.Update</c>, the .NET analog of Pekko's
    /// <c>AtomicReference</c> + <c>@tailrec</c> compareAndSet loops.
    /// </para>
    /// </summary>
    internal sealed class SharedTestState
    {
        private ImmutableDictionary<Address, ImmutableHashSet<Address>> _blackholes =
            ImmutableDictionary<Address, ImmutableHashSet<Address>>.Empty;

        /// <summary>
        /// Whether ANY blackhole entry (including a healed key retaining an empty set -- see the
        /// type-level "empty sets are retained" remarks) has ever been registered.
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
        /// direction (port of Pekko's <c>SharedTestState.blackhole</c>): <c>Send</c> adds
        /// <c>(a -&gt; b)</c>, <c>Receive</c> adds <c>(b -&gt; a)</c>, <c>Both</c> adds both.
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
        /// Reverses <see cref="Blackhole"/> for the given direction (port of Pekko's
        /// <c>SharedTestState.passThrough</c>). The key itself is retained (possibly with an empty
        /// set) -- see the type-level remarks.
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
                static (map, pair) => map.TryGetValue(pair.From, out var destinations)
                    ? map.SetItem(pair.From, destinations.Remove(pair.To))
                    : map,
                (From: from, To: to));
    }

    /// <summary>
    /// INTERNAL API.
    ///
    /// Outbound half of artery test-mode failure injection (port of Pekko's
    /// <c>OutboundTestStage</c>, <c>TestStage.scala</c>): drops every outbound envelope while
    /// <c>(localAddress, remoteAddress)</c> is blackholed in the <see cref="SharedTestState"/>,
    /// passes everything through otherwise. Woven into the outbound pipelines ONLY when
    /// <see cref="ArterySettings.TestMode"/> is on -- placement per stream (Pekko-faithful):
    /// <list type="bullet">
    /// <item><description>ordinary (single-lane and per-lane) and large streams: UPSTREAM of
    /// <see cref="OutboundHandshakeStage"/> (Pekko <c>Association.scala</c>'s
    /// <c>runOutboundOrdinaryMessagesStream</c>/<c>runOutboundLargeMessagesStream</c>) -- so the
    /// handshake stage's own injected <see cref="HandshakeReq"/>s enter DOWNSTREAM of this stage
    /// and are never dropped here;</description></item>
    /// <item><description>control stream: DOWNSTREAM of <see cref="SystemMessageDeliveryStage"/>,
    /// immediately before the encoder (Pekko <c>ArteryTransport.outboundControl</c>: "system
    /// messages must not be dropped before the SystemMessageDelivery stage") -- a blackholed
    /// system message is already recorded in the delivery stage's resend buffer, so it is
    /// re-delivered once <c>PassThrough</c> heals the link.</description></item>
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
    /// Inbound half of artery test-mode failure injection (port of Pekko's
    /// <c>InboundTestStage</c>, <c>TestStage.scala</c>). Woven into the inbound sink between the
    /// deserializing <see cref="ArteryInboundProcessingStage"/> and
    /// <see cref="InboundHandshakeStage"/> (Pekko: after <c>createDeserializer</c>, before
    /// <c>InboundHandshake</c>) ONLY when <see cref="ArterySettings.TestMode"/> is on.
    ///
    /// <para>
    /// Per-envelope behavior (verbatim Pekko parity):
    /// <list type="bullet">
    /// <item><description>origin uid resolves to a (handshake-completed) association: drop when
    /// <c>(localAddress, originAddress)</c> is blackholed, else pass;</description></item>
    /// <item><description>origin unknown (handshake not completed) and any blackhole has ever been
    /// present: let a <see cref="HandshakeReq"/> through (we cannot yet know whether ITS origin is
    /// blackholed, and dropping it would wedge legitimate new associations), drop everything else
    /// -- including a <see cref="HandshakeRsp"/>, exactly like Pekko;</description></item>
    /// <item><description>origin unknown and no blackhole has ever been present: pass.</description></item>
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
