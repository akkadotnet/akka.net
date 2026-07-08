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
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using Inet = Akka.IO.Inet;

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
    ///
    /// <para>
    /// <b>Reconnect (design.md group 9, "Association outbound-stream lifecycle: reconnect").</b>
    /// An outbound stream's TCP connection is no longer a one-shot affair: when either stream
    /// (ordinary or control) terminates for any reason -- other than this system's own
    /// <see cref="Shutdown"/> -- <see cref="ScheduleOutboundRestart"/> resets that stream's
    /// materialize-once gate and schedules re-materialization after <c>outbound-restart-backoff</c>
    /// (unlimited retries, fixed backoff, no restart-count give-up). The CONTROL stream always
    /// restarts (it pierces quarantine); the ORDINARY stream does not restart while the
    /// association's CURRENT peer uid is quarantined (<see cref="Send"/> already gates ordinary
    /// sends for that uid, so reconnecting would only waste a connection). See
    /// <see cref="MaterializeOutboundStream"/>'s "Reconnect" remarks for the full mechanism.
    /// </para>
    /// </summary>
    internal sealed class ArteryRemoting : RemoteTransport, IControlMessageSubscriber
    {
        private readonly ArterySettings _settings;
        private readonly ILoggingAdapter _log;
        private readonly AssociationRegistry _registry = new();

        /// <summary>
        /// Test-observability accessor for <see cref="_registry"/> (design.md task 8.5, "slow
        /// receiver tests proving queues do not grow unbounded"): lets tests reach a live
        /// association's <see cref="Association.OutboundQueueCount"/>/<see cref="Association.ControlQueueCount"/>,
        /// and (via <see cref="AssociationRegistry.CompleteHandshake"/>) fake a completed handshake
        /// against a peer that will never actually respond, without needing a second real, reachable
        /// <see cref="ArteryRemoting"/> instance on the wire. Production code never reads this --
        /// every production access to associations goes through the instance methods above that
        /// already close over <see cref="_registry"/>.
        /// </summary>
        internal AssociationRegistry Registry => _registry;

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

        // Transport-wide shutdown guard + teardown, mirroring Pekko's ArteryTransport (its
        // `hasBeenShutdown` AtomicBoolean + the shared "transportKillSwitch"). _isShutdown is set
        // FIRST in Shutdown() so no NEW outbound stream is materialized once teardown begins (see the
        // guard at the top of MaterializeOutboundStream). _killSwitch is woven into EVERY inbound and
        // outbound stream graph, so a single Shutdown() on it tears them all down at once. Like Pekko,
        // we deliberately do NOT call _materializer.Shutdown() -- the kill switch stops the streams and
        // the ActorSystem lifecycle reclaims the materializer; force-shutting the materializer down was
        // exactly what raced a late materialization into an IllegalStateException.
        private volatile bool _isShutdown;
        private readonly SharedKillSwitch _killSwitch = KillSwitches.Shared("arteryTransportKillSwitch");
        private UniqueAddress _localUniqueAddress;
        private AssociationRegistryInboundContext? _inboundContext;

        /// <summary>
        /// The <see cref="ArrayPool{T}"/> every materialized outbound stream's
        /// <see cref="ArteryEncodeStage"/> rents its encode buffers from -- sourced from
        /// <see cref="ArteryTransportSetup.EncodeBufferPool"/> (read once in <see cref="Start"/>).
        /// When no override is supplied (the production default) this is a transport-scoped
        /// <see cref="ArrayPool{T}.Create()"/> instance rather than <see cref="ArrayPool{T}.Shared"/>:
        /// the encode buffer is rented on the materialization thread and returned on the TCP write
        /// thread, and a dedicated per-transport pool avoids thrashing <see cref="ArrayPool{T}.Shared"/>'s
        /// per-core buckets with that cross-thread traffic (see <see cref="Start"/> for the full
        /// rationale). Replaces the former mutable static test hook (<c>EncodePoolOverrideForTests</c>)
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
        /// Applied to EVERY Artery socket: the accepting <c>Tcp.Bind</c> and both outbound
        /// <c>Tcp.OutgoingConnection</c> call sites in <see cref="MaterializeOutboundStream"/>.
        /// Explicitly-pinned large socket buffers prevent the kernel shrinking the receiver's
        /// window below loopback's MSS under memory pressure, which springs a sender-side
        /// silly-window-syndrome stall (rwnd_limited forever, observed as an intermittent
        /// benchmark wedge; see ss evidence: notsent+persist-timer with all app layers idle).
        /// Pinning &gt;&gt; MSS makes the trap unreachable.
        /// </summary>
        private static readonly IImmutableList<Inet.SocketOption> ArterySocketOptions =
            ImmutableList.Create<Inet.SocketOption>(
                new Inet.SO.ReceiveBufferSize(1024 * 1024),
                new Inet.SO.SendBufferSize(1024 * 1024));

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
            // Default the encode pool to a transport-scoped ArrayPool<byte>.Create() instance rather
            // than ArrayPool<byte>.Shared (which is what a null value resolves to downstream). Shared is
            // a single process-wide pool; the outbound encode path rents on the stream's materialization
            // thread and returns on the TCP write thread, so under load the cross-thread rent/return
            // traffic thrashes Shared's per-core buckets (measured ~10% throughput loss). A dedicated
            // per-transport pool isolates that traffic. Created ONCE here in Start() (not per outbound
            // connection -- MaterializeOutboundStream reads this field), so every outbound lane in this
            // transport shares the one instance. A test-injected ArteryTransportSetup.EncodeBufferPool
            // (e.g. the poison pool) still overrides this.
            _encodeBufferPool = arteryTransportSetup.Select(s => s.EncodeBufferPool).GetOrElse(null)
                                ?? ArrayPool<byte>.Create();
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
            var (bindingTask, _) = _tcp.Bind(_settings.CanonicalHostname, _settings.CanonicalPort,
                    options: ArterySocketOptions, halfClose: true)
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
            // Set the guard FIRST (mirrors Pekko's hasBeenShutdown.compareAndSet at the top of
            // shutdown()): from here on MaterializeOutboundStream refuses to start new streams, so a
            // late system message racing termination can no longer trigger a materialization.
            _isShutdown = true;
            _log.Info("Shutting down Artery TCP remoting on [{0}]", _defaultAddress);

            // Complete the outbound queues so their consumers finish gracefully and no restart is
            // scheduled (CompleteOutbound also latches the per-association shutdown flags).
            foreach (var association in _registry.AllAssociations)
            {
                association.CompleteOutbound();
                association.CompleteControlOutbound();
            }

            // Tear every remaining stream down via the shared kill switch first (every inbound and
            // outbound graph is woven through it) -- the graceful path, mirroring Pekko's
            // transportKillSwitch abort.
            _killSwitch.Shutdown();

            // ...then REAP the materializer. The kill switch alone is NOT sufficient: a stage parked
            // on an EXTERNAL signal (e.g. the TCP write stage awaiting a WriteAck from a connection
            // actor that died with the ack unsent) never processes the kill switch's completion and
            // sits parked forever -- its ActorGraphInterpreter can then never stop, the /system
            // guardian can never terminate, and ActorSystem.Terminate() hangs until CoordinatedShutdown's
            // actor-system-terminate phase times out (observed: 10s per system + zombie systems whose
            // remote-watchers kept firing into subsequent benchmark rounds, with ~31 leaked interpreter
            // actors in the heap). Materializer.Shutdown() force-stops those interpreters. This is
            // SAFE against the late-materialization IllegalStateException race that originally
            // motivated removing it, because _isShutdown was set FIRST (above) and
            // MaterializeOutboundStream both guards on _materializer.IsShutdown and catches the
            // residual race around Run().
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
                .Via(_killSwitch.Flow<ReadOnlySequence<byte>>())
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
                // isRestart is derived from the gate's OWN history (design.md group 9), not a
                // literal here -- this on-demand path can race ScheduleOutboundRestart's scheduled
                // callback for who actually wins EnsureOutboundMaterialized after a reset, and
                // BOTH must agree on whether a fresh handshake is required (see
                // Association.HasOutboundEverRestarted's remarks).
                association.EnsureOutboundMaterialized(a => MaterializeOutbound(remoteAddress, a, isRestart: a.HasOutboundEverRestarted));

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
                association.EnsureControlOutboundMaterialized(a => MaterializeControlOutbound(remoteAddress, a, isRestart: a.HasControlEverRestarted));

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
                association.EnsureControlOutboundMaterialized(a => MaterializeControlOutbound(remoteAddress, a, isRestart: a.HasControlEverRestarted));

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

        private void MaterializeOutbound(Address remoteAddress, Association association, bool isRestart = false) =>
            MaterializeOutboundStream(remoteAddress, association, ArteryStreamId.Ordinary, isRestart);

        private void MaterializeControlOutbound(Address remoteAddress, Association association, bool isRestart = false) =>
            MaterializeOutboundStream(remoteAddress, association, ArteryStreamId.Control, isRestart);

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
        /// <b>Reconnect (design.md group 9, "Association outbound-stream lifecycle: reconnect").</b>
        /// The channel <paramref name="association"/> exposes (<see cref="Association.OutboundReader"/>/
        /// <see cref="Association.ControlReader"/>) is Association-owned and outlives any single
        /// materialization -- so when THIS materialization's completion Task settles (for ANY
        /// reason: connection refused/reset, <see cref="HandshakeTimeoutException"/>, write
        /// failure, or even a graceful peer-side close), <see cref="ScheduleOutboundRestart"/>
        /// resets that stream's materialize-once gate and schedules a fresh call back into THIS
        /// SAME method after <c>outbound-restart-backoff</c> -- <see cref="ChannelSource.FromReader{T}"/>
        /// re-attaches a NEW consumer to the SAME channel, so any envelope enqueued but not yet
        /// dequeued by the old (now-dead) consumer is still there waiting (the "queue survives,
        /// consumer restarts" invariant this design has relied on since G2). Guarded against
        /// restarting after this system's own transport <see cref="Shutdown"/> and, for the
        /// ORDINARY stream only, against restarting while the CURRENT peer uid is quarantined --
        /// see <see cref="Association.ShouldRestartOutbound"/>/<see cref="Association.ShouldRestartControl"/>.
        /// </para>
        /// <para>
        /// <paramref name="isRestart"/> (design.md group 9) is <see langword="true"/> when THIS
        /// materialization is (or could be) a reconnect -- it forces <see cref="OutboundHandshakeStage"/>
        /// to always send a fresh <see cref="HandshakeReq"/> rather than trusting stale "already
        /// associated" state left over from a possibly-since-restarted peer -- see
        /// <see cref="OutboundHandshakeStage.ForceReqOnStart"/>'s remarks for why this is required
        /// for correctness (not merely defensive). Every caller derives this from
        /// <see cref="Association.HasOutboundEverRestarted"/>/<see cref="Association.HasControlEverRestarted"/>
        /// AT THE MOMENT its <c>EnsureOutboundMaterialized</c>/<c>EnsureControlOutboundMaterialized</c>
        /// callback actually runs (never a hardcoded literal) -- <see cref="ScheduleOutboundRestart"/>'s
        /// scheduled callback is not the ONLY caller that can win the race to materialize after a
        /// reset; an ordinary producer's on-demand enqueue call can too, and both must agree.
        /// </para>
        /// </summary>
        private void MaterializeOutboundStream(Address remoteAddress, Association association, ArteryStreamId streamId, bool isRestart = false)
        {
            // Transport is tearing down: do not materialize a new stream. A late system message (e.g.
            // RemoteWatcher's final Unwatch during CoordinatedShutdown) can otherwise reach here after
            // teardown has begun. Mirrors Pekko's `if (transport.isShutdown) throw ShuttingDown` guard
            // before run() (Association.scala) -- but we RETURN quietly rather than throw, since our
            // caller (RemoteActorRef.SendSystemMessage) logs a thrown exception as a noisy ERROR. We
            // ALSO check the materializer itself: unlike Pekko, our ActorMaterializer.Create(System) is
            // reclaimed by the ActorSystem's OWN teardown (its StreamSupervisor.PostStop flips
            // IsShutdown) independently of _isShutdown, so it can already be dead here while _isShutdown
            // is still false. The message stays in the association-owned channel undelivered -- correct,
            // the transport is going away. The residual race (materializer reclaimed between this check
            // and Run() below) is caught around Run().
            if (_isShutdown || _materializer is null || _materializer.IsShutdown)
                return;

            var isControlStream = streamId == ArteryStreamId.Control;
            var reader = isControlStream ? association.ControlReader : association.OutboundReader;

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
                _settings.InjectHandshakeInterval, isControlStream: isControlStream, forceReqOnStart: isRestart);

            var host = remoteAddress.Host
                ?? throw new RemoteTransportException($"Cannot open an Artery {streamId} outbound connection to [{remoteAddress}]: missing host.");
            var port = remoteAddress.Port
                ?? throw new RemoteTransportException($"Cannot open an Artery {streamId} outbound connection to [{remoteAddress}]: missing port.");

            // The (string host, int port) OutgoingConnection convenience overload does not accept
            // socket options, so build the EndPoint ourselves (mirrors Streams.Dsl.Tcp's own
            // internal CreateEndpoint, which isn't visible from this assembly) to reach the
            // overload that does -- see ArterySocketOptions.
            var remoteEndpoint = IPAddress.TryParse(host, out var parsedHost)
                ? (EndPoint)new IPEndPoint(parsedHost, port)
                : new DnsEndPoint(host, port);

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
            // dropped) -- see that stage's own type-level placement remarks. The Association-owned
            // SystemMessageDeliveryState (design.md group 9 invariant 3) is passed in so a
            // restarted materialization attaches to the SAME unacked buffer/seqNo, instead of
            // starting from empty -- see that state type's remarks.
            var withSystemMessageDelivery = isControlStream
                ? withHeartbeat.Via(Flow.FromGraph(new SystemMessageDeliveryStage(
                    outboundContext, association.SystemMessageDeliveryState, _settings.SystemMessageBufferSize,
                    _settings.SystemMessageResendInterval, _settings.GiveUpSystemMessageAfter)))
                : withHeartbeat;

            var frames = withSystemMessageDelivery
                .Via(Flow.FromGraph(handshakeStage))
                .Via(Flow.FromGraph(encodeStage));

            // TERMINATION SIGNAL (design.md group 9 -- empirically corrected from the design's
            // first-draft "RunWith result / Sink.Ignore task" wording; see the type-level
            // "Reconnect" remarks and the group 9 report for the full story). Artery's outbound
            // connections are ONE-WAY BY DESIGN (see the type-level "Connection cardinality"
            // remarks): the PEER's accepted (inbound) counterpart always writes `Source.Empty`
            // (see `HandleIncomingConnection`), which completes the INSTANT it materializes. That
            // makes THIS connection's READ side hit EOF almost immediately after every single
            // connect -- healthy or not -- so `Sink.Ignore`'s own materialized Task (which only
            // tracks that READ side) resolves near-instantly on EVERY materialization, including
            // perfectly healthy ones, which would busy-loop-restart a fine connection forever.
            // `WatchTermination` placed on the WRITE side (the `frames` source, upstream of
            // `OutgoingConnection`) instead reports the thing group 9 actually needs: it resolves
            // ONLY when the association's own channel completes (this system's `Shutdown` calling
            // `CompleteOutbound`/`CompleteControlOutbound` -- a deliberate, non-restart-worthy
            // completion) or when the WRITE direction genuinely fails/gets cancelled downstream
            // (a real connection failure) -- never merely because the read side (which nothing
            // ever writes to) reached EOF.
            // Woven through the transport-wide kill switch (same instance as the inbound streams) so
            // Shutdown() tears every outbound stream down at once -- see _killSwitch. Placed at the
            // head of the write side so an abort/shutdown propagates down through encode ->
            // OutgoingConnection and closes the socket.
            var preambleAndFrames = Source.Single(BuildPreamble(streamId)).Concat(frames)
                .Via(_killSwitch.Flow<ReadOnlySequence<byte>>());

            // The ORDINARY stream is fitted with a KillSwitch that is published to the Association so
            // the CONTROL stream -- which detects peer death RELIABLY via its periodic heartbeat,
            // unlike the keep-alive-less ordinary stream -- can drive it down when control's own
            // connection fails, instead of leaving an idle ordinary stream stranded on a dead socket
            // (design.md group 9's canonical reconnect fix; see Association._outboundKillSwitch). The
            // control stream itself needs no such switch -- it IS the reliable detector. Control also
            // captures its OutgoingConnection materialized task: when that connection is ESTABLISHED
            // it arms the once-per-death ordinary trip (MarkControlHealthy); a connection-refused
            // reconnect attempt faults that task instead, so the edge-detector stays disarmed and the
            // ordinary stream is not churned during a still-dead-peer reconnect loop.
            // null! satisfies definite-assignment: the catch below always returns, so terminationWatch
            // is only read past this block when the try assigned it.
            Task terminationWatch = null!;
            try
            {
                if (isControlStream)
                {
                    Task connectionTask;
                    ((terminationWatch, connectionTask), _) = preambleAndFrames
                        .WatchTermination(Keep.Right)
                        .ViaMaterialized(_tcp!.OutgoingConnection(remoteEndpoint, options: ArterySocketOptions), Keep.Both)
                        .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both)
                        .Run(_materializer!);

                    connectionTask.ContinueWith(ct =>
                    {
                        if (ct.IsCompletedSuccessfully)
                            association.MarkControlHealthy();
                    }, TaskContinuationOptions.ExecuteSynchronously);
                }
                else
                {
                    UniqueKillSwitch killSwitch;
                    ((killSwitch, terminationWatch), _) = preambleAndFrames
                        .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Right)
                        .WatchTermination(Keep.Both)
                        .Via(_tcp!.OutgoingConnection(remoteEndpoint, options: ArterySocketOptions))
                        .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both)
                        .Run(_materializer!);
                    association.SetOutboundKillSwitch(killSwitch);
                }
            }
            catch (Akka.Pattern.IllegalStateException) when (_isShutdown || _materializer is null || _materializer.IsShutdown)
            {
                // Lost the race with teardown: the ActorSystem reclaimed the materializer (its
                // StreamSupervisor stopped) between the guard at the top of this method and Run() here,
                // so Materialize() threw. The transport is going away -- drop quietly. Gated on an
                // actually-shut-down materializer so a genuine IllegalStateException from a live
                // materializer still propagates.
                _log.Debug("Artery {0} outbound stream to [{1}] not materialized: materializer is shutting down.", streamId, remoteAddress);
                return;
            }

            terminationWatch.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _log.Warning(
                        t.Exception?.GetBaseException(),
                        "Artery {0} outbound connection to [{1}] failed; this association's {0} outbound stream " +
                        "has ended -- reconnect will be attempted per outbound-restart-backoff unless shut down " +
                        "or (ordinary only) quarantined.", streamId, remoteAddress);
                else
                    _log.Debug(
                        "Artery {0} outbound connection to [{1}] completed; reconnect will be attempted per " +
                        "outbound-restart-backoff unless shut down or (ordinary only) quarantined.", streamId, remoteAddress);

                // GROUP 9 canonical reconnect fix: when the CONTROL stream's connection genuinely
                // FAILS after having been ESTABLISHED (t.IsFaulted AND TryConsumeControlHealthy --
                // edge-triggered, once per death; a graceful shutdown-completion never faults, and a
                // connection-refused reconnect attempt against a still-dead peer never armed the
                // detector), drive the ORDINARY stream down ONCE so it reconnects alongside control
                // rather than lingering on a dead socket after a single ordinary write failed to
                // surface the death. Firing only on the edge avoids churning a healthy ordinary
                // consumer mid-handshake against the revived peer. Idempotent + null-safe when the
                // ordinary stream is not currently materialized. See Association._outboundKillSwitch.
                if (isControlStream && t.IsFaulted && association.TryConsumeControlHealthy())
                    association.TripOutboundKillSwitch();

                ScheduleOutboundRestart(remoteAddress, association, streamId);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Design.md group 9, "Association outbound-stream lifecycle: reconnect": called every
        /// time <paramref name="streamId"/>'s outbound stream for <paramref name="association"/>
        /// terminates (see <see cref="MaterializeOutboundStream"/>'s completion continuation).
        /// Resets that stream's materialize-once gate and schedules exactly one re-materialization
        /// call after <c>outbound-restart-backoff</c>, via <see cref="Actor.IActionScheduler.ScheduleOnce(TimeSpan, Action)"/>
        /// (the system scheduler -- never a raw <c>Thread</c>/<c>Task.Delay</c> loop). Retries are
        /// unlimited at this fixed backoff -- there is deliberately no restart-count give-up (see
        /// design.md's rationale: the association's own reliability give-up, plus quarantine
        /// gating at <see cref="Send"/>, already provide termination where it matters).
        ///
        /// <para>
        /// Both the pre-schedule AND the post-backoff checks re-consult
        /// <see cref="Association.ShouldRestartOutbound"/>/<see cref="Association.ShouldRestartControl"/>
        /// -- this system's own <see cref="Shutdown"/> or (ordinary stream only) a quarantine of the
        /// current peer uid may happen at any point during the backoff window, and must still take
        /// effect even though the gate was already reset.
        /// </para>
        /// </summary>
        private void ScheduleOutboundRestart(Address remoteAddress, Association association, ArteryStreamId streamId)
        {
            if (streamId == ArteryStreamId.Control)
            {
                if (!association.ShouldRestartControl())
                    return;

                association.ResetControlGate();
                System.Scheduler.Advanced.ScheduleOnce(_settings.OutboundRestartBackoff, () =>
                {
                    if (!association.ShouldRestartControl())
                        return;

                    association.EnsureControlOutboundMaterialized(a => MaterializeControlOutbound(remoteAddress, a, isRestart: a.HasControlEverRestarted));
                });

                return;
            }

            if (!association.ShouldRestartOutbound())
                return;

            association.ResetOutboundGate();
            System.Scheduler.Advanced.ScheduleOnce(_settings.OutboundRestartBackoff, () =>
            {
                if (!association.ShouldRestartOutbound())
                    return;

                association.EnsureOutboundMaterialized(a => MaterializeOutbound(remoteAddress, a, isRestart: a.HasOutboundEverRestarted));
            });
        }

        private static ReadOnlySequence<byte> BuildPreamble(ArteryStreamId streamId)
        {
            var buffer = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(buffer, streamId);
            return new ReadOnlySequence<byte>(buffer);
        }
    }
}
