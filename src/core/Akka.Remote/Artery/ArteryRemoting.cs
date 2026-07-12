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
using Akka.Actor.Internal;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
// Akka.IO types aliased individually rather than imported wholesale: `using Akka.IO;` would make
// Tcp/TcpExt ambiguous with the Akka.Streams.Dsl Tcp/TcpExt this file uses for the transport.
using Inet = Akka.IO.Inet;
using IOwnedSequenceSegment = Akka.IO.IOwnedSequenceSegment;
using OwnedSequenceSegment = Akka.IO.OwnedSequenceSegment;

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
        private readonly AssociationRegistry _registry;

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
        /// The DEDICATED <see cref="ArrayPool{T}"/> the LARGE-MESSAGE outbound stream's
        /// <see cref="ArteryEncodeStage"/> rents its encode buffers from (task 10.2) -- kept
        /// SEPARATE from <see cref="_encodeBufferPool"/> rather than shared, since large-message
        /// buffers are (by construction) much bigger than ordinary/control ones and would
        /// otherwise pollute <see cref="_encodeBufferPool"/>'s bucket sizing. Sized directly from
        /// <see cref="ArterySettings.MaximumLargeFrameSize"/>/<see cref="ArterySettings.LargeBufferPoolSize"/>
        /// via <see cref="ArrayPool{T}.Create(int, int)"/> -- mapping Pekko's <c>EnvelopeBufferPool</c>
        /// (maximumFrameSize, bufferPoolSize) sizing onto this port's ArrayPool idiom (maxArrayLength,
        /// maxArraysPerBucket). Created unconditionally in <see cref="Start"/> (harmless bucket
        /// bookkeeping if the large stream is never used) -- see
        /// <see cref="Association.DefaultLargeQueueCapacity"/>'s remarks for the same
        /// "always allocate, only conditionally used" pattern.
        /// </summary>
        private ArrayPool<byte>? _largeEncodeBufferPool;

        /// <summary>
        /// Per-LANE dedicated <see cref="ArrayPool{T}"/> instances (outbound-lanes) -- one
        /// independent <see cref="ArrayPool{T}.Create()"/> per lane, NOT shared with each other,
        /// with <see cref="_encodeBufferPool"/> (which continues to serve the control stream, and
        /// the ordinary stream at the <c>outbound-lanes = 1</c> default), or with
        /// <see cref="_largeEncodeBufferPool"/>. Each lane's <see cref="ArteryEncodeStage"/> rents
        /// on its own lane's materialization thread and returns on the ONE shared TCP write
        /// thread -- giving every lane its own pool avoids the SAME cross-thread
        /// rent-here-return-there bucket-thrashing pattern <see cref="_encodeBufferPool"/>'s own
        /// remarks describe for <see cref="ArrayPool{T}.Shared"/> (measured ~10% throughput loss
        /// there), just one level down (lane vs. transport-wide). This is the CONSERVATIVE choice
        /// (favor isolation over pool-instance count); whether per-lane pools actually beat one
        /// pool shared across all lanes (fewer pool instances, at the cost of reintroducing
        /// cross-lane rent/return traffic) is an open question a micro-benchmark should answer
        /// before this gate signs off -- see the outbound-lanes report.
        ///
        /// <para>
        /// Empty (never indexed) at <c>outbound-lanes = 1</c> -- the ordinary stream at the default
        /// lane count never reads this array; it uses <see cref="_encodeBufferPool"/> via the
        /// UNCHANGED <see cref="MaterializeOutboundStream"/> path (gate B).
        /// </para>
        /// </summary>
        private ArrayPool<byte>[]? _laneEncodeBufferPools;

        /// <summary>
        /// Fault-injection test hook (design.md gate G3) -- see <see cref="ArteryTransportSetup.DropOutboundControlMessage"/>.
        /// Read once from <see cref="ArteryTransportSetup"/> in <see cref="Start"/>; <see langword="null"/>
        /// (production default) disables it entirely.
        /// </summary>
        private Func<object, bool>? _dropOutboundControlMessage;

        /// <summary>
        /// Test-observability hook (inbound lanes) -- see <see cref="ArteryTransportSetup.OnInboundLanesInitialized"/>.
        /// Read once from <see cref="ArteryTransportSetup"/> in <see cref="Start"/>; <see langword="null"/>
        /// (production default) disables it entirely.
        /// </summary>
        private Action<int>? _onInboundLanesInitialized;

        /// <summary>
        /// Applied to EVERY Artery socket: the accepting <c>Tcp.Bind</c> and both outbound
        /// <c>Tcp.OutgoingConnection</c> call sites in <see cref="MaterializeOutboundStream"/>.
        /// Explicitly-pinned large socket buffers prevent the kernel shrinking the receiver's
        /// window below loopback's MSS under memory pressure, which springs a sender-side
        /// silly-window-syndrome stall (rwnd_limited forever, observed as an intermittent
        /// benchmark wedge; see ss evidence: notsent+persist-timer with all app layers idle).
        /// Pinning &gt;&gt; MSS makes the trap unreachable.
        ///
        /// <para>
        /// Also carries an <see cref="Inet.SO.PipeBufferSize"/> (<see cref="ArterySettings.TcpPipeBufferSize"/>,
        /// 1 MiB by default) so <c>TcpIncomingConnection</c>/<c>TcpOutgoingConnection</c> size their
        /// input pipe's pause/resume watermarks to match these socket buffers, instead of falling back
        /// to Akka.IO's much smaller default (derived from <c>akka.io.tcp.receive-buffer-size</c>,
        /// 8 KiB) -- that default throttles the read pump well below what these pinned sockets can
        /// sustain under high-in-flight or one-way flood traffic.
        /// </para>
        /// </summary>
        internal static IImmutableList<Inet.SocketOption> BuildArterySocketOptions(ArterySettings settings) =>
            ImmutableList.Create<Inet.SocketOption>(
                new Inet.SO.ReceiveBufferSize(1024 * 1024),
                new Inet.SO.SendBufferSize(1024 * 1024),
                new Inet.SO.PipeBufferSize(settings.TcpPipeBufferSize));

        private readonly IImmutableList<Inet.SocketOption> _arterySocketOptions;

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
            _arterySocketOptions = BuildArterySocketOptions(_settings);
            // Sized from THIS transport's own settings (fix for the 78x-undersized 256 default that
            // caused spurious quarantines under a mass-termination Unwatch burst) rather than the
            // registry's own hardcoded defaults -- see ArterySettings.OutboundMessageQueueSize /
            // OutboundControlQueueSize / OutboundLargeMessageQueueSize (task 10.2).
            _registry = new AssociationRegistry(
                _settings.OutboundMessageQueueSize, _settings.OutboundControlQueueSize, _settings.OutboundLargeMessageQueueSize,
                _settings.OutboundLanes);
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
                "Artery TCP remoting is EXPERIMENTAL and under active development -- reliable system-message " +
                "delivery (seq/Ack/Nack/resend over the control stream) and outbound lanes " +
                "(akka.remote.artery.advanced.outbound-lanes) have landed; no inbound lanes/compression yet. " +
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
            var encodePoolOverride = arteryTransportSetup.Select(s => s.EncodeBufferPool).GetOrElse(null);
            _encodeBufferPool = encodePoolOverride ?? ArrayPool<byte>.Create();
            _dropOutboundControlMessage = arteryTransportSetup.Select(s => s.DropOutboundControlMessage).GetOrElse(null);
            _onInboundLanesInitialized = arteryTransportSetup.Select(s => s.OnInboundLanesInitialized).GetOrElse(null);

            // Outbound lanes: one DEDICATED ArrayPool<byte>.Create() per lane (never Shared, never
            // reused across lanes) -- see _laneEncodeBufferPools' remarks. A test-injected
            // EncodeBufferPool override (the poison-pool regression hook) is honored uniformly
            // across every lane too, same as it is for _encodeBufferPool above. Left empty
            // (harmless -- never indexed) at the outbound-lanes = 1 default; see MaterializeOutbound.
            _laneEncodeBufferPools = _settings.OutboundLanes > 1
                ? BuildLaneEncodeBufferPools(_settings.OutboundLanes, encodePoolOverride)
                : Array.Empty<ArrayPool<byte>>();

            // Large-message stream (task 10.2): a dedicated pool, sized from the large-specific
            // settings -- see _largeEncodeBufferPool's remarks for why this is kept separate from
            // _encodeBufferPool. Created unconditionally, regardless of whether
            // ArterySettings.LargeMessageChannelEnabled is true -- harmless when unused (mirrors
            // Association's always-allocated-but-possibly-unused large channel).
            _largeEncodeBufferPool = ArrayPool<byte>.Create(_settings.MaximumLargeFrameSize, _settings.LargeBufferPoolSize);

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
                    options: _arterySocketOptions, halfClose: true)
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
            // scheduled (CompleteOutbound also latches the per-association shutdown flags) -- then
            // DRAIN whatever each completed channel still holds, publishing a Dropped per element
            // (mirrors Pekko's SendQueue.postStop dead-lettering its remaining queue). Completing a
            // channel does NOT discard its buffered elements, and its consuming stream is being torn
            // down right below -- without the drain those elements would be stranded in the dead
            // channel forever, SILENTLY. At-most-once loss on shutdown is within contract; UNLOGGED
            // loss is not. Complete-then-drain is race-free: the closed writer admits nothing new
            // behind the drain.
            foreach (var association in _registry.AllAssociations)
            {
                association.CompleteOutbound();
                association.CompleteControlOutbound();
                association.CompleteLargeOutbound();

                var remoteAddress = association.RemoteAddress;
                association.DrainOutboundToDropped(envelope => System.EventStream.Publish(new Dropped(
                    envelope.Message,
                    $"Outbound Artery queue to [{remoteAddress}] drained on transport shutdown",
                    ActorRefs.NoSender,
                    System.DeadLetters)));
                association.DrainControlToDropped(envelope => System.EventStream.Publish(new Dropped(
                    envelope.Message,
                    $"Outbound Artery CONTROL queue to [{remoteAddress}] drained on transport shutdown",
                    ActorRefs.NoSender,
                    System.DeadLetters)));
                association.DrainLargeToDropped(envelope => System.EventStream.Publish(new Dropped(
                    envelope.Message,
                    $"Outbound Artery large-message queue to [{remoteAddress}] drained on transport shutdown",
                    ActorRefs.NoSender,
                    System.DeadLetters)));
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

            EnqueueOutbound(remoteAddress, message, senderPath, recipientPath, recipient);
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

            // Ordinary, Control, AND (task 10.2) Large connections all feed this SAME inbound
            // shape -- ArteryInboundProcessingStage accepts any of the three preambles; routing
            // downstream is purely by the decoded envelope's IsControl flag, not by which
            // connection carried it. The stage itself picks the frame-size limit (ordinary/control
            // share MaximumFrameSize; large uses MaximumLargeFrameSize) once it has read enough of
            // the preamble to know which stream this particular connection is.
            // SystemMessageAckerStage (design.md gate G3) sits right after InboundHandshakeStage,
            // mirroring the reference "InboundHandshake -> InboundQuarantineCheck ->
            // [control only: SystemMessageAcker]" pipeline -- it is a no-op pass-through for every
            // element that is not a SystemMessageEnvelope, so composing it unconditionally here
            // (rather than only for control-preamble connections) is correct and simpler.
            //
            // GATE A / conditional topology (inbound lanes): at the InboundLanes=1 default, this
            // constructs the stage EXACTLY as before -- the 3-arg overload, no lane machinery ever
            // materialized. Only when InboundLanes > 1 does construction take the lanes-aware
            // overload, handing it the shared IInboundContext (reused for its own IsKnownOrigin
            // gate on an Ordinary connection's lane path -- see that stage's remarks) and
            // DispatchOrdinaryMessage (the SAME resolve-and-Tell logic DispatchInbound's ordinary
            // branch uses, invoked directly by each lane's consumer loop instead of via this
            // Sink.ForEach). InboundHandshakeStage/SystemMessageAckerStage/DispatchInbound below are
            // NEVER modified for lanes -- a lane-enabled Ordinary connection's own ordinary traffic
            // never reaches them at all (dispatched directly from the lane), while any (should
            // never happen) control-classified frame on that same connection, and everything on
            // every Control/Large connection, still flows through them completely unchanged.
            var processingStage = _settings.InboundLanes > 1
                ? new ArteryInboundProcessingStage(
                    _settings.MaximumFrameSize, _settings.MaximumLargeFrameSize, System.Serialization,
                    _settings.InboundLanes, _settings.InboundLaneBufferSize, _inboundContext!, DispatchOrdinaryMessage,
                    _onInboundLanesInitialized)
                : new ArteryInboundProcessingStage(_settings.MaximumFrameSize, _settings.MaximumLargeFrameSize, System.Serialization);

            var inboundSink = Flow.Create<ReadOnlySequence<byte>>()
                .Via(_killSwitch.Flow<ReadOnlySequence<byte>>())
                .Via(processingStage)
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

            if (env.Message is ISystemMessage systemMessage)
            {
                // Reliable system-message delivery (design.md gate G3): SystemMessageAckerStage has
                // already deduplicated/ordered this -- dispatch via SendSystemMessage, mirroring
                // classic's DefaultMessageDispatcher system-message path, NOT Tell. System messages
                // never carry a sender in practice (RemoteActorRef.SendSystemMessage always sends
                // with sender: null) -- see SystemMessageEnvelope's type-level remarks.
                var recipient = Provider.ResolveActorRefWithLocalAddress(env.RecipientPath, DefaultAddress);
                recipient.SendSystemMessage(systemMessage);
                return;
            }

            DispatchOrdinaryMessage(env.Message, env.SenderPath, env.RecipientPath);
        }

        /// <summary>
        /// The ordinary-message half of <see cref="DispatchInbound"/> (resolve recipient, resolve
        /// sender or fall back to dead letters, <c>Tell</c>) -- factored out so it can ALSO be
        /// invoked directly by an inbound lane's consumer loop (<see cref="ArteryInboundProcessingStage"/>,
        /// when <see cref="ArterySettings.InboundLanes"/> &gt; 1) once it has deserialized the
        /// payload, bypassing this <see cref="Sink"/> entirely for that traffic. Identical logic,
        /// identical semantics, regardless of which caller invokes it -- lanes change WHERE this
        /// runs (a dedicated lane thread vs. this connection's own stage thread), never WHAT it does.
        /// </summary>
        private void DispatchOrdinaryMessage(object message, string? senderPath, string recipientPath)
        {
            var recipient = Provider.ResolveActorRefWithLocalAddress(recipientPath, DefaultAddress);

            // Defensive-only safety branch mirroring DispatchInbound's ISystemMessage handling
            // above -- in practice UNREACHABLE for either of this method's callers: DispatchInbound
            // itself already special-cases ISystemMessage before ever reaching here (the lanes=1
            // path), and lane-routed traffic is Ordinary-stream-only by construction (every system
            // message rides the CONTROL stream, wrapped in a SystemMessageEnvelope, via
            // EnqueueSystemMessage -- never the ordinary queue lanes fan out from; see
            // ArteryInboundProcessingStage's "Why control/large connections and lanes=1 never touch
            // this machinery" remarks). Kept anyway so this method's dispatch semantics stay EXACTLY
            // identical to DispatchInbound's regardless of which caller -- the lanes=1 Sink.ForEach
            // above, or an inbound lane's consumer loop -- ends up invoking it.
            if (message is ISystemMessage systemMessage)
            {
                recipient.SendSystemMessage(systemMessage);
                return;
            }

            var sender = senderPath is { } sp
                ? Provider.ResolveActorRefWithLocalAddress(sp, DefaultAddress)
                : (IActorRef)System.DeadLetters;

            // Mirrors classic's DefaultMessageDispatcher semantics without depending on classic types:
            // an unresolvable recipient resolves to an EmptyLocalActorRef, whose Tell publishes a
            // DeadLetter automatically.
            recipient.Tell(message, sender);
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

        /// <summary>
        /// <see cref="RemoteActorRef.CachedSendQueueIndex"/>'s sentinel: this ref's send route has
        /// not been resolved yet (see <see cref="ResolveSendRoute"/>).
        /// </summary>
        private const int UnresolvedSendRoute = -1;

        /// <summary>
        /// <see cref="RemoteActorRef.CachedSendQueueIndex"/> encoding for "route to the
        /// LARGE-MESSAGE stream" -- distinguished from every ordinary-lane index (always &gt;= 0)
        /// by being negative but distinct from <see cref="UnresolvedSendRoute"/>.
        /// </summary>
        private const int LargeMessageRoute = -2;

        /// <summary>
        /// Resolves (and caches, per <see cref="RemoteActorRef.CachedSendQueueIndex"/>) which
        /// outbound queue <paramref name="recipient"/>'s ordinary sends belong on -- Pekko's
        /// <c>Association.selectQueue</c>, computed ONCE per ref rather than on every single send
        /// (closing the review follow-up from the large-message-stream PR: this also eliminates
        /// that per-send <see cref="Akka.Util.WildcardIndex{T}.Find"/> call on the steady-state path
        /// once the ref's route is cached). Returns either <see cref="LargeMessageRoute"/> or an ordinary
        /// lane index in <c>[0, association.OutboundLanes)</c> (<see cref="Association.SelectLane"/>).
        /// </summary>
        private int ResolveSendRoute(RemoteActorRef recipient, Association association)
        {
            var cached = recipient.CachedSendQueueIndex;
            if (cached != UnresolvedSendRoute)
                return cached;

            var computed = _settings.LargeMessageChannelEnabled &&
                            _settings.LargeMessageDestinations.Find(recipient.Path.Elements) is not null
                ? LargeMessageRoute
                : Association.SelectLane(recipient.Path.Uid, association.OutboundLanes);

            recipient.CachedSendQueueIndex = computed;
            return computed;
        }

        private void EnqueueOutbound(Address remoteAddress, object message, string? senderPath, string? recipientPath, RemoteActorRef recipient)
        {
            var association = _registry.AssociationFor(remoteAddress);
            var route = ResolveSendRoute(recipient, association);

            // Large-message stream routing (task 10.2), Pekko-faithful precedence: control > large
            // > ordinary. Control is already excluded by construction -- every system/control
            // message is diverted to EnqueueSystemMessage/EnqueueControl BEFORE this method is ever
            // called (see Send()), so it can never reach here regardless of whether its path
            // happens to match a large-message-destinations pattern. ActorSelectionMessage is
            // explicitly excluded too, matching Pekko's documented large-message-destinations
            // semantics ("Messages sent to ActorSelections will not be passed through the large
            // message stream") -- and in this port `recipient` for a selection send is the
            // selection's ANCHOR, not the final target, so it would essentially never match a
            // destination pattern anyway. This exclusion is applied PER-MESSAGE (never cached) so a
            // shared anchor ref's cached "large" route -- from some OTHER, non-selection send to
            // the same anchor -- never leaks into a selection send's routing.
            if (route == LargeMessageRoute && message is not ActorSelectionMessage)
            {
                if (!association.IsLargeOutboundMaterialized)
                    association.EnsureLargeOutboundMaterialized(a => MaterializeLargeOutbound(remoteAddress, a, isRestart: a.HasLargeEverRestarted));

                if (!association.TryEnqueueLarge(new OutboundEnvelope(message, senderPath, recipientPath)))
                {
                    // Same direct-to-event-stream Dropped semantics as the ordinary queue's PR
                    // #8346 fix (see below) -- soft drop, never a quarantine.
                    System.EventStream.Publish(new Dropped(
                        message,
                        $"Outbound Artery large-message queue to [{remoteAddress}] is full (capacity {association.LargeQueueCapacity})",
                        ActorRefs.NoSender,
                        System.DeadLetters));
                }

                return;
            }

            // Either an ordinary lane index already, or an ActorSelectionMessage whose recipient's
            // cached route says "large" -- forced onto lane 0 for THIS send only (see the remarks
            // above; the ref's cache is left untouched for its own future non-selection sends).
            var laneIndex = route == LargeMessageRoute ? 0 : route;

            if (!association.IsOutboundMaterialized)
                // isRestart is derived from the gate's OWN history (design.md group 9), not a
                // literal here -- this on-demand path can race ScheduleOutboundRestart's scheduled
                // callback for who actually wins EnsureOutboundMaterialized after a reset, and
                // BOTH must agree on whether a fresh handshake is required (see
                // Association.HasOutboundEverRestarted's remarks).
                association.EnsureOutboundMaterialized(a => MaterializeOutbound(remoteAddress, a, isRestart: a.HasOutboundEverRestarted));

            if (!association.TryEnqueueOutbound(new OutboundEnvelope(message, senderPath, recipientPath), laneIndex))
            {
                // Publish DIRECTLY to the event stream -- mirrors Pekko's Association.dropped().
                // NOT routed through System.DeadLetters.Tell (that would double-wrap: Tell to the
                // DeadLetters actor itself publishes a DeadLetter/Dropped wrapping whatever it's
                // handed). The existing DeadLetterListener already provides log-N-then-periodic-
                // summary behavior for Dropped via the akka.log-dead-letters settings, so no
                // separate log-once latch is needed here (see DeadLetterSuspensionSpec). The lane
                // index is named explicitly so an operator can tell WHICH lane backed up --
                // outbound-lanes drop-observability requirement.
                System.EventStream.Publish(new Dropped(
                    message,
                    $"Outbound Artery queue to [{remoteAddress}] lane [{laneIndex}] is full (capacity {association.OutboundQueueCapacity})",
                    ActorRefs.NoSender,
                    System.DeadLetters));
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

            // Early shutdown guard: graceful ActorSystem termination tears down every
            // association's control channel (ArteryRemoting.Shutdown() -> CompleteControlOutbound)
            // BEFORE RemoteWatcher finishes draining its own queued Unwatch work, so this method is
            // routinely called after the channel is already closed. Route straight to dead letters at
            // DEBUG instead of paying for a (pointless) materialize/enqueue attempt. This check alone
            // cannot close the race -- the channel can still complete between here and
            // TryEnqueueControl below -- so HandleControlOverflow applies the SAME check again,
            // race-free, after a failed enqueue.
            if (_isShutdown || association.IsControlShutDown)
            {
                _log.Debug(
                    "Outbound control channel to [{0}] already closed during shutdown; dropping {1} to dead letters.",
                    remoteAddress, message.GetType());
                System.DeadLetters.Tell(message, ActorRefs.NoSender);
                return;
            }

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

            // Early shutdown guard -- see EnqueueSystemMessage's matching guard for the full
            // rationale (same race, same quiet drop-to-dead-letters path).
            if (_isShutdown || association.IsControlShutDown)
            {
                _log.Debug(
                    "Outbound control channel to [{0}] already closed during shutdown; dropping {1} to dead letters.",
                    remoteAddress, message.GetType());
                System.DeadLetters.Tell(message, ActorRefs.NoSender);
                return;
            }

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
        ///
        /// <para>
        /// <b>Closed-channel vs. actually-full (mirrors Pekko's <c>Association.sendControl</c>
        /// <c>isShutdown</c> gating).</b> <see cref="Association.TryEnqueueControl"/>'s underlying
        /// <see cref="System.Threading.Channels.ChannelWriter{T}.TryWrite"/> returns
        /// <see langword="false"/> BOTH when the bounded queue is genuinely at capacity AND when the
        /// writer has already been completed by <see cref="Association.CompleteControlOutbound"/> --
        /// which graceful <see cref="Shutdown"/> calls for every association BEFORE RemoteWatcher has
        /// necessarily finished draining its own queued Unwatch work. Checking
        /// <see cref="Association.IsControlShutDown"/> HERE, after the failed enqueue, is race-free
        /// (channel completion latches permanently) even though the guards at the top of
        /// <see cref="EnqueueControl"/>/<see cref="EnqueueSystemMessage"/> cannot close this TOCTOU
        /// gap by themselves. Treating a closed-channel drop the same as a genuinely full queue would
        /// otherwise spuriously log at ERROR and quarantine an otherwise perfectly healthy peer on
        /// every ordinary graceful shutdown.
        /// </para>
        /// </summary>
        private void HandleControlOverflow(Address remoteAddress, Association association, object message)
        {
            if (association.IsControlShutDown)
            {
                _log.Debug(
                    "Outbound control channel to [{0}] already closed during shutdown; dropping {1} to dead letters.",
                    remoteAddress, message.GetType());
                System.DeadLetters.Tell(message, ActorRefs.NoSender);
                return;
            }

            var peer = association.CurrentState.UniqueRemoteAddress;
            var shouldQuarantine = peer is { } p && !association.IsQuarantined(p.Uid);

            _log.Error(
                "Outbound Artery CONTROL queue to [{0}] is full (capacity {1}); dropping control message of " +
                "type [{2}] to dead letters{3}.",
                remoteAddress, association.ControlQueueCapacity, message.GetType(),
                shouldQuarantine ? " and quarantining the association" : "");
            System.DeadLetters.Tell(message, ActorRefs.NoSender);

            if (shouldQuarantine)
                Quarantine(remoteAddress, peer!.Value.Uid);
        }

        /// <summary>
        /// Materializes this association's ORDINARY outbound stream. GATE B: at
        /// <c>association.OutboundLanes &lt;= 1</c> (the shipping default) this is EXACTLY today's
        /// call -- <see cref="MaterializeOutboundStream"/>, unchanged, no merge/fan-in machinery
        /// ever materialized. Only at <c>OutboundLanes &gt; 1</c> does this branch into the
        /// N-lanes-&gt;-one-socket assembly (<see cref="MaterializeOrdinaryOutboundWithLanes"/>).
        /// Branches on <c>association.OutboundLanes</c> (the actual channel array's own lane
        /// count), not a separately-read settings snapshot, so this can never drift from the shape
        /// <see cref="Association"/> was actually constructed with.
        /// </summary>
        private void MaterializeOutbound(Address remoteAddress, Association association, bool isRestart = false)
        {
            if (association.OutboundLanes <= 1)
            {
                MaterializeOutboundStream(remoteAddress, association, ArteryStreamId.Ordinary, isRestart);
                return;
            }

            MaterializeOrdinaryOutboundWithLanes(remoteAddress, association, isRestart);
        }

        private void MaterializeControlOutbound(Address remoteAddress, Association association, bool isRestart = false) =>
            MaterializeOutboundStream(remoteAddress, association, ArteryStreamId.Control, isRestart);

        private void MaterializeLargeOutbound(Address remoteAddress, Association association, bool isRestart = false) =>
            MaterializeOutboundStream(remoteAddress, association, ArteryStreamId.Large, isRestart);

        /// <summary>
        /// Materializes this association's ORDINARY outbound stream when <c>outbound-lanes &gt; 1</c>:
        /// N independent lane chains --
        /// <c>ChannelSource.FromReader(laneReader) -&gt; OutboundHandshakeStage -&gt; ArteryEncodeStage</c>,
        /// each with its OWN <see cref="OutboundHandshakeStage"/> instance (Pekko does this too --
        /// <c>ArteryTransport.scala:791-812</c>) and its OWN dedicated encode buffer pool
        /// (<see cref="_laneEncodeBufferPools"/>) -- merged via <see cref="MergeHub"/> into the
        /// SAME preamble/kill-switch/<c>Tcp.OutgoingConnection</c> tail
        /// <see cref="MaterializeOutboundStream"/> uses for the single-lane case: ONE socket per
        /// association, regardless of lane count. Frame atomicity is inherent -- <see cref="MergeHub"/>
        /// merges whole elements, and each <see cref="ArteryEncodeStage"/> element is one complete
        /// framed message, so interleaving happens only at frame boundaries.
        ///
        /// <para>
        /// <b>One restartable unit (Pekko's <c>streamKillSwitch</c> semantics,
        /// <c>Association.scala</c>'s <c>runOutboundOrdinaryMessagesStream</c>).</b> A single
        /// <see cref="SharedKillSwitch"/> (<paramref name="association"/>'s published
        /// <see cref="Association.SetOutboundKillSwitch"/> value for this materialization) is woven
        /// into EVERY lane chain AND the merge/socket tail. Whichever piece settles FIRST -- a lane
        /// chain failing, the merge/socket tail failing, or either completing gracefully -- trips
        /// this switch, which tears every OTHER piece down too; only once every piece's own
        /// termination task has then settled does the single overall termination watch fire
        /// <see cref="ScheduleOutboundRestart"/> for <see cref="ArteryStreamId.Ordinary"/> -- i.e.
        /// the whole N-lanes-plus-socket assembly restarts together, as Pekko does, never a lone
        /// lane silently going quiet while its siblings and the socket linger.
        /// </para>
        ///
        /// <para>
        /// Lane channels themselves survive this restart exactly the way the single ordinary
        /// channel does today -- they are <paramref name="association"/>-owned and outlive any one
        /// materialization (design.md group 9); only the stream graphs reading from them are torn
        /// down and rebuilt.
        /// </para>
        /// </summary>
        private void MaterializeOrdinaryOutboundWithLanes(Address remoteAddress, Association association, bool isRestart)
        {
            // Same shutdown/materializer-liveness guard as MaterializeOutboundStream -- see its
            // remarks for the full rationale (late system message racing teardown; materializer
            // reclaimed independently of _isShutdown).
            if (_isShutdown || _materializer is null || _materializer.IsShutdown)
                return;

            var lanes = association.OutboundLanes;

            var outboundContext = new AssociationRegistryOutboundContext(
                _registry,
                _localUniqueAddress,
                remoteAddress,
                sendControl: message => EnqueueControl(remoteAddress, message),
                subscribeControl: SubscribeControl,
                unsubscribeControl: UnsubscribeControl,
                quarantine: (address, uid) => Quarantine(address, uid));

            var host = remoteAddress.Host
                ?? throw new RemoteTransportException($"Cannot open an Artery {ArteryStreamId.Ordinary} outbound connection to [{remoteAddress}]: missing host.");
            var port = remoteAddress.Port
                ?? throw new RemoteTransportException($"Cannot open an Artery {ArteryStreamId.Ordinary} outbound connection to [{remoteAddress}]: missing port.");
            var remoteEndpoint = IPAddress.TryParse(host, out var parsedHost)
                ? (EndPoint)new IPEndPoint(parsedHost, port)
                : new DnsEndPoint(host, port);

            // ONE switch, shared across every lane chain AND the merge/socket tail below -- see
            // this method's "one restartable unit" remarks. Named per-association for diagnosability.
            var laneKillSwitch = KillSwitches.Shared($"arteryOutboundLanesKillSwitch-{remoteAddress}");

            // null! satisfies definite-assignment (same pattern/rationale as MaterializeOutboundStream's
            // own terminationWatch): the catch clauses below always return, so these are only read
            // past the try block when it assigned them.
            Sink<ReadOnlySequence<byte>, NotUsed> mergeSink = null!;
            Task mergeTailTermination = null!;
            var laneTerminations = new Task[lanes];

            try
            {
                var mergeHubSource = MergeHub.Source<ReadOnlySequence<byte>>();

                // INNER CONNECTION RESTART (Pekko's connectionFlowWithRestart,
                // ArteryTcpTransport.scala): the TCP connection ALONE is wrapped in
                // RestartFlow.OnFailuresWithBackoff so a transient connect/write fault (connection
                // refused at a fresh materialization, a port-rebind race, a reset mid-burst) retries
                // the SOCKET -- with backoff, up to 3 times (Pekko's message-stream maxRestarts) --
                // WITHOUT tearing down the lane chains, the MergeHub, or the handshake stages. This
                // is the message-loss fix: before this wrapper, a single connect fault tripped the
                // shared kill switch and discarded every element already dequeued from the
                // (restart-safe) lane channels into the stream stages. Only after the inner retries
                // are exhausted does the failure propagate and settle the assembly, handing control
                // to the OUTER tier (ScheduleOutboundRestart) exactly as before.
                //
                // The [streamId] preamble is prepended INSIDE the restart factory -- load-bearing:
                // every reconnect materializes a fresh flow from this factory, so every new socket
                // re-sends the connection header first (Pekko does the same, prepending the header
                // inside its lazyFlow: ArteryTcpTransport.scala's connectionFlowWithRestart).
                //
                // OnFailures (NOT plain WithBackoff): our outbound connections are one-way with
                // halfClose enabled -- the read side EOFs immediately and harmlessly -- and a
                // GRACEFUL completion (this system's own Shutdown()/CompleteOutbound draining
                // through) must complete the tail, not re-dial the peer.
                var restartSettings = RestartSettings.Create(
                        minBackoff: _settings.OutboundRestartBackoff,
                        maxBackoff: TimeSpan.FromTicks(_settings.OutboundRestartBackoff.Ticks * 5),
                        randomFactor: 0.1)
                    .WithMaxRestarts(OrdinaryConnectionMaxInnerRestarts, _settings.OutboundRestartBackoff);

                var connectionWithRestart = RestartFlow.OnFailuresWithBackoff(
                    () => Flow.Create<ReadOnlySequence<byte>>()
                        .Prepend(Source.Single(BuildPreamble(ArteryStreamId.Ordinary)))
                        .Via(_tcp!.OutgoingConnection(remoteEndpoint, options: _arterySocketOptions)),
                    restartSettings);

                // MergeHub -> transport-wide kill switch (Shutdown() tears every association's
                // streams down) -> this assembly's OWN shared kill switch -> watch termination
                // (write-side completion signal, exactly like MaterializeOutboundStream's
                // "TERMINATION SIGNAL" remarks) -> frame batching (below) -> restart-wrapped
                // OutgoingConnection (above).
                //
                // FRAME BATCHING: crossing into the restart-wrapped connection flow costs
                // per-ELEMENT, so under load the merge tail collapses whatever already-encoded
                // frames are immediately available into ONE multi-segment sequence of up to
                // LaneWriteBatchMaxBytes before crossing (AppendFrameToBatch chains the segments
                // zero-copy; see its remarks for the ownership-transfer invariant). Wire bytes are
                // IDENTICAL either way -- the inbound side parses frames off the byte stream and
                // never sees element boundaries. When the connection keeps up, BatchWeighted is a
                // 1:1 pass-through (it only rolls up while downstream is busy); when it doesn't,
                // frames coalesce. Placed AFTER WatchTermination so the termination-signal wiring
                // is unchanged. Teardown: a batch stranded here by an abrupt abort is reclaimed by
                // the GC rather than returned to its buffer pools -- the same accepted trade as
                // elements stranded in the MergeHub's own queue (see ArteryEncodeStage's "why this
                // stage itself needs no backstop" remarks).
                ((mergeSink, mergeTailTermination), _) = mergeHubSource
                    .Via(_killSwitch.Flow<ReadOnlySequence<byte>>())
                    .Via(laneKillSwitch.Flow<ReadOnlySequence<byte>>())
                    .WatchTermination(Keep.Both)
                    .BatchWeighted(
                        max: LaneWriteBatchMaxBytes,
                        costFunction: static frame => frame.Length,
                        seed: static frame => frame,
                        aggregate: AppendFrameToBatch)
                    .Via(connectionWithRestart)
                    .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both)
                    .Run(_materializer!);

                association.SetOutboundKillSwitch(laneKillSwitch);

                for (var i = 0; i < lanes; i++)
                {
                    var handshakeStage = new OutboundHandshakeStage(
                        outboundContext, _settings.HandshakeRetryInterval, _settings.HandshakeTimeout,
                        _settings.InjectHandshakeInterval, isControlStream: false, forceReqOnStart: isRestart);

                    var encodeStage = new ArteryEncodeStage(
                        System.Serialization, _localUniqueAddress.Uid, _laneEncodeBufferPools![i]);

                    // RecoverWithRetries sits AFTER WatchTermination -- the ordering is load-bearing:
                    // the termination task (upstream of the recovery) must still observe the lane's
                    // FAILURE, so the trip-all continuation below Aborts the shared kill switch and
                    // the assembly restarts; while MergeHub (downstream of the recovery) sees only a
                    // graceful completion and never logs "Upstream producer failed" for a fault the
                    // assembly is already handling. Pekko does exactly this ("recover to avoid error
                    // logging by MergeHub", Association.scala's lane construction).
                    var (laneTermination, _) = ChannelSource.FromReader(association.LaneReader(i))
                        .Via(laneKillSwitch.Flow<IOutboundEnvelope>())
                        .Via(Flow.FromGraph(handshakeStage))
                        .Via(Flow.FromGraph(encodeStage))
                        .WatchTermination(Keep.Right)
                        .Via(Flow.Create<ReadOnlySequence<byte>>()
                            .RecoverWithRetries(_ => Source.Empty<ReadOnlySequence<byte>>(), attempts: -1))
                        .ToMaterialized(mergeSink, Keep.Both)
                        .Run(_materializer!);

                    laneTerminations[i] = laneTermination;
                }
            }
            catch (Akka.Pattern.IllegalStateException) when (_isShutdown || _materializer is null || _materializer.IsShutdown)
            {
                // Same shutdown race MaterializeOutboundStream's matching catch documents -- lost
                // the race with teardown (materializer reclaimed between the guard above and Run()).
                // Trip the assembly's own kill switch FIRST so any piece a PARTIAL materialization
                // did manage to run (the merge tail and/or earlier lanes) is torn down
                // deterministically -- the no-double-consumer property must hold structurally, never
                // rest on "materialization exception implies the system is terminating".
                // Then release the materialize-once gate: this catch-and-return path otherwise
                // leaves the gate latched "started" with NO stream materialized and NO restart
                // scheduled -- a permanent wedge if the association outlives this race (producers
                // keep enqueueing, nothing ever drains). Harmless when the system really is dying;
                // curative when it is not.
                laneKillSwitch.Shutdown();
                association.ResetOutboundGate();
                _log.Debug("Artery {0} outbound lanes stream to [{1}] not materialized: materializer is shutting down.", ArteryStreamId.Ordinary, remoteAddress);
                return;
            }
            catch (InvalidOperationException) when (IsActorSystemTerminating())
            {
                // Same second shutdown race MaterializeOutboundStream's matching catch documents --
                // /user guardian tearing down ahead of ArteryRemoting.Shutdown() itself. Narrowed by
                // actual termination state (see IsActorSystemTerminating): a spurious
                // InvalidOperationException from a LIVE system propagates instead -- up through
                // MaterializeOnceGate.EnsureStarted, which resets the gate and rethrows, so the
                // failure is observable AND the next send can retry. Kill switch tripped before the
                // gate release -- same structural no-double-consumer teardown as the catch above --
                // and the gate released for the same anti-wedge rationale.
                laneKillSwitch.Shutdown();
                association.ResetOutboundGate();
                _log.Debug("Artery {0} outbound lanes stream to [{1}] not materialized: actor system is terminating.", ArteryStreamId.Ordinary, remoteAddress);
                return;
            }

            // Whichever piece (any lane, or the merge/socket tail) settles FIRST trips the shared
            // switch, tearing every OTHER piece down too -- see this method's "one restartable unit"
            // remarks. Idempotent (SharedKillSwitch.Shutdown()/Abort() are both first-caller-wins).
            var allPieces = new Task[lanes + 1];
            Array.Copy(laneTerminations, allPieces, lanes);
            allPieces[lanes] = mergeTailTermination;

            foreach (var piece in allPieces)
            {
                piece.ContinueWith(t =>
                {
                    if (t.IsFaulted)
                        laneKillSwitch.Abort(t.Exception!.GetBaseException());
                    else
                        laneKillSwitch.Shutdown();
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            // The SINGLE overall termination signal for this assembly: fires ScheduleOutboundRestart
            // exactly once, only after EVERY lane and the merge/socket tail have all themselves
            // settled (a direct consequence of the trip-on-first-settle wiring above cascading the
            // shutdown/abort through every OTHER piece's kill-switch-woven flow).
            Task.WhenAll(allPieces).ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _log.Warning(
                        t.Exception?.GetBaseException(),
                        "Artery {0} outbound lanes stream to [{1}] failed; this association's ordinary outbound " +
                        "assembly ({2} lanes) has ended -- reconnect will be attempted per outbound-restart-backoff " +
                        "unless shut down or quarantined.", ArteryStreamId.Ordinary, remoteAddress, lanes);
                else
                    _log.Debug(
                        "Artery {0} outbound lanes stream to [{1}] completed ({2} lanes); reconnect will be " +
                        "attempted per outbound-restart-backoff unless shut down or quarantined.",
                        ArteryStreamId.Ordinary, remoteAddress, lanes);

                ScheduleOutboundRestart(remoteAddress, association, ArteryStreamId.Ordinary);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

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
            var isLargeStream = streamId == ArteryStreamId.Large;
            var reader = streamId switch
            {
                ArteryStreamId.Control => association.ControlReader,
                ArteryStreamId.Large => association.LargeReader,
                _ => association.OutboundReader
            };

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
            // overload that does -- see BuildArterySocketOptions/_arterySocketOptions.
            var remoteEndpoint = IPAddress.TryParse(host, out var parsedHost)
                ? (EndPoint)new IPEndPoint(parsedHost, port)
                : new DnsEndPoint(host, port);

            // Large-message stream (task 10.2) rents from its OWN dedicated, large-sized pool --
            // see _largeEncodeBufferPool's remarks.
            var encodeStage = new ArteryEncodeStage(
                System.Serialization, _localUniqueAddress.Uid, isLargeStream ? _largeEncodeBufferPool : _encodeBufferPool);

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
                        .ViaMaterialized(_tcp!.OutgoingConnection(remoteEndpoint, options: _arterySocketOptions), Keep.Both)
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
                    // Ordinary AND large-message (task 10.2) streams share this branch -- neither
                    // has its own heartbeat, so both rely on the CONTROL stream's death detection
                    // to trip their kill switch (see the ordinary-vs-large kill switch dispatch
                    // just below, and the termination continuation's trip-both call).
                    UniqueKillSwitch killSwitch;
                    ((killSwitch, terminationWatch), _) = preambleAndFrames
                        .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Right)
                        .WatchTermination(Keep.Both)
                        .Via(_tcp!.OutgoingConnection(remoteEndpoint, options: _arterySocketOptions))
                        .ToMaterialized(Sink.Ignore<ReadOnlySequence<byte>>(), Keep.Both)
                        .Run(_materializer!);

                    if (isLargeStream)
                        association.SetLargeOutboundKillSwitch(killSwitch);
                    else
                        association.SetOutboundKillSwitch(killSwitch);
                }
            }
            catch (Akka.Pattern.IllegalStateException) when (_isShutdown || _materializer is null || _materializer.IsShutdown)
            {
                // Lost the race with teardown: the ActorSystem reclaimed the materializer (its
                // StreamSupervisor stopped) between the guard at the top of this method and Run() here,
                // so Materialize() threw. The transport is going away -- drop quietly. Gated on an
                // actually-shut-down materializer so a genuine IllegalStateException from a live
                // materializer still propagates. The stream's materialize-once gate is released first
                // -- this catch-and-return path otherwise leaves it latched "started" with NO stream
                // materialized and NO restart scheduled (a permanent wedge if the association
                // outlives the race: producers keep enqueueing, nothing ever drains).
                ResetGateFor(association, streamId);
                _log.Debug("Artery {0} outbound stream to [{1}] not materialized: materializer is shutting down.", streamId, remoteAddress);
                return;
            }
            catch (InvalidOperationException) when (IsActorSystemTerminating())
            {
                // A SECOND, DIFFERENT shutdown race (this transport's own flags don't cover it --
                // read on). ActorMaterializer.Create(system)'s StreamSupervisor is a TOP-LEVEL
                // actor created via system.ActorOf(...), i.e. it lives under /user -- so it starts
                // terminating (ActorCell then throws InvalidOperationException for any new
                // graph-interpreter child Run() tries to create) as soon as /user guardian tears
                // down, which happens WELL BEFORE ArteryRemoting.Shutdown() runs (that is gated
                // behind /system's RemotingTerminator phase, later in CoordinatedShutdown). During
                // that window BOTH _isShutdown and _materializer.IsShutdown are still false -- so
                // the filter consults actual termination state (the supervisor's own cell,
                // CoordinatedShutdown, WhenTerminated) instead of swallowing the whole exception
                // TYPE (see IsActorSystemTerminating): a spurious InvalidOperationException from a LIVE system
                // propagates up through MaterializeOnceGate.EnsureStarted (which resets the gate and
                // rethrows) instead of being silently eaten with the gate latched. Dropping the
                // message/ack this materialization would have carried is safe: the peer's
                // SystemMessageDeliveryStage resend/give-up protocol handles a missing ack. Gate
                // released here too, same anti-wedge rationale as the catch above.
                ResetGateFor(association, streamId);
                _log.Debug("Artery {0} outbound stream to [{1}] not materialized: actor system is terminating.", streamId, remoteAddress);
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
                // detector), drive the ORDINARY stream (and, task 10.2, the LARGE-MESSAGE stream --
                // it has no heartbeat of its own either) down ONCE so they reconnect alongside
                // control rather than lingering on a dead socket after a single write failed to
                // surface the death. Firing only on the edge avoids churning a healthy consumer
                // mid-handshake against the revived peer. Idempotent + null-safe when a stream is
                // not currently materialized. See Association._outboundKillSwitch/_largeOutboundKillSwitch.
                if (isControlStream && t.IsFaulted && association.TryConsumeControlHealthy())
                {
                    association.TripOutboundKillSwitch();
                    association.TripLargeOutboundKillSwitch();
                }

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

            if (streamId == ArteryStreamId.Large)
            {
                if (!association.ShouldRestartLargeOutbound())
                    return;

                association.ResetLargeGate();
                System.Scheduler.Advanced.ScheduleOnce(_settings.OutboundRestartBackoff, () =>
                {
                    if (!association.ShouldRestartLargeOutbound())
                        return;

                    association.EnsureLargeOutboundMaterialized(a => MaterializeLargeOutbound(remoteAddress, a, isRestart: a.HasLargeEverRestarted));
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

        /// <summary>
        /// Maximum number of INNER connection restarts (the <see cref="RestartFlow"/> wrapped
        /// around the ordinary-lanes assembly's <c>Tcp.OutgoingConnection</c>) before the failure
        /// is allowed to propagate and settle the whole assembly, handing control to the OUTER
        /// restart tier (<see cref="ScheduleOutboundRestart"/>). Pekko's message-stream constant
        /// (<c>ArteryTcpTransport.scala</c>: <c>val maxRestarts = if (streamId == ControlStreamId)
        /// Int.MaxValue else 3</c>) -- deliberately a constant, not a new HOCON key.
        /// </summary>
        private const int OrdinaryConnectionMaxInnerRestarts = 3;

        /// <summary>
        /// State-based filter for the two <see cref="InvalidOperationException"/> materialization
        /// catches: is this system (or this transport's slice of it) shutting down, so the
        /// exception can be attributed to the shutdown race rather than a genuine fault on a live
        /// system?
        ///
        /// <para>
        /// Why <c>Run()</c> throws <see cref="InvalidOperationException"/> at all: it creates the
        /// graph-interpreter actor as a CHILD of the materializer's StreamSupervisor
        /// (<c>ExtendedActorMaterializer.ActorOf</c> -&gt; <c>ActorCell.AttachChild</c>). The
        /// StreamSupervisor is a /user top-level actor, so it starts terminating as soon as the
        /// /user guardian tears down -- WELL BEFORE <see cref="Shutdown"/> flips
        /// <c>_isShutdown</c> (gated behind /system's RemotingTerminator phase, later in
        /// <see cref="CoordinatedShutdown"/>). A child-creation attempt in that window throws
        /// from one of three sites, depending on where the attaching thread lands relative to
        /// the supervisor's own termination: <c>ActorCell.Children.cs:462</c> (MakeChild's
        /// up-front terminating check), <c>TerminatingChildrenContainer.cs:68</c> or
        /// <c>TerminatedChildrenContainer.cs:50</c> (the non-atomic ReserveChild step losing the
        /// same race a moment later).
        /// </para>
        ///
        /// <para>
        /// The signals checked, cheapest first -- their union covers the whole window from
        /// "termination begun" to "termination complete":
        /// <list type="bullet">
        /// <item><description><c>_isShutdown</c> / <c>_materializer.IsShutdown</c>: this
        /// transport's own teardown (the late end of the window; the same flags the sibling
        /// <c>IllegalStateException</c> catches consult).</description></item>
        /// <item><description><see cref="IsStreamSupervisorTerminating"/>: the DIRECT cause --
        /// the cell <c>Run()</c> attaches children to is terminating/terminated. All three throw
        /// sites above fire precisely when that cell's <c>ChildrenContainer</c> has entered a
        /// terminating state, and that state never reverts (a Termination reason is terminal),
        /// so this check -- evaluated at throw time, since <c>when</c> filters run before
        /// unwinding -- is equivalent to the union of the three throw conditions.</description></item>
        /// <item><description><see cref="CoordinatedShutdown.ShutdownReason"/> non-null:
        /// "termination started" -- set atomically the instant a CoordinatedShutdown run begins
        /// (<see cref="ActorSystem.Terminate"/> routes through it by default).
        /// <c>TryGetExtension</c> avoids instantiating the extension from inside an exception
        /// filter (a throwing filter silently evaluates to false).</description></item>
        /// <item><description><c>ActorSystemImpl.Aborting</c>: <c>ActorSystem.Abort()</c>
        /// skips CoordinatedShutdown entirely; this is its flag.</description></item>
        /// <item><description><c>WhenTerminated.IsCompleted</c>: the belt-and-suspenders late
        /// signal (termination already finished).</description></item>
        /// </list>
        /// There is no <c>ActorSystem.WhenTerminating</c> in Akka.NET --
        /// <c>ShutdownReason</c>/<c>Aborting</c> are the earliest "termination has begun" state
        /// available. Used to NARROW the materialization catches so a spurious
        /// <see cref="InvalidOperationException"/> from a live system propagates instead of being
        /// silently swallowed.
        /// </para>
        /// Weight cap for the <c>BatchWeighted</c> stage on the ordinary-lanes merge tail: the most
        /// bytes of already-encoded frames one batched element may carry. Mirrors the TCP
        /// write-coalescing cap it feeds into
        /// (<c>Akka.Streams.Implementation.IO.TcpStages.TcpStreamLogic.WriteBufferCap</c>, 16 KiB):
        /// that is the largest accumulation the connection stage itself builds before it stops
        /// pulling, so batching further upstream past it buys nothing.
        /// </summary>
        private const long LaneWriteBatchMaxBytes = 16 * 1024;

        /// <summary>
        /// <c>BatchWeighted</c> aggregate for the ordinary-lanes merge tail (see
        /// <see cref="MaterializeOrdinaryOutboundWithLanes"/>): chains every segment of
        /// <paramref name="frame"/> onto the tail of <paramref name="batch"/>'s segment chain --
        /// zero-copy, no memcpy anywhere -- and returns a single <see cref="ReadOnlySequence{T}"/>
        /// spanning both. The ownership transfer follows
        /// <c>Akka.Streams.Implementation.IO.TcpStages.TcpStreamLogic.AppendToWriteBuffer</c>
        /// exactly: each source segment's owner is detached exactly once
        /// (<see cref="IOwnedSequenceSegment.DetachOwner"/>) and re-carried by the new link appended
        /// to the batch, so every pooled buffer always has exactly one segment responsible for
        /// eventually disposing it. Mutating the batch's tail here is safe: the Batch stage's logic
        /// is single-threaded, and a pushed batch is never aggregated onto again (the next
        /// accumulation starts from a fresh seed).
        ///
        /// <para>
        /// <b>Invariant (throws if violated).</b> Both inputs are always
        /// <see cref="OwnedSequenceSegment"/>-backed. Every element on this path is pushed by
        /// <see cref="ArteryEncodeStage"/> as <c>OwnedSequenceSegment.Create(owner)</c> -- a
        /// single-segment, owner-carrying, segment-backed sequence -- and reaches the batch stage
        /// unchanged: the kill-switch flows, <c>WatchTermination</c>, <c>RecoverWithRetries</c> and
        /// the <c>MergeHub</c> are all pass-through, and the connection preamble is prepended
        /// INSIDE the downstream restart factory, so it never passes through this stage. The batch
        /// side holds by induction: <c>BatchWeighted</c>'s seed is the identity, so an accumulator
        /// is either a raw encode-stage frame or a previous return value of this method. A
        /// violation means a foreign producer got wired into the merge tail -- failing the stage
        /// loudly (settling the assembly, whose restart tier takes over) beats silently copying, or
        /// worse dropping, bytes of unknown provenance.
        /// </para>
        /// </summary>
        /// <param name="batch">The accumulated batch. Its backing chain is extended in place.</param>
        /// <param name="frame">The incoming encoded frame whose segments (and their owners) move onto the batch.</param>
        /// <returns>One sequence over the batch's head through the newly appended tail.</returns>
        internal static ReadOnlySequence<byte> AppendFrameToBatch(ReadOnlySequence<byte> batch, ReadOnlySequence<byte> frame)
        {
            if (batch.Start.GetObject() is not OwnedSequenceSegment head ||
                batch.End.GetObject() is not OwnedSequenceSegment tail)
                throw new InvalidOperationException(
                    "Artery lane frame batching requires OwnedSequenceSegment-backed batches, got " +
                    $"[{batch.Start.GetObject()?.GetType().ToString() ?? "null"}]: every ordinary-lanes " +
                    "element must originate from ArteryEncodeStage.");

            if (frame.Start.GetObject() is not ReadOnlySequenceSegment<byte> startObject)
                throw new InvalidOperationException(
                    "Artery lane frame batching requires segment-backed frames, got a memory-backed " +
                    $"sequence of [{frame.Length}] bytes: every ordinary-lanes element must originate " +
                    "from ArteryEncodeStage.");

            // Same bounded, slice-aware walk as AppendToWriteBuffer: never run past the tail this
            // sequence actually references, and honor the frame's own start/end offsets on the
            // first/last links.
            var endSegment = frame.End.GetObject() as ReadOnlySequenceSegment<byte>;
            var startIndex = frame.Start.GetInteger();
            var endIndex = frame.End.GetInteger();

            var segment = startObject;
            while (segment is not null)
            {
                var memory = segment.Memory;
                var isFirst = ReferenceEquals(segment, startObject);
                var isLast = ReferenceEquals(segment, endSegment);

                if (isFirst)
                    memory = memory.Slice(startIndex);
                if (isLast)
                    memory = memory.Slice(0, isFirst ? endIndex - startIndex : endIndex);

                var owner = (segment as IOwnedSequenceSegment)?.DetachOwner();
                if (owner is null)
                    throw new InvalidOperationException(
                        $"Artery lane frame batching found a frame segment ([{segment.GetType()}]) " +
                        "carrying no live pooled-buffer owner: every ordinary-lanes frame segment " +
                        "must carry the ownership ArteryEncodeStage minted for it.");

                tail = tail.Append(memory, owner);

                if (isLast)
                    break;

                segment = segment.Next;
            }

            return new ReadOnlySequence<byte>(head, batch.Start.GetInteger(), tail, tail.Memory.Length);
        }

        /// <summary>
        /// Returns whether the actor system, materializer, or transport has entered termination.
        /// This state-based filter narrows the materialization catches so an unrelated
        /// <see cref="InvalidOperationException"/> from a live system still propagates.
        /// </summary>
        private bool IsActorSystemTerminating() =>
            _isShutdown
            || _materializer is null || _materializer.IsShutdown
            || IsStreamSupervisorTerminating()
            || (System.TryGetExtension<CoordinatedShutdown>(out var coordinatedShutdown) && coordinatedShutdown.ShutdownReason != null)
            || (System is ActorSystemImpl systemImpl && systemImpl.Aborting)
            || System.WhenTerminated.IsCompleted;

        /// <summary>
        /// Whether the materializer's StreamSupervisor -- the cell every <c>Run()</c> in this
        /// class attaches its graph-interpreter children to -- is terminating or terminated.
        /// <c>ActorRefWithCell.Underlying</c> unifies the <c>LocalActorRef</c>/<c>RepointableActorRef</c>
        /// cases; an <c>UnstartedCell</c> (supervisor still spinning up) reports an empty,
        /// non-terminating container, so this can never false-positive during startup.
        /// </summary>
        private bool IsStreamSupervisorTerminating() =>
            _materializer?.Supervisor is ActorRefWithCell { Underlying: { } supervisorCell }
            && (supervisorCell.IsTerminated || supervisorCell.ChildrenContainer.IsTerminating);

        /// <summary>
        /// Releases <paramref name="streamId"/>'s materialize-once gate on
        /// <paramref name="association"/> -- the anti-wedge counterpart to the
        /// materialization catch blocks' quiet <c>return</c> paths: without it the gate stays
        /// latched "started" with no stream materialized and no restart scheduled, so producers
        /// enqueue forever and nothing ever drains. Safe against the concurrent
        /// <c>EnsureStarted</c> race: the failed materialization has nothing left to run, so a
        /// racing caller re-materializing immediately is exactly the desired outcome.
        /// </summary>
        private static void ResetGateFor(Association association, ArteryStreamId streamId)
        {
            switch (streamId)
            {
                case ArteryStreamId.Control:
                    association.ResetControlGate();
                    break;
                case ArteryStreamId.Large:
                    association.ResetLargeGate();
                    break;
                default:
                    association.ResetOutboundGate();
                    break;
            }
        }

        private static ReadOnlySequence<byte> BuildPreamble(ArteryStreamId streamId)
        {
            var buffer = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(buffer, streamId);
            return new ReadOnlySequence<byte>(buffer);
        }

        /// <summary>
        /// Builds <paramref name="lanes"/> independent <see cref="ArrayPool{T}"/> instances for
        /// <see cref="_laneEncodeBufferPools"/> -- see that field's remarks for why each lane gets
        /// its own <see cref="ArrayPool{T}.Create()"/> rather than sharing one. When
        /// <paramref name="testOverride"/> is supplied (the poison-pool regression hook), every
        /// lane uses that SAME override instance instead, mirroring how <see cref="_encodeBufferPool"/>
        /// honors the override.
        /// </summary>
        private static ArrayPool<byte>[] BuildLaneEncodeBufferPools(int lanes, ArrayPool<byte>? testOverride)
        {
            var pools = new ArrayPool<byte>[lanes];
            for (var i = 0; i < lanes; i++)
                pools[i] = testOverride ?? ArrayPool<byte>.Create();

            return pools;
        }
    }
}
