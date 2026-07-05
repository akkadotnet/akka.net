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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
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
    /// This is the gate-G2 "Plaintext TCP Transport" implementation (design.md, "G2 -- Basic
    /// ordinary messaging"): a single (ordinary) TCP stream per direction, single lane, no
    /// compression, no control stream (handshake rides the ordinary connection -- "G2 staging").
    /// Message sent -> received -> dispatched to the correct actor; classic remoting is unaffected.
    /// </para>
    ///
    /// <para>
    /// <b>Connection cardinality (verify against design.md).</b> Artery uses SEPARATE per-direction
    /// TCP connections -- there is no single bidirectional "association socket". When system A
    /// first sends to a B-hosted actor, A materializes an OUTBOUND connection A-&gt;B (carrying the
    /// injected <see cref="HandshakeReq"/> ahead of the user message). B's <see cref="InboundHandshakeStage"/>
    /// replies with a <see cref="HandshakeRsp"/> via <see cref="IInboundContext.SendControl"/>, which
    /// routes through THIS SAME <see cref="EnqueueOutbound"/> mechanism keyed by A's address --
    /// i.e. B materializes (or reuses) its OWN outbound connection B-&gt;A to carry the reply. The
    /// inbound socket B accepted from A is never written to.
    /// </para>
    /// </summary>
    internal sealed class ArteryRemoting : RemoteTransport
    {
        private readonly ArterySettings _settings;
        private readonly ILoggingAdapter _log;
        private readonly AssociationRegistry _registry = new();

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
                "Artery TCP remoting is EXPERIMENTAL and under active development -- gate G2 (plaintext TCP " +
                "transport: single ordinary stream, single lane, no compression, no control stream). Do not " +
                "use in production.");

            _materializer = ActorMaterializer.Create(System);
            _tcp = System.TcpStream();
            _encodeBufferPool = System.Settings.Setup.Get<ArteryTransportSetup>()
                .Select(s => s.EncodeBufferPool)
                .GetOrElse(null);

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

            _log.Info("Artery TCP remoting started; listening on [{0}]", address);
        }

        /// <inheritdoc/>
        public override Task Shutdown()
        {
            _log.Info("Shutting down Artery TCP remoting on [{0}]", _defaultAddress);

            foreach (var association in _registry.AllAssociations)
                association.CompleteOutbound();

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
            var recipientPath = recipient.Path.ToSerializationFormatWithAddress(remoteAddress);
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

            var inboundSink = Flow.Create<ReadOnlySequence<byte>>()
                .Via(new ArteryInboundProcessingStage(_settings.MaximumFrameSize, System.Serialization))
                .Via(Flow.FromGraph(new InboundHandshakeStage(_inboundContext!)))
                .To(Sink.ForEach<IInboundEnvelope>(DispatchInbound));

            // The accepted (inbound) connection is read-only at G2: Artery uses SEPARATE
            // per-direction connections, so any reply (starting with the HandshakeRsp) goes out over
            // a NEW/reused outbound connection this system originates back towards the peer -- see
            // the type-level "Connection cardinality" remarks. We never write to this socket.
            connection.HandleWith(Flow.FromSinkAndSource(inboundSink, Source.Empty<ReadOnlySequence<byte>>()), _materializer!);
        }

        private void DispatchInbound(IInboundEnvelope env)
        {
            if (env.IsControl || env.RecipientPath is null)
            {
                // InboundHandshakeStage swallows HandshakeReq/HandshakeRsp (and any other control
                // envelope); nothing else is expected to reach this point.
                _log.Warning("Dropping unexpected inbound Artery control envelope carrying [{0}]", env.Message.GetType());
                return;
            }

            var recipient = Provider.ResolveActorRefWithLocalAddress(env.RecipientPath, DefaultAddress);
            var sender = env.SenderPath is { } senderPath
                ? Provider.ResolveActorRefWithLocalAddress(senderPath, DefaultAddress)
                : (IActorRef)System.DeadLetters;

            // Mirrors classic's DefaultMessageDispatcher semantics without depending on classic types:
            // an unresolvable recipient resolves to an EmptyLocalActorRef, whose Tell publishes a
            // DeadLetter automatically.
            recipient.Tell(env.Message, sender);
        }

        private void SendControlToAddress(Address to, object message) => EnqueueOutbound(to, message, senderPath: null, recipientPath: null);

        private void EnqueueOutbound(Address remoteAddress, object message, string? senderPath, string? recipientPath)
        {
            var association = _registry.AssociationFor(remoteAddress);
            if (!association.IsOutboundMaterialized)
                association.EnsureOutboundMaterialized(a => MaterializeOutbound(remoteAddress, a));

            if (!association.TryEnqueueOutbound(new OutboundEnvelope(message, senderPath, recipientPath)))
            {
                _log.Warning(
                    "Outbound Artery queue to [{0}] is full (capacity {1}); dropping message of type [{2}] to dead letters.",
                    remoteAddress, Association.DefaultOutboundQueueCapacity, message.GetType());
                System.DeadLetters.Tell(message, ActorRefs.NoSender);
            }
        }

        /// <summary>
        /// Materializes the outbound stream chain for <paramref name="association"/>, exactly once
        /// (gated by <see cref="Association.EnsureOutboundMaterialized"/>):
        /// <c>ChannelSource.FromReader(association.OutboundReader) -&gt; OutboundHandshakeStage -&gt;
        /// encode -&gt; prepend Ordinary preamble -&gt; Tcp().OutgoingConnection</c>. Faithful-but-minimal
        /// (G2): no reconnect/retry -- if the connection fails, this association's outbound stream
        /// simply ends (a subsequent <see cref="EnqueueOutbound"/> call will keep buffering into the
        /// channel, but nothing will ever drain it again until the process is restarted). Reconnect
        /// sophistication is out of scope for G2 (design.md "Faithful-but-minimal").
        /// </summary>
        private void MaterializeOutbound(Address remoteAddress, Association association)
        {
            var outboundContext = new AssociationRegistryOutboundContext(
                _registry,
                _localUniqueAddress,
                remoteAddress,
                message => EnqueueOutbound(remoteAddress, message, senderPath: null, recipientPath: null));

            var handshakeStage = new OutboundHandshakeStage(
                outboundContext, _settings.HandshakeRetryInterval, _settings.HandshakeTimeout, _settings.InjectHandshakeInterval);

            var host = remoteAddress.Host
                ?? throw new RemoteTransportException($"Cannot open an Artery outbound connection to [{remoteAddress}]: missing host.");
            var port = remoteAddress.Port
                ?? throw new RemoteTransportException($"Cannot open an Artery outbound connection to [{remoteAddress}]: missing port.");

            var encodeStage = new ArteryEncodeStage(System.Serialization, _localUniqueAddress.Uid, _encodeBufferPool);

            var frames = ChannelSource.FromReader(association.OutboundReader)
                .Via(Flow.FromGraph(handshakeStage))
                .Via(Flow.FromGraph(encodeStage));

            var completion = Source.Single(BuildOrdinaryPreamble())
                .Concat(frames)
                .Via(_tcp!.OutgoingConnection(host, port))
                .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), _materializer!);

            completion.ContinueWith(t =>
            {
                if (t.IsFaulted)
                    _log.Warning(
                        t.Exception?.GetBaseException(),
                        "Artery outbound connection to [{0}] failed; this association's outbound stream has " +
                        "ended (G2: no automatic reconnect).", remoteAddress);
                else
                    _log.Debug("Artery outbound connection to [{0}] completed.", remoteAddress);
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private static ReadOnlySequence<byte> BuildOrdinaryPreamble()
        {
            var buffer = new byte[ArteryConnectionHeader.Length];
            ArteryConnectionHeader.WriteTo(buffer, ArteryStreamId.Ordinary);
            return new ReadOnlySequence<byte>(buffer);
        }
    }
}
