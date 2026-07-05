//-----------------------------------------------------------------------
// <copyright file="ArteryRemoting.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;

namespace Akka.Remote.Artery
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="RemoteTransport"/> implementation for Artery TCP remoting (EXPERIMENTAL,
    /// under active development -- see <c>openspec/changes/artery-tcp-remoting/design.md</c>).
    ///
    /// <para>
    /// This now hosts task group 7, "Reliable System Messages" (design.md gate G3), on top of task
    /// group 6's control stream: each association materializes TWO independent outbound streams --
    /// ordinary (user messages) and control (handshake, heartbeat, quarantine notice, AND -- new at
    /// group 7 -- reliably-delivered system messages + their Ack/Nack replies) -- each on its own
    /// bounded queue and its own TCP connection (see <see cref="MaterializeOutboundStream"/>). The
    /// DeathWatch triple (Watch/Unwatch/DeathWatchNotification) + Terminate now ride the control
    /// stream through <see cref="SystemMessageDeliveryStage"/> (outbound) /
    /// <see cref="SystemMessageAckerStage"/> (inbound) for exactly-once, strictly-in-order delivery;
    /// every other system message type is unaffected (there are none at this layer -- remote deploy's
    /// <c>DaemonMsgCreate</c> stays an ORDINARY message, per design.md's explicit non-scope). Message
    /// sent -> received -> dispatched to the correct actor; classic remoting is unaffected.
    /// </para>
    ///
    /// <para>
    /// <b>Connection cardinality (verify against design.md).</b> Artery uses SEPARATE per-direction
    /// TCP connections -- there is no single bidirectional "association socket". When system A
    /// first sends to a B-hosted actor, A materializes an OUTBOUND ordinary connection A-&gt;B
    /// (whose <see cref="OutboundHandshakeStage"/> instance routes its <see cref="HandshakeReq"/>
    /// via the control side channel -- see <see cref="EnqueueControl"/>) plus, lazily, an OUTBOUND
    /// CONTROL connection A-&gt;B the first time any control message actually needs sending. B's
    /// <see cref="InboundHandshakeStage"/> replies with a <see cref="HandshakeRsp"/> via
    /// <see cref="IInboundContext.SendControl"/>, which ALSO routes through <see cref="EnqueueControl"/>
    /// keyed by A's address -- i.e. B materializes (or reuses) its OWN outbound CONTROL connection
    /// B-&gt;A to carry the reply. Neither system ever writes to a socket it accepted (inbound);
    /// every direction of every stream type gets its own independently-materialized outbound
    /// connection.
    /// </para>
    /// </summary>
    internal sealed class ArteryRemoting : RemoteTransport, IControlMessageSubscriber
    {
        private readonly ArterySettings _settings;
        private readonly ILoggingAdapter _log;
        private readonly AssociationRegistry _registry = new();

        /// <summary>
        /// Subscribers notified (task 6.2) for every decoded, non-handshake inbound control
        /// message, across every association's control connection. <see cref="ArteryRemoting"/>
        /// subscribes itself in <see cref="Start"/> to handle <see cref="ArteryHeartbeat"/> /
        /// <see cref="ArteryQuarantined"/>; group 7's reliable system-message stages subscribe
        /// here too, once they land.
        /// </summary>
        private ImmutableList<IControlMessageSubscriber> _controlSubscribers = ImmutableList<IControlMessageSubscriber>.Empty;

        private volatile HashSet<Address>? _addresses;
        private volatile Address? _defaultAddress;

        private ActorMaterializer? _materializer;
        private TcpExt? _tcp;
        private Tcp.ServerBinding? _binding;
        private UniqueAddress _localUniqueAddress;
        private AssociationRegistryInboundContext? _inboundContext;

        /// <summary>
        /// The <see cref="ArrayPool{T}"/> every materialized outbound stream's
        /// <see cref="ArteryEncodeStage"/> rents its encode buffers from -- sourced from
        /// <see cref="ArteryTransportSetup.EncodeBufferPool"/> (read once in <see cref="Start"/>).
        /// <see langword="null"/> (production default, and whenever no <see cref="ArteryTransportSetup"/>
        /// is present at all) means <see cref="ArrayPool{T}.Shared"/> -- see
        /// <see cref="ArteryEnvelopeCodec.Encode(Akka.Serialization.Serialization,long,string?,string?,object,ArrayPool{byte}?)"/>'s
        /// own default. Replaces the former mutable static test hook (<c>EncodePoolOverrideForTests</c>)
        /// -- see <see cref="ArteryTransportSetup"/> for why (per-<see cref="ExtendedActorSystem"/>
        /// configuration, not a process-wide static, so concurrently-running tests never race
        /// each other over it).
        /// </summary>
        private ArrayPool<byte>? _encodeBufferPool;

        /// <summary>
        /// Fault-injection test hook (design.md gate G3) -- see <see cref="ArteryTransportSetup.DropOutboundControlMessage"/>.
        /// Read once from <see cref="ArteryTransportSetup"/> in <see cref="Start"/>; <see langword="null"/>
        /// (production default) disables it entirely.
        /// </summary>
        private Func<object, bool>? _dropOutboundControlMessage;

        /// <summary>
        /// Initializes a new instance of the <see cref="ArteryRemoting"/> class.
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="provider">TBD</param>
        public ArteryRemoting(ExtendedActorSystem system, RemoteActorRefProvider provider)
            : base(system, provider)
        {
            _log = Logging.GetLogger(system, "artery");
            _settings = new ArterySettings(system.Settings.Config.GetConfig("akka.remote.artery"));
        }

        /// <inheritdoc/>
        public override ISet<Address> Addresses => _addresses!;

        /// <inheritdoc/>
        public override Address DefaultAddress => _defaultAddress!;

        /// <inheritdoc/>
        public override void Start()
        {
            _log.Info("Starting Artery TCP remoting on [{0}:{1}]", _settings.CanonicalHostname, _settings.CanonicalPort);
            _log.Warning(
                "Artery TCP remoting is EXPERIMENTAL and under active development -- task group 7 (reliable " +
                "system-message delivery: seq/Ack/Nack/resend over the control stream; no lanes/compression yet). " +
                "Do not use in production.");

            _materializer = ActorMaterializer.Create(System);
            _tcp = System.TcpStream();
            var arteryTransportSetup = System.Settings.Setup.Get<ArteryTransportSetup>();
            _encodeBufferPool = arteryTransportSetup.Select(s => s.EncodeBufferPool).GetOrElse(null);
            _dropOutboundControlMessage = arteryTransportSetup.Select(s => s.DropOutboundControlMessage).GetOrElse(null);

            // halfClose: true is essential here, not cosmetic. Every accepted (inbound) connection's
            // WRITE side is `Source.Empty` (Artery uses separate per-direction connections -- see the
            // type-level "Connection cardinality" remarks -- so an accepted connection is read-only
            // and its write side "completes" the instant it materializes). With the Streams TCP
            // default (halfClose: false), that instant write-side completion makes
            // TcpConnectionStage send `Tcp.Close` (fully close, per `TcpStages.cs`'s
            // `onUpstreamFinish`), tearing down the READ side too -- killing the connection out from
            // under the peer within milliseconds of it being accepted. halfClose: true makes it send
            // `Tcp.ConfirmedClose` (FIN on the write half only) instead, keeping the read side open
            // for as long as the peer keeps sending.
            var (bindingTask, _) = _tcp.Bind(_settings.CanonicalHostname, _settings.CanonicalPort, halfClose: true)
                .ToMaterialized(Sink.ForEach<Tcp.IncomingConnection>(HandleIncomingConnection), Keep.Both)
                .Run(_materializer);

            // RemoteTransport.Start() is a synchronous override (the base contract classic Remoting.cs
            // shares) that must not return until DefaultAddress is known -- canonical.port = 0 needs
            // the BOUND ephemeral port, which is only available once the bind Task completes. Classic
            // remoting blocks the exact same way on its own startup promise (Remoting.cs Start(),
            // `addressPromise.Task.Wait(...)`); mirrored here rather than invented. This is the one
            // place in this file where a blocking wait is unavoidable given the synchronous contract.
            if (!bindingTask.Wait(Provider.RemoteSettings.StartupTimeout))
                throw new RemoteTransportException(
                    $"Artery TCP remoting failed to bind to [{_settings.CanonicalHostname}:{_settings.CanonicalPort}] " +
                    $"within {Provider.RemoteSettings.StartupTimeout}.");

            _binding = bindingTask.GetAwaiter().GetResult();
            var boundPort = ((IPEndPoint)_binding.Value.LocalAddress).Port;

            var address = new Address("akka", System.Name, _settings.CanonicalHostname, boundPort);
            _defaultAddress = address;
            _addresses = new HashSet<Address> { address };

            _localUniqueAddress = new UniqueAddress(address, AddressUidExtension.Uid(System));
            _inboundContext = new AssociationRegistryInboundContext(_registry, _localUniqueAddress, SendControlToAddress);

            // Self-subscribe to handle ArteryHeartbeat (reply) and ArteryQuarantined (publish
            // ThisActorSystemQuarantinedEvent) -- see IControlMessageSubscriber.ControlMessageReceived.
            SubscribeControl(this);

            _log.Info("Artery TCP remoting started; listening on [{0}]", address);
        }

        /// <inheritdoc/>
        public override Task Shutdown()
        {
            _log.Info("Shutting down Artery TCP remoting on [{0}]", _defaultAddress);

            foreach (var association in _registry.AllAssociations)
            {
                association.CompleteOutbound();
                association.CompleteControlOutbound();
            }

            var unbindTask = _binding?.Unbind() ?? Task.CompletedTask;
            var materializer = _materializer;

            return unbindTask.ContinueWith(_ =>
            {
                materializer?.Shutdown();
                _log.Info("Artery TCP remoting shut down");
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <inheritdoc/>
        public override void Send(object message, IActorRef sender, RemoteActorRef recipient)
        {
            var remoteAddress = recipient.Path.Address;
            var association = _registry.AssociationFor(remoteAddress);

            // Quarantine gating at the send-routing layer (design.md Invariants; task 6.6, resolved
            // for group 7 below): a quarantined association drops BOTH ordinary AND system-message
            // sends, logged ONCE per association (not per message) -- the sole carve-out is
            // ActorSelectionMessage (design.md "Blocked under quarantine except ActorSelectionMessage
            // / ClearSystemMessageDelivery"). Control HOUSEKEEPING messages (handshake/heartbeat/
            // quarantine-notice/Ack/Nack) never go through Send at all -- they always flow via
            // EnqueueControl -- so this gate cannot affect them; that is how the control channel
            // "pierces quarantine", not an exception carved out here.
            //
            // GROUP7 RESOLVED: ClearSystemMessageDelivery does NOT need a Send()-level pierce in
            // this implementation -- it is issued directly by Quarantine() via EnqueueControl (the
            // SAME path ArteryQuarantined already uses), never through Send, so it is unaffected by
            // this gate by construction. System messages do NOT pierce quarantine either (unlike
            // control housekeeping traffic): once an incarnation's system-message delivery has been
            // quarantined (whether by an external Quarantine() call or by
            // SystemMessageDeliveryStage's own give-up), further Watch/Unwatch/DeathWatchNotification/
            // Terminate sends to that SAME (now-defunct) uid are pointless and are dropped here, same
            // as ordinary messages -- see SystemMessageDeliveryStage's give-up remarks for why this
            // is safe (nothing more will be sent under the given-up incarnation, so its immediate
            // local seqNo/buffer reset cannot desync a still-active peer).
            if (association.CurrentState.UniqueRemoteAddress is { } peer &&
                association.IsQuarantined(peer.Uid) &&
                message is not ActorSelectionMessage)
            {
                if (association.ShouldLogQuarantineDrop(peer.Uid))
                    _log.Warning(
                        "Dropping messages to quarantined association [{0}] (uid [{1}]); further drops for this " +
                        "association/uid will not be logged individually.", remoteAddress, peer.Uid);

                System.DeadLetters.Tell(message, sender);
                return;
            }

            var recipientPath = recipient.Path.ToSerializationFormatWithAddress(remoteAddress);

            if (message is ISystemMessage systemMessage)
            {
                // Reliable system-message delivery (design.md gate G3) rides the CONTROL stream,
                // wrapped by SystemMessageDeliveryStage -- never the ordinary stream/lanes (design.md
                // invariant 5: "system messages NEVER hashed onto ordinary lanes").
                EnqueueSystemMessage(remoteAddress, systemMessage, recipientPath);
                return;
            }

            var senderPath = sender.IsNobody() ? null : sender.Path.ToSerializationFormatWithAddress(DefaultAddress);

            EnqueueOutbound(remoteAddress, message, senderPath, recipientPath);
        }

        /// <inheritdoc/>
        public override Task<bool> ManagementCommand(object cmd) => Task.FromResult(false);

        /// <inheritdoc/>
        public override Task<bool> ManagementCommand(object cmd, CancellationToken cancellationToken) => Task.FromResult(false);

        /// <inheritdoc/>
        public override Address LocalAddressForRemote(Address remote)
        {
            // Artery has exactly one transport address (no per-protocol transport table like
            // classic's DotNetty/TestTransport mapping) -- mirrors classic's RemoteTransportException
            // error style (Remoting.LocalAddressForRemote) for an unsupported protocol/scheme.
            if (remote.Protocol == "akka")
                return DefaultAddress;

            throw new RemoteTransportException(
                $"Cannot find LocalAddressForRemote for protocol [{remote.Protocol}] -- Artery TCP remoting " +
                "only supports the \"akka\" scheme.");
        }

        /// <inheritdoc/>
        public override void Quarantine(Address address, long? uid)
        {
            if (uid is { } u)
            {
                var association = _registry.AssociationFor(address);
                if (association.Quarantine(u))
                {
                    _log.Warning("Quarantined association to [{0}] with uid [{1}]", address, u);
                    System.EventStream.Publish(new QuarantinedEvent(address, u));

                    // Notify the peer over the control stream (design.md task 6.5: "sent on
                    // Quarantine()") -- control "pierces quarantine", so this always flows even
                    // though ordinary sends to `address` are now gated off in Send().
                    EnqueueControl(address, new ArteryQuarantined(_localUniqueAddress, u));

                    // GROUP7 RESOLVED: design.md's "Quarantine (UID-scoped)" calls for sending
                    // ClearSystemMessageDelivery(incarnation) alongside the quarantine notice --
                    // this resets THIS association's OWN outbound SystemMessageDeliveryStage
                    // (seqNo back to 1, unacked buffer emptied) via the SAME control-queue plumbing
                    // ArteryQuarantined just used. It is local-only in this implementation (consumed
                    // by that stage, never forwarded to the wire) -- see ClearSystemMessageDelivery's
                    // type-level remarks for the full rationale/simplification.
                    EnqueueControl(address, new ClearSystemMessageDelivery(association.CurrentState.Incarnation));
                }
            }
            else
            {
                // Full non-uid quarantine semantics (gating without a known uid, matching classic's
                // "stop the current endpoint writer and gate the address" behavior) land at gate G3
                // alongside the control stream + reliable system-message delivery -- see design.md
                // "Reliable system-message delivery (gate G3)". Logging (not throwing) is the
                // reasonable G2 behavior: a uid-less quarantine request must not crash the caller.
                _log.Warning(
                    "Quarantine requested for [{0}] without a uid; full non-uid quarantine semantics land at " +
                    "gate G3. No action taken.", address);
            }
        }

        private void HandleIncomingConnection(Tcp.IncomingConnection connection)
        {
            _log.Debug("Accepted inbound Artery TCP connection from [{0}]", connection.RemoteAddress);

            // Both Ordinary and Control connections feed this SAME inbound shape (task 6.2) --
            // ArteryInboundProcessingStage accepts either preamble; routing downstream is purely
            // by the decoded envelope's IsControl flag, not by which connection carried it.
            // SystemMessageAckerStage (design.md gate G3) sits right after InboundHandshakeStage,
            // mirroring the reference "InboundHandshake -> InboundQuarantineCheck ->
            // [control only: SystemMessageAcker]" pipeline -- it is a no-op pass-through for every
            // element that is not a SystemMessageEnvelope, so composing it unconditionally here
            // (rather than only for control-preamble connections) is correct and simpler.
            var inboundSink = Flow.Create<ReadOnlySequence<byte>>()
                .Via(new ArteryInboundProcessingStage(_settings.MaximumFrameSize, System.Serialization))
                .Via(Flow.FromGraph(new InboundHandshakeStage(_inboundContext!)))
                .Via(Flow.FromGraph(new SystemMessageAckerStage(_inboundContext!)))
                .To(Sink.ForEach<IInboundEnvelope>(DispatchInbound));

            // Every accepted (inbound) connection is read-only: Artery uses SEPARATE per-direction
            // connections, so any reply (a HandshakeRsp, a heartbeat, ...) goes out over a
            // NEW/reused OUTBOUND connection this system originates back towards the peer -- see
            // the type-level "Connection cardinality" remarks. We never write to this socket.
            connection.HandleWith(Flow.FromSinkAndSource(inboundSink, Source.Empty<ReadOnlySequence<byte>>()), _materializer!);
        }

        private void DispatchInbound(IInboundEnvelope env)
        {
            if (env.IsControl)
            {
                // HandshakeReq/HandshakeRsp are consumed entirely inside InboundHandshakeStage and
                // never reach here; any OTHER control message (heartbeat, quarantine notice, ...)
                // is dispatched to the registered IControlMessageSubscribers (task 6.2).
                NotifyControlSubscribers(env.OriginUid, env.Message);
                return;
            }

            if (env.RecipientPath is null)
            {
                _log.Warning("Dropping inbound Artery ordinary-stream envelope with no recipient path, carrying [{0}]", env.Message.GetType());
                return;
            }

            var recipient = Provider.ResolveActorRefWithLocalAddress(env.RecipientPath, DefaultAddress);

            if (env.Message is ISystemMessage systemMessage)
            {
                // Reliable system-message delivery (design.md gate G3): SystemMessageAckerStage has
                // already deduplicated/ordered this -- dispatch via SendSystemMessage, mirroring
                // classic's DefaultMessageDispatcher system-message path, NOT Tell. System messages
                // never carry a sender in practice (RemoteActorRef.SendSystemMessage always sends
                // with sender: null) -- see SystemMessageEnvelope's type-level remarks.
                recipient.SendSystemMessage(systemMessage);
                return;
            }

            var sender = env.SenderPath is { } senderPath
                ? Provider.ResolveActorRefWithLocalAddress(senderPath, DefaultAddress)
                : (IActorRef)System.DeadLetters;

            // Mirrors classic's DefaultMessageDispatcher semantics without depending on classic types:
            // an unresolvable recipient resolves to an EmptyLocalActorRef, whose Tell publishes a
            // DeadLetter automatically.
            recipient.Tell(env.Message, sender);
        }

        /// <summary>
        /// Registers <paramref name="subscriber"/> to be notified (task 6.2) of every decoded
        /// non-handshake inbound control message, across every association. INTERNAL test/group-7
        /// hook -- see <see cref="IControlMessageSubscriber"/>.
        /// </summary>
        internal void SubscribeControl(IControlMessageSubscriber subscriber) =>
            ImmutableInterlocked.Update(ref _controlSubscribers, static (list, s) => list.Add(s), subscriber);

        /// <summary>
        /// Reverses <see cref="SubscribeControl"/>.
        /// </summary>
        internal void UnsubscribeControl(IControlMessageSubscriber subscriber) =>
            ImmutableInterlocked.Update(ref _controlSubscribers, static (list, s) => list.Remove(s), subscriber);

        private void NotifyControlSubscribers(long originUid, object message)
        {
            var subscribers = _controlSubscribers;
            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber.ControlMessageReceived(originUid, message);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Control-message subscriber [{0}] threw while handling [{1}].", subscriber.GetType(), message.GetType());
                }
            }
        }

        /// <inheritdoc/>
        void IControlMessageSubscriber.ControlMessageReceived(long originUid, object message)
        {
            switch (message)
            {
                case ArteryQuarantined quarantined when quarantined.QuarantinedUid == _localUniqueAddress.Uid:
                    // Only act when the notification is about THIS system's CURRENT incarnation --
                    // a notification about a stale/superseded uid must not be acted on (design.md's
                    // UID-scoped invariant, mirrored on the receiving side).
                    _log.Warning(
                        "This system has been quarantined by [{0}] (uid [{1}]).",
                        quarantined.From.Address, quarantined.QuarantinedUid);
                    System.EventStream.Publish(new ThisActorSystemQuarantinedEvent(DefaultAddress, quarantined.From.Address));
                    break;

                case ArteryHeartbeat:
                    // Best-effort reply -- see design.md "Ack/Nack best-effort" invariant analog;
                    // loss is fine, the sender's own idle timer will simply try again.
                    if (_registry.TryGetByUid(originUid) is { } association)
                        EnqueueControl(association.RemoteAddress, new ArteryHeartbeatRsp());
                    break;

                case ArteryHeartbeatRsp:
                    // No action needed at task group 6 -- a missed-heartbeat failure detector is
                    // later (group 7+) work; the Rsp's value today is purely observability (tests
                    // subscribe to see liveness/non-starvation -- see ArteryTransportSpec).
                    break;
            }
        }

        private void SendControlToAddress(Address to, object message) => EnqueueControl(to, message);

        private void EnqueueOutbound(Address remoteAddress, object message, string? senderPath, string? recipientPath)
        {
            var association = _registry.AssociationFor(remoteAddress);
            if (!association.IsOutboundMaterialized)
                association.EnsureOutboundMaterialized(a => MaterializeOutbound(remoteAddress, a));

            if (!association.TryEnqueueOutbound(new OutboundEnvelope(message, senderPath, recipientPath)))
            {
                // Log-once-per-association (mirrors HandleControlOverflow's sibling
                // ShouldLogQuarantineDrop latch): a flooded producer can otherwise overflow this
                // queue thousands of times in a row, and logging (format + write) on EVERY
                // dropped message was itself an amplifier of unrelated ThreadPool starvation
                // observed under CI load -- see AssociationRegistry.ShouldLogOrdinaryOverflowDrop.
                if (association.ShouldLogOrdinaryOverflowDrop(association.CurrentState.UniqueRemoteAddress?.Uid))
                    _log.Warning(
                        "Outbound Artery queue to [{0}] is full (capacity {1}); dropping message of type [{2}] to " +
                        "dead letters. Further drops for this association/uid will not be logged individually.",
                        remoteAddress, Association.DefaultOutboundQueueCapacity, message.GetType());
                System.DeadLetters.Tell(message, ActorRefs.NoSender);
            }
        }

        /// <summary>
        /// Enqueues <paramref name="message"/> onto <paramref name="remoteAddress"/>'s CONTROL
        /// outbound queue for RELIABLE delivery (design.md gate G3): the raw
        /// <see cref="ISystemMessage"/> plus its resolved recipient path travel together (as an
        /// <see cref="OutboundEnvelope"/> whose <see cref="IOutboundEnvelope.RecipientPath"/> is
        /// populated, unlike every other control message) onto the SAME control channel handshake/
        /// heartbeat/quarantine-notice traffic uses. <see cref="SystemMessageDeliveryStage"/> --
        /// materialized ONLY on the control stream -- is what wraps it into a seq-numbered
        /// <see cref="SystemMessageEnvelope"/>; this method never constructs one directly.
        /// </summary>
        private void EnqueueSystemMessage(Address remoteAddress, ISystemMessage message, string recipientPath)
        {
            var association = _registry.AssociationFor(remoteAddress);
            if (!association.IsControlOutboundMaterialized)
                association.EnsureControlOutboundMaterialized(a => MaterializeControlOutbound(remoteAddress, a));

            if (!association.TryEnqueueControl(new OutboundEnvelope(message, null, recipientPath)))
                HandleControlOverflow(remoteAddress, association, message);
        }

        /// <summary>
        /// Enqueues <paramref name="message"/> onto <paramref name="remoteAddress"/>'s CONTROL
        /// outbound queue, materializing that association's control stream on first use (task
        /// group 6, task 6.1). This is the ONE path every HOUSEKEEPING control message travels:
        /// handshake Req/Rsp (via <see cref="OutboundHandshakeStage"/> / <see cref="InboundHandshakeStage"/>),
        /// heartbeats (<see cref="ArteryHeartbeatStage"/>), quarantine notices + <see cref="ClearSystemMessageDelivery"/>
        /// (<see cref="Quarantine"/>), and system-message <see cref="Ack"/>/<see cref="Nack"/> replies
        /// (<see cref="SystemMessageAckerStage"/>, via <see cref="SendControlToAddress"/>) all funnel
        /// through here. See <see cref="EnqueueSystemMessage"/> for the SEPARATE path a raw
        /// <see cref="ISystemMessage"/> destined for reliable delivery takes (also this same queue,
        /// but with its recipient path attached).
        /// </summary>
        private void EnqueueControl(Address remoteAddress, object message)
        {
            // Fault-injection test hook (design.md gate G3 correctness suite -- induced ack loss /
            // DeathWatch-under-loss). Production default is null (disabled) -- see
            // ArteryTransportSetup.DropOutboundControlMessage.
            if (_dropOutboundControlMessage?.Invoke(message) == true)
            {
                _log.Debug(
                    "Test hook: dropping outbound Artery control message of type [{0}] to [{1}] (simulated loss).",
                    message.GetType(), remoteAddress);
                return;
            }

            var association = _registry.AssociationFor(remoteAddress);
            if (!association.IsControlOutboundMaterialized)
                association.EnsureControlOutboundMaterialized(a => MaterializeControlOutbound(remoteAddress, a));

            if (!association.TryEnqueueControl(new OutboundEnvelope(message, null, null)))
                HandleControlOverflow(remoteAddress, association, message);
        }

        /// <summary>
        /// GROUP7 RESOLVED: design.md Decision 7 calls for control/system overflow to QUARANTINE
        /// (not merely drop) -- a control channel backed up enough to overflow (default capacity
        /// 256; low-volume housekeeping traffic plus whatever system-message volume is in flight)
        /// signals real trouble with this association, matching the same "give up, never a silent
        /// drop" philosophy <see cref="SystemMessageDeliveryStage"/>'s OWN (much larger,
        /// reliability-window-sized) internal buffer overflow uses. The overflowing message itself
        /// cannot be queued, so it is logged + dead-lettered either way.
        ///
        /// <para>
        /// <b>Re-entrancy guard.</b> <see cref="Quarantine"/> itself calls back into
        /// <see cref="EnqueueControl"/> (to send <see cref="ArteryQuarantined"/> +
        /// <see cref="ClearSystemMessageDelivery"/>) -- onto the SAME already-full channel, which
        /// would otherwise recurse straight back into this method forever. Only calling
        /// <see cref="Quarantine"/> when the uid is NOT already quarantined breaks the cycle: by the
        /// time <c>Quarantine</c>'s own follow-up <c>EnqueueControl</c> calls (possibly) overflow in
        /// turn, the CAS state flip has already happened, so the second re-entry's guard is false.
        /// </para>
        /// </summary>
        private void HandleControlOverflow(Address remoteAddress, Association association, object message)
        {
            var peer = association.CurrentState.UniqueRemoteAddress;
            var shouldQuarantine = peer is { } p && !association.IsQuarantined(p.Uid);

            _log.Error(
                "Outbound Artery CONTROL queue to [{0}] is full (capacity {1}); dropping control message of " +
                "type [{2}] to dead letters{3}.",
                remoteAddress, Association.DefaultControlQueueCapacity, message.GetType(),
                shouldQuarantine ? " and quarantining the association" : "");
            System.DeadLetters.Tell(message, ActorRefs.NoSender);

            if (shouldQuarantine)
                Quarantine(remoteAddress, peer!.Value.Uid);
        }

        private void MaterializeOutbound(Address remoteAddress, Association association) =>
            MaterializeOutboundStream(remoteAddress, association.OutboundReader, ArteryStreamId.Ordinary);

        private void MaterializeControlOutbound(Address remoteAddress, Association association) =>
            MaterializeOutboundStream(remoteAddress, association.ControlReader, ArteryStreamId.Control);

        /// <summary>
        /// Materializes ONE outbound stream chain -- shared shape for BOTH the ordinary and
        /// control streams (design.md task group 6, task 6.1: "factor the shared shape into a
        /// helper rather than duplicating (both differ only in stream id + channel + handshake
        /// presence)"):
        /// <c>ChannelSource.FromReader(reader) -&gt; [control only: ArteryHeartbeatStage] -&gt;
        /// OutboundHandshakeStage -&gt; encode -&gt; prepend [streamId] preamble -&gt;
        /// Tcp().OutgoingConnection</c>.
        ///
        /// <para>
        /// Every stream -- control AND ordinary -- gets an <see cref="OutboundHandshakeStage"/>
        /// instance (task 6.3: "every stream handshakes"); only <paramref name="streamId"/> ==
        /// <see cref="ArteryStreamId.Control"/>'s instance is told <c>isControlStream: true</c>,
        /// which is what makes IT (and only it) inject its <see cref="HandshakeReq"/> inline onto
        /// its own <see cref="OutboundHandshakeStage.Out"/> -- the ordinary stream's instance
        /// instead routes its Req through <see cref="IOutboundContext.SendControl"/>, i.e. back
        /// through <see cref="EnqueueControl"/>.
        /// </para>
        /// <para>
        /// Faithful-but-minimal (carried over from G2): no reconnect/retry -- if the connection
        /// fails, this association's outbound stream simply ends (a subsequent enqueue call will
        /// keep buffering into the channel, but nothing will ever drain it again until the
        /// process is restarted). Reconnect sophistication remains out of scope.
        /// </para>
        /// </summary>
        private void MaterializeOutboundStream(Address remoteAddress, ChannelReader<IOutboundEnvelope> reader, ArteryStreamId streamId)
        {
            var isControlStream = streamId == ArteryStreamId.Control;

            var outboundContext = new AssociationRegistryOutboundContext(
                _registry,
                _localUniqueAddress,
                remoteAddress,
                sendControl: message => EnqueueControl(remoteAddress, message),
                subscribeControl: SubscribeControl,
                unsubscribeControl: UnsubscribeControl,
                quarantine: (address, uid) => Quarantine(address, uid));

            var handshakeStage = new OutboundHandshakeStage(
                outboundContext, _settings.HandshakeRetryInterval, _settings.HandshakeTimeout,
                _settings.InjectHandshakeInterval, isControlStream: isControlStream);

            var host = remoteAddress.Host
                ?? throw new RemoteTransportException($"Cannot open an Artery {streamId} outbound connection to [{remoteAddress}]: missing host.");
            var port = remoteAddress.Port
                ?? throw new RemoteTransportException($"Cannot open an Artery {streamId} outbound connection to [{remoteAddress}]: missing port.");

            var encodeStage = new ArteryEncodeStage(System.Serialization, _localUniqueAddress.Uid, _encodeBufferPool);

            var source = ChannelSource.FromReader(reader);

            // Heartbeat stage is UPSTREAM of the handshake stage (control stream only) so a
            // self-generated heartbeat is subject to the exact same "hold until handshake
            // completes" gating as any other control-stream element -- see ArteryHeartbeatStage's
            // type-level remarks for why the ordering matters.
            var withHeartbeat = isControlStream
                ? source.Via(Flow.FromGraph(new ArteryHeartbeatStage(_settings.ControlHeartbeatInterval)))
                : source;

            // SystemMessageDeliveryStage (design.md gate G3) is CONTROL-STREAM ONLY (invariant 5:
            // system messages are never hashed onto ordinary lanes) and sits UPSTREAM of the
            // handshake stage -- so a freshly-wrapped SystemMessageEnvelope is gated by handshake
            // completion exactly like every other control-stream element (held behind
            // OutboundHandshakeStage's pendingMessage until the association completes, never
            // dropped) -- see that stage's own type-level placement remarks.
            var withSystemMessageDelivery = isControlStream
                ? withHeartbeat.Via(Flow.FromGraph(new SystemMessageDeliveryStage(
                    outboundContext, _settings.SystemMessageBufferSize, _settings.SystemMessageResendInterval, _settings.GiveUpSystemMessageAfter)))
                : withHeartbeat;

            var frames = withSystemMessageDelivery
                .Via(Flow.FromGraph(handshakeStage))
                .Via(Flow.FromGraph(encodeStage));

            var completion = Source.Single(BuildPreamble(streamId))
                .Concat(frames)
                .Via(_tcp!.OutgoingConnection(host, port))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer!);

            completion.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _log.Warning(
                        t.Exception?.GetBaseException(),
                        "Artery {0} outbound connection to [{1}] failed; this association's {0} outbound stream " +
                        "has ended (no automatic reconnect).", streamId, remoteAddress);
                else
                    _log.Debug("Artery {0} outbound connection to [{1}] completed.", streamId, remoteAddress);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private static ReadOnlySequence<byte> BuildPreamble(ArteryStreamId streamId)
        {
            var buffer = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(buffer, streamId);
            return new ReadOnlySequence<byte>(buffer);
        }
    }
}
