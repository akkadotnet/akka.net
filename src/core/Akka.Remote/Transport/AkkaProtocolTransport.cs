//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Util.Internal;
using Google.Protobuf;

namespace Akka.Remote.Transport
{
    /// <summary>
    /// <para>
    /// This class represents a pairing of an <see cref="AkkaProtocolTransport"/> with its <see cref="Address"/> binding.
    /// </para>
    /// <para>
    /// This is the information that's used to allow external <see cref="ActorSystem"/> messages to address
    /// this system over the network.
    /// </para>
    /// </summary>
    internal sealed class ProtocolTransportAddressPair
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProtocolTransportAddressPair"/> class.
        /// </summary>
        /// <param name="protocolTransport">The protocol transport to pair with the specified <paramref name="address"/>.</param>
        /// <param name="address">The address to pair with the specified <paramref name="protocolTransport"/>.</param>
        public ProtocolTransportAddressPair(AkkaProtocolTransport protocolTransport, Address address)
        {
            ProtocolTransport = protocolTransport;
            Address = address;
        }

        /// <summary>
        /// The protocol transport part of the pairing.
        /// </summary>
        public AkkaProtocolTransport ProtocolTransport { get; private set; }

        /// <summary>
        /// The address part of the pairing.
        /// </summary>
        public Address Address { get; private set; }
    }

    /// <summary>
    /// This exception is thrown when an error occurred during the Akka protocol handshake.
    /// </summary>
    public class AkkaProtocolException : AkkaException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaProtocolException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public AkkaProtocolException(string message, Exception cause = null) : base(message, cause) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaProtocolException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AkkaProtocolException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }

    /// <summary>
    /// Implementation of the Akka protocol as a (logical) <see cref="Transport"/> that wraps an underlying (physical) <see cref="Transport"/> instance.
    ///
    /// Features provided by this transport include:
    ///  - Soft-state associations via the use of heartbeats and failure detectors
    ///  - Transparent origin address handling
    ///
    /// This transport is loaded automatically by <see cref="Remoting"/> and will wrap all dynamically loaded transports.
    /// </summary>
    internal class AkkaProtocolTransport : ActorTransportAdapter
    {
        /// <summary>
        /// Creates a protocol transport that wraps an underlying physical transport.
        /// </summary>
        /// <param name="wrappedTransport">Physical transport used to establish and manage associations.</param>
        /// <param name="system">Actor system hosting this transport adapter.</param>
        /// <param name="settings">Handshake, heartbeat, and failure detector settings for the protocol layer.</param>
        /// <param name="codec">Codec used to encode and decode protocol data units.</param>
        public AkkaProtocolTransport(Transport wrappedTransport, ActorSystem system, AkkaProtocolSettings settings, AkkaPduCodec codec)
            : base(wrappedTransport, system)
        {
            Codec = codec;
            Settings = settings;
        }

        /// <summary>
        /// Settings governing handshakes, heartbeats, and transport failure detection.
        /// </summary>
        public AkkaProtocolSettings Settings { get; private set; }

        /// <summary>
        /// Codec used to encode and decode Akka protocol data units.
        /// </summary>
        protected AkkaPduCodec Codec { get; private set; }

        private readonly SchemeAugmenter _schemeAugmenter = new(RemoteSettings.AkkaScheme);

        /// <summary>
        /// Adds the Akka protocol scheme to addresses exposed by the wrapped transport.
        /// </summary>
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _schemeAugmenter; }
        }

        private string _managerName;
        /// <summary>
        /// Name of the manager actor coordinating associations for this wrapped transport.
        /// </summary>
        protected override string ManagerName
        {
            get
            {
                if (string.IsNullOrEmpty(_managerName))
                    _managerName = string.Format("akkaprotocolmanager.{0}.{1}", WrappedTransport.SchemeIdentifier,
                        UniqueId.GetAndIncrement());
                return _managerName;
            }
        }

        private Props _managerProps;
        /// <summary>
        /// Props used to create the local manager actor for protocol associations.
        /// </summary>
        protected override Props ManagerProps
        {
            get
            {
                return _managerProps ??= Props.Create(() => new AkkaProtocolManager(WrappedTransport, Settings))
                    .WithDeploy(Deploy.Local);
            }
        }

        /// <summary>
        /// Forwards a management command to the underlying transport.
        /// </summary>
        /// <param name="message">Management command to forward.</param>
        /// <returns>A task that completes with the underlying transport's command result.</returns>
        public override Task<bool> ManagementCommand(object message)
        {
            return WrappedTransport.ManagementCommand(message);
        }

        /// <summary>
        /// Requests an association after removing the Akka protocol scheme from the remote address.
        /// </summary>
        /// <param name="remoteAddress">Remote protocol address to associate with.</param>
        /// <param name="refuseUid">Optional remote UID to reject if it matches the handshake UID.</param>
        /// <returns>A task that completes with the protocol association handle or fails if association fails.</returns>
        public async Task<AkkaProtocolHandle> Associate(Address remoteAddress, long? refuseUid)
        {
            // Prepare a Task and pass its completion source to the manager
            var statusPromise = new TaskCompletionSource<AssociationHandle>();

            manager.Tell(new AssociateUnderlyingRefuseUid(SchemeAugmenter.RemoveScheme(remoteAddress), statusPromise, refuseUid));

            return (AkkaProtocolHandle)await statusPromise.Task.ConfigureAwait(false);
        }

        #region Static properties

        /// <summary>
        /// Counter used to make protocol manager actor names unique within the process.
        /// </summary>
        public static AtomicCounter UniqueId = new(0);

        #endregion
    }

    /// <summary>
    /// Actor that creates per-association protocol state actors for the wrapped transport.
    /// </summary>
    internal sealed class AkkaProtocolManager : ActorTransportAdapterManager
    {
        /// <summary>
        /// Creates a manager for associations on an underlying transport.
        /// </summary>
        /// <param name="wrappedTransport">Underlying transport used for physical associations.</param>
        /// <param name="settings">Settings used to initialize each protocol state actor.</param>
        public AkkaProtocolManager(Transport wrappedTransport, AkkaProtocolSettings settings)
        {
            _wrappedTransport = wrappedTransport;
            _settings = settings;
        }

        private readonly Transport _wrappedTransport;

        private readonly AkkaProtocolSettings _settings;

        /// <summary>
        /// The <see cref="AkkaProtocolTransport"/> does not handle recovery of associations, this task is implemented
        /// in the remoting itself. Hence the strategy <see cref="Directive.Stop"/>.
        /// </summary>
        private readonly SupervisorStrategy _supervisor = new OneForOneStrategy(_ => Directive.Stop);
        /// <summary>
        /// Stops a failed child; association recovery is handled by remoting.
        /// </summary>
        /// <returns>The supervision strategy for this manager's children.</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _supervisor;
        }

        #region ActorBase / ActorTransportAdapterManager overrides

        /// <summary>
        /// Handles inbound associations and requests to establish outbound associations.
        /// </summary>
        /// <param name="message">Inbound association or outbound association request.</param>
        protected override void Ready(object message)
        {
            switch (message)
            {
                case InboundAssociation ia:
                    var handle = ia.Association;
                    var stateActorLocalAddress = LocalAddress;
                    var stateActorAssociationListener = AssociationListener;
                    var stateActorSettings = _settings;
                    var failureDetector = CreateTransportFailureDetector();
                    Context.ActorOf(RARP.For(Context.System).ConfigureDispatcher(ProtocolStateActor.InboundProps(
                        new HandshakeInfo(stateActorLocalAddress, AddressUidExtension.Uid(Context.System)),
                        handle,
                        stateActorAssociationListener,
                        stateActorSettings,
                        new AkkaPduProtobuffCodec(Context.System),
                        failureDetector)), ActorNameFor(handle.RemoteAddress));
                    break;
                case AssociateUnderlying au:
                    CreateOutboundStateActor(au.RemoteAddress, au.StatusPromise, null);
                    break;
                case AssociateUnderlyingRefuseUid aur:
                    CreateOutboundStateActor(aur.RemoteAddress, aur.StatusCompletionSource, aur.RefuseUid);
                    break;
                default:
                    Unhandled(message);
                    break;
            }
        }

        #endregion

        #region Actor creation methods

        private string ActorNameFor(Address remoteAddress)
        {
            return string.Format("akkaProtocol-{0}-{1}", AddressUrlEncoder.Encode(remoteAddress), NextId());
        }

        private void CreateOutboundStateActor(Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusPromise, long? refuseUid)
        {
            var stateActorLocalAddress = LocalAddress;
            var stateActorSettings = _settings;
            var stateActorWrappedTransport = _wrappedTransport;
            var failureDetector = CreateTransportFailureDetector();

            Context.ActorOf(RARP.For(Context.System).ConfigureDispatcher(ProtocolStateActor.OutboundProps(
                new HandshakeInfo(stateActorLocalAddress, AddressUidExtension.Uid(Context.System)),
                remoteAddress,
                statusPromise,
                stateActorWrappedTransport,
                stateActorSettings,
                new AkkaPduProtobuffCodec(Context.System), failureDetector, refuseUid)),
                ActorNameFor(remoteAddress));
        }

        private FailureDetector CreateTransportFailureDetector()
        {
            return Context.LoadFailureDetector(_settings.TransportFailureDetectorImplementationClass,
                _settings.TransportFailureDetectorConfig);
        }

        #endregion
    }

    /// <summary>
    /// Manager message requesting an outbound association, optionally refusing a particular remote UID.
    /// </summary>
    internal sealed class AssociateUnderlyingRefuseUid : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Creates an outbound association request for a remote address.
        /// </summary>
        /// <param name="remoteAddress">Remote address to associate with.</param>
        /// <param name="statusCompletionSource">Completion source for the protocol-wrapped association handle.</param>
        /// <param name="refuseUid">Optional remote UID that the handshake must reject if it matches.</param>
        public AssociateUnderlyingRefuseUid(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, long? refuseUid = null)
        {
            RefuseUid = refuseUid;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// Remote address requested by the caller.
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// Completion source completed when the association succeeds or fails.
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        /// <summary>
        /// Optional remote UID to reject during the handshake.
        /// </summary>
        public long? RefuseUid { get; private set; }
    }

    /// <summary>
    /// Origin address and UID exchanged during the association handshake.
    /// </summary>
    internal sealed class HandshakeInfo
    {
        /// <summary>
        /// Creates handshake information for a remote endpoint.
        /// </summary>
        /// <param name="origin">Address advertised by the endpoint sending this handshake information.</param>
        /// <param name="uid">Unique identifier of the endpoint's actor system incarnation.</param>
        public HandshakeInfo(Address origin, long uid)
        {
            Origin = origin;
            Uid = uid;
        }

        /// <summary>
        /// Address advertised by the endpoint sending this handshake information.
        /// </summary>
        public Address Origin { get; private set; }

        /// <summary>
        /// Unique identifier of the endpoint's actor system incarnation.
        /// </summary>
        public long Uid { get; private set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is HandshakeInfo info && Equals(info);
        }

        private bool Equals(HandshakeInfo other)
        {
            return Equals(Origin, other.Origin) && Uid == other.Uid;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((Origin != null ? Origin.GetHashCode() : 0) * 397) ^ Uid.GetHashCode();
            }
        }
    }

    /// <summary>
    /// Association handle that exposes the Akka protocol layer over an underlying transport handle.
    /// </summary>
    internal sealed class AkkaProtocolHandle : AbstractTransportAdapterHandle
    {
        /// <summary>
        /// Creates a protocol handle for an established underlying association.
        /// </summary>
        /// <param name="originalLocalAddress">Local address exposed by the protocol transport.</param>
        /// <param name="originalRemoteAddress">Remote address exposed by the protocol transport.</param>
        /// <param name="readHandlerCompletionSource">Completion source for the protocol-level inbound event listener.</param>
        /// <param name="wrappedHandle">Underlying transport association handle.</param>
        /// <param name="handshakeInfo">Handshake information received from the remote endpoint.</param>
        /// <param name="stateActor">Protocol state actor managing this association.</param>
        /// <param name="codec">Codec used to frame payloads and control PDUs.</param>
        public AkkaProtocolHandle(Address originalLocalAddress, Address originalRemoteAddress,
            TaskCompletionSource<IHandleEventListener> readHandlerCompletionSource, AssociationHandle wrappedHandle,
            HandshakeInfo handshakeInfo, IActorRef stateActor, AkkaPduCodec codec)
            : base(originalLocalAddress, originalRemoteAddress, wrappedHandle, RemoteSettings.AkkaScheme)
        {
            HandshakeInfo = handshakeInfo;
            StateActor = stateActor;
            ReadHandlerSource = readHandlerCompletionSource;
            Codec = codec;
        }

        /// <summary>
        /// The current handshake information.
        /// </summary>
        public readonly HandshakeInfo HandshakeInfo;

        /// <summary>
        /// The <see cref="ProtocolStateActor"/> responsible for this association.
        /// </summary>
        public readonly IActorRef StateActor;

        /// <summary>
        /// The codec instance used for managing transport-specific commands.
        /// </summary>
        public readonly AkkaPduCodec Codec;

        /// <inheritdoc cref="AssociationHandle"/>
        public override bool Write(ByteString payload)
        {
            return WrappedHandle.Write(Codec.ConstructPayload(payload));
        }

#pragma warning disable CS0672 // Member overrides obsolete member
        /// <inheritdoc cref="AssociationHandle"/>
        public override void Disassociate()
#pragma warning restore CS0672 // Member overrides obsolete member
        {
            Disassociate(DisassociateInfo.Unknown);
        }

        /// <summary>
        /// Forces a disassociation of the current transport.
        /// </summary>
        /// <param name="info">The reason for disassociating.</param>
        public void Disassociate(DisassociateInfo info)
        {
            StateActor.Tell(new DisassociateUnderlying(info));
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((AkkaProtocolHandle)obj);
        }

        /// <inheritdoc/>
        private bool Equals(AkkaProtocolHandle other)
        {
            return base.Equals(other) && Equals(HandshakeInfo, other.HandshakeInfo) && Equals(StateActor, other.StateActor);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = base.GetHashCode();
                hashCode = (hashCode * 397) ^ (HandshakeInfo != null ? HandshakeInfo.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (StateActor != null ? StateActor.GetHashCode() : 0);
                return hashCode;
            }
        }
    }

    /// <summary>
    /// States tracked by a per-association protocol state actor.
    /// </summary>
    internal enum AssociationState
    {
        /// <summary>
        /// The underlying association is not established.
        /// </summary>
        Closed = 0,
        /// <summary>
        /// The underlying association is established and the protocol handshake is in progress.
        /// </summary>
        WaitHandshake = 1,
        /// <summary>
        /// The protocol handshake has completed and the association is open.
        /// </summary>
        Open = 2
    }

    /// <summary>
    /// Timer event used to trigger a heartbeat or check transport liveness.
    /// </summary>
    internal sealed class HeartbeatTimer : INoSerializationVerificationNeeded { }

    internal sealed class HandshakeTimer : INoSerializationVerificationNeeded { }

    /// <summary>
    /// Event carrying the handle returned by an outbound association attempt.
    /// </summary>
    internal sealed class HandleMsg : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Creates an event for the handle returned by an outbound association attempt.
        /// </summary>
        /// <param name="handle">The established underlying association handle.</param>
        public HandleMsg(AssociationHandle handle)
        {
            Handle = handle;
        }

        /// <summary>
        /// Gets the established handle for the outbound association.
        /// </summary>
        public AssociationHandle Handle { get; private set; }
    }

    /// <summary>
    /// Event indicating that an inbound event listener has been registered.
    /// </summary>
    internal sealed class HandleListenerRegistered : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Creates a listener-registration event.
        /// </summary>
        /// <param name="listener">Listener that will receive protocol-level association events.</param>
        public HandleListenerRegistered(IHandleEventListener listener)
        {
            Listener = listener;
        }

        /// <summary>
        /// Listener registered for protocol-level association events.
        /// </summary>
        public IHandleEventListener Listener { get; private set; }
    }

    /// <summary>
    /// Base class for data retained while a protocol association changes state.
    /// </summary>
    internal abstract class ProtocolStateData { }
    /// <summary>
    /// Base class for initial association data, before protocol state transitions begin.
    /// </summary>
    internal abstract class InitialProtocolStateData : ProtocolStateData { }

    /// <summary>
    /// Neither the underlying nor the provided transport is associated
    /// </summary>
    internal sealed class OutboundUnassociated : InitialProtocolStateData
    {
        /// <summary>
        /// Creates state data for an outbound association before the underlying transport connects.
        /// </summary>
        /// <param name="remoteAddress">Address the underlying transport should connect to.</param>
        /// <param name="statusCompletionSource">Completion source for the resulting association handle.</param>
        /// <param name="transport">Underlying transport used to create the association.</param>
        public OutboundUnassociated(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport transport)
        {
            Transport = transport;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// Address the underlying transport should connect to.
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// Completion source for the resulting association handle.
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        /// <summary>
        /// Underlying transport used to create the association.
        /// </summary>
        public Transport Transport { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handshake of the Akka protocol is not yet finished
    /// </summary>
    internal sealed class OutboundUnderlyingAssociated : ProtocolStateData
    {
        /// <summary>
        /// Creates state data for an established outbound transport association awaiting handshake completion.
        /// </summary>
        /// <param name="statusCompletionSource">Completion source for the protocol-level association handle.</param>
        /// <param name="wrappedHandle">Established underlying association handle.</param>
        public OutboundUnderlyingAssociated(TaskCompletionSource<AssociationHandle> statusCompletionSource, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            StatusCompletionSource = statusCompletionSource;
        }

        /// <summary>
        /// Completion source for the protocol-level association handle.
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        /// <summary>
        /// Established underlying association handle.
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handshake of the akka protocol is not yet finished
    /// </summary>
    internal sealed class InboundUnassociated : InitialProtocolStateData
    {
        /// <summary>
        /// Creates state data for an inbound underlying association before its protocol handshake begins.
        /// </summary>
        /// <param name="associationEventListener">Listener that receives a completed inbound protocol association.</param>
        /// <param name="wrappedHandle">Established underlying association handle.</param>
        public InboundUnassociated(IAssociationEventListener associationEventListener, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            AssociationEventListener = associationEventListener;
        }

        /// <summary>
        /// Listener that receives the completed inbound protocol association.
        /// </summary>
        public IAssociationEventListener AssociationEventListener { get; private set; }

        /// <summary>
        /// Established underlying association handle.
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handler for the handle has not been provided yet
    /// </summary>
    internal sealed class AssociatedWaitHandler : ProtocolStateData
    {
        /// <summary>
        /// Creates state data for an open association whose protocol event listener has not registered yet.
        /// </summary>
        /// <param name="handlerListener">Task that completes when the protocol event listener is registered.</param>
        /// <param name="wrappedHandle">Underlying association handle.</param>
        /// <param name="queue">Inbound payloads buffered until the listener is registered.</param>
        public AssociatedWaitHandler(Task<IHandleEventListener> handlerListener, AssociationHandle wrappedHandle, Queue<ByteString> queue)
        {
            Queue = queue;
            WrappedHandle = wrappedHandle;
            HandlerListener = handlerListener;
        }

        /// <summary>
        /// Task that completes when the protocol event listener is registered.
        /// </summary>
        public Task<IHandleEventListener> HandlerListener { get; private set; }

        /// <summary>
        /// Underlying association handle.
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }

        /// <summary>
        /// Inbound payloads buffered until the listener is registered.
        /// </summary>
        public Queue<ByteString> Queue { get; private set; }
    }

    /// <summary>
    /// System ready!
    /// </summary>
    internal sealed class ListenerReady : ProtocolStateData
    {
        /// <summary>
        /// Creates state data for an open association with its protocol event listener registered.
        /// </summary>
        /// <param name="listener">Registered listener for protocol-level association events.</param>
        /// <param name="wrappedHandle">Underlying association handle.</param>
        public ListenerReady(IHandleEventListener listener, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            Listener = listener;
        }

        /// <summary>
        /// Registered listener for protocol-level association events.
        /// </summary>
        public IHandleEventListener Listener { get; private set; }

        /// <summary>
        /// Underlying association handle.
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// Message sent when a <see cref="FailureDetector.IsAvailable"/> returns false, signaling a transport timeout.
    /// </summary>
    internal sealed class TimeoutReason
    {
        /// <summary>
        /// Creates a timeout reason for a handshake or failure detector timeout.
        /// </summary>
        /// <param name="errorMessage">Message describing the timeout.</param>
        public TimeoutReason(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// Description of the timeout.
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Timeout: {ErrorMessage}";
        }
    }

    /// <summary>
    /// Failure reason indicating that the remote UID was explicitly refused.
    /// </summary>
    internal sealed class ForbiddenUidReason { }

    /// <summary>
    /// INTERNAL API.
    /// </summary>
    internal sealed class ProtocolStateActor : FSM<AssociationState, ProtocolStateData>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly InitialProtocolStateData _initialData;
        private readonly HandshakeInfo _localHandshakeInfo;
        private readonly long? _refuseUid;
        private readonly AkkaProtocolSettings _settings;
        private readonly Address _localAddress;
        private readonly AkkaPduCodec _codec;
        private readonly FailureDetector _failureDetector;

        private const string HandshakeTimerKey = "handshake-timer";

        /// <summary>
        /// Constructor for outbound ProtocolStateActors
        /// </summary>
        /// <param name="handshakeInfo">Local endpoint address and UID to advertise during the handshake.</param>
        /// <param name="remoteAddress">Remote address to connect to.</param>
        /// <param name="statusCompletionSource">Completion source for the protocol association handle.</param>
        /// <param name="transport">Underlying transport used to establish the connection.</param>
        /// <param name="settings">Protocol handshake and heartbeat settings.</param>
        /// <param name="codec">Codec used to encode and decode protocol PDUs.</param>
        /// <param name="failureDetector">Failure detector used to monitor the association.</param>
        /// <param name="refuseUid">Optional remote UID that the actor must reject.</param>
        public ProtocolStateActor(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport transport,
            AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, long? refuseUid = null)
            : this(
                new OutboundUnassociated(remoteAddress, statusCompletionSource, transport), handshakeInfo, settings, codec, failureDetector,
                refuseUid)
        {

        }

        /// <summary>
        /// Constructor for inbound ProtocolStateActors
        /// </summary>
        /// <param name="handshakeInfo">Local endpoint address and UID to advertise during the handshake.</param>
        /// <param name="wrappedHandle">Already established underlying association handle.</param>
        /// <param name="associationEventListener">Listener notified when the protocol association is ready.</param>
        /// <param name="settings">Protocol handshake and heartbeat settings.</param>
        /// <param name="codec">Codec used to encode and decode protocol PDUs.</param>
        /// <param name="failureDetector">Failure detector used to monitor the association.</param>
        public ProtocolStateActor(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle, IAssociationEventListener associationEventListener, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector)
            : this(new InboundUnassociated(associationEventListener, wrappedHandle), handshakeInfo, settings, codec, failureDetector, refuseUid: null) { }

        /// <summary>
        /// Common constructor used by both the outbound and the inbound cases
        /// </summary>
        /// <param name="initialData">State data describing the initial inbound or outbound association.</param>
        /// <param name="localHandshakeInfo">Local endpoint address and UID to advertise during the handshake.</param>
        /// <param name="settings">Protocol handshake and heartbeat settings.</param>
        /// <param name="codec">Codec used to encode and decode protocol PDUs.</param>
        /// <param name="failureDetector">Failure detector used to monitor the association.</param>
        /// <param name="refuseUid">Optional remote UID that the actor must reject.</param>
        /// <exception cref="AkkaProtocolException">
        /// This exception is thrown for a number of reasons that include the following:
        /// <dl>
        ///   <dt><b>when in the <see cref="AssociationState.WaitHandshake"/> state</b></dt>
        ///   <dd>
        ///     <dl>
        ///       <dt><b>the event is of type <see cref="HeartbeatTimer"/></b></dt>
        ///       <dd>This exception is thrown when there is no response from the remote system causing the timeout.</dd>
        ///     </dl>
        ///   </dd>
        ///   <dt><b>when in the <see cref="AssociationState.Open"/> state</b></dt>
        ///   <dd>
        ///     <dl>
        ///       <dt><b>the event is of type <see cref="InboundPayload"/></b></dt>
        ///       <dd>This exception is thrown when the message type could not be handled.</dd>
        ///       <dt><b>the event is of type <see cref="HeartbeatTimer"/></b></dt>
        ///       <dd>This exception is thrown when there is no response from the remote system causing the timeout.</dd>
        ///       <dt><b>the event is of type <see cref="DisassociateUnderlying"/></b></dt>
        ///       <dd>This exception is thrown when the message type could not be handled.</dd>
        ///     </dl>
        ///   </dd>
        ///   <dt><b>when the FSM is terminating <see cref="FSM{TState, TData}.OnTermination(Action{Akka.Actor.FSMBase.StopEvent{TState, TData}})"/></b></dt>
        ///   <dd>
        ///     <dl>
        ///       <dt><b>the event is of type <see cref="OutboundUnassociated"/></b></dt>
        ///       <dd>This exception is thrown when the transport disassociated before the handshake finished.</dd>
        ///       <dt><b>the event is of type <see cref="OutboundUnderlyingAssociated" /> with <see cref="Akka.Actor.FSMBase.StopEvent{TState, TData}.Reason "/>
        ///           being <see cref="Akka.Actor.FSMBase.Failure"/></b>
        ///       </dt>
        ///       <dd>This exception is thrown when either a timeout occurs, the remote system is shutting down,
        ///           this system has been quarantined, or the transport disassociated before handshake finished.
        ///       </dd>
        ///     </dl>
        ///   </dd>
        /// </dl>
        /// </exception>
        private ProtocolStateActor(InitialProtocolStateData initialData, HandshakeInfo localHandshakeInfo, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, long? refuseUid)
        {
            _initialData = initialData;
            _localHandshakeInfo = localHandshakeInfo;
            _settings = settings;
            _refuseUid = refuseUid;
            _localAddress = _localHandshakeInfo.Origin;
            _codec = codec;
            _failureDetector = failureDetector;
            InitializeFSM();
        }

        #region FSM bindings

        private void InitializeFSM()
        {
            When(AssociationState.Closed, fsmEvent =>
            {
                switch (fsmEvent.FsmEvent)
                {
                    case Status.Failure f:
                        switch (fsmEvent.StateData)
                        {
                            case OutboundUnassociated ou:
                                ou.StatusCompletionSource.SetException(f.Cause);
                                return Stop();
                        }

                        return null;
                    case HandleMsg h:
                        switch (fsmEvent.StateData)
                        {
                            case OutboundUnassociated ou:
                                /*
                                 * Association has been established, but handshake is not yet complete.
                                 * This actor, the outbound ProtocolStateActor, can now set itself as
                                 * the read handler for the remainder of the handshake process.
                                 */
                                var wrappedHandle = h.Handle;
                                var statusPromise = ou.StatusCompletionSource;
                                wrappedHandle.ReadHandlerSource.TrySetResult(new ActorHandleEventListener(Self));
                                if (SendAssociate(wrappedHandle, _localHandshakeInfo))
                                {
                                    _failureDetector.HeartBeat();
                                    InitHeartbeatTimer();
                                    // wait for reply from the inbound side of the connection (WaitHandshake)
                                    return
                                        GoTo(AssociationState.WaitHandshake)
                                            .Using(new OutboundUnderlyingAssociated(statusPromise, wrappedHandle));
                                }
                                else
                                {
                                    // Underlying transport was busy -- Associate could not be sent
                                    SetTimer("associate-retry", new HandleMsg(wrappedHandle),
                                        RARP.For(Context.System).Provider
                                            .RemoteSettings.BackoffPeriod, repeat: false);
                                    return Stay();
                                }
                        }

                        return Stay();
                    case DisassociateUnderlying du:
                        return Stop();
                    case HandshakeTimer t when fsmEvent.StateData is OutboundUnassociated ou:
                        var errMsg = $"No response from remote for outbound association. Associate timed out after " +
                                     $"[{_settings.HandshakeTimeout.TotalMilliseconds} ms].";
                        ou.StatusCompletionSource.SetException(new TimeoutException(errMsg));
                        return Stop(new Failure(new TimeoutReason(errMsg)));
                    default:
                        return Stay();
                }
            });

            //Transport layer events for outbound associations
            When(AssociationState.WaitHandshake, @event =>
            {
                switch (@event.FsmEvent)
                {
                    case Disassociated d:
                        return Stop(new Failure(d.Info));
                    case InboundPayload p when @event.StateData is OutboundUnderlyingAssociated ola:
                        {
                            var pdu = DecodePdu(p.Payload);
                            /*
                             * This state is used for OutboundProtocolState actors when they receive
                             * a reply back from the inbound end of the association.
                             */
                            var wrappedHandle = ola.WrappedHandle;
                            var statusCompletionSource = ola.StatusCompletionSource;
                            switch (pdu)
                            {
                                case Associate a:
                                    var handshakeInfo = a.Info;
                                    if (_refuseUid.HasValue && _refuseUid == handshakeInfo.Uid) //refused UID
                                    {
                                        SendDisassociate(wrappedHandle, DisassociateInfo.Quarantined);
                                        return Stop(new Failure(new ForbiddenUidReason()));
                                    }
                                    else //accepted UID
                                    {
                                        _failureDetector.HeartBeat();
                                        CancelTimer(HandshakeTimerKey);
                                        return
                                            GoTo(AssociationState.Open)
                                                .Using(
                                                    new AssociatedWaitHandler(
                                                        NotifyOutboundHandler(wrappedHandle, handshakeInfo,
                                                            statusCompletionSource), wrappedHandle,
                                                        new Queue<ByteString>()));
                                    }
                                case Disassociate d:
                                    //After receiving Disassociate we MUST NOT send back a Disassociate (loop)
                                    return Stop(new Failure(d.Reason));
                                default:
                                    //Expect handshake to be finished, dropping connection
                                    if (_log.IsDebugEnabled)
                                        _log.Debug("Expected message of type Associate; instead received {0}", @event.FsmEvent.GetType());

                                    SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                                    return Stop();
                            }
                        }

                    case HeartbeatTimer t when @event.StateData is OutboundUnderlyingAssociated oua:
                        return HandleTimers(oua.WrappedHandle);

                    // Events for inbound associations
                    case InboundPayload p when @event.StateData is InboundUnassociated iu:
                        {
                            var pdu = DecodePdu(p.Payload);
                            /*
                             * This state is used by inbound protocol state actors
                             * when they receive an association attempt from the
                             * outbound side of the association.
                             */
                            var associationHandler = iu.AssociationEventListener;
                            var wrappedHandle = iu.WrappedHandle;
                            switch (pdu)
                            {
                                case Disassociate d:
                                    // After receiving Disassociate we MUST NOT send back a Disassociate (loop)
                                    return Stop(new Failure(d.Reason));
                                case Associate a:
                                    // Incoming association -- implicitly ACK by a heartbeat
                                    SendAssociate(wrappedHandle, _localHandshakeInfo);
                                    _failureDetector.HeartBeat();
                                    InitHeartbeatTimer();
                                    CancelTimer(HandshakeTimerKey);
                                    return GoTo(AssociationState.Open).Using(
                                        new AssociatedWaitHandler(
                                            NotifyInboundHandler(wrappedHandle, a.Info, associationHandler),
                                            wrappedHandle, new Queue<ByteString>()));

                                // Got a stray message -- explicitly reset the association (force remote endpoint to reassociate)
                                default:
                                    if (_log.IsDebugEnabled)
                                        _log.Debug("Sending disassociate to [{0}] because unexpected message of type [{1}] was received unassociated.", wrappedHandle, @event.FsmEvent.GetType());
                                    SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                                    return Stop();
                            }
                        }

                    case HandshakeTimer t when @event.StateData is OutboundUnderlyingAssociated oua:
                        if (_log.IsDebugEnabled)
                            _log.Debug("Sending disassociate to [{0}] because handshake timed out for outbound association after [{1}] ms.", oua.WrappedHandle, _settings.HandshakeTimeout.TotalMilliseconds);
                        SendDisassociate(oua.WrappedHandle, DisassociateInfo.Unknown);
                        return Stop(new Failure(new TimeoutReason(
                            $"No response from remote for outbound association. Handshake timed out after [{_settings.HandshakeTimeout.TotalMilliseconds}] ms")));
                    case HandshakeTimer t when @event.StateData is InboundUnassociated iu:
                        if (_log.IsDebugEnabled)
                            _log.Debug("Sending disassociate to [{0}] because handshake timed out for inbound association after [{1}] ms.", iu.WrappedHandle, _settings.HandshakeTimeout.TotalMilliseconds);
                        SendDisassociate(iu.WrappedHandle, DisassociateInfo.Unknown);
                        return Stop(new Failure(new TimeoutReason(
                            $"No response from remote for inbound association. Handshake timed out after [{_settings.HandshakeTimeout.TotalMilliseconds}] ms")));
                    case UnderlyingTransportError ue:
                        PublishError(ue);
                        return Stay();
                    default:
                        return null;
                }
            });

            When(AssociationState.Open, @event =>
            {
                switch (@event.FsmEvent)
                {
                    case Disassociated d:
                        return Stop(new Failure(d.Info));
                    case InboundPayload ip:
                        {
                            var pdu = DecodePdu(ip.Payload);
                            switch (pdu)
                            {
                                case Disassociate d:
                                    return Stop(new Failure(d.Reason));
                                case Heartbeat h:
                                    _failureDetector.HeartBeat();
                                    return Stay();
                                case Payload p:
                                    // use incoming ordinary message as alive sign
                                    _failureDetector.HeartBeat();
                                    switch (@event.StateData)
                                    {
                                        case AssociatedWaitHandler awh:
                                            var nQueue = new Queue<ByteString>(awh.Queue);
                                            nQueue.Enqueue(p.Bytes);
                                            return
                                                Stay()
                                                    .Using(new AssociatedWaitHandler(awh.HandlerListener, awh.WrappedHandle,
                                                        nQueue));
                                        case ListenerReady lr:
                                            lr.Listener.Notify(new InboundPayload(p.Bytes));
                                            return Stay();
                                        default:
                                            throw new AkkaProtocolException(
                                                $"Unhandled message in state Open(InboundPayload) with type [{@event.FsmEvent?.GetType()}]");
                                    }
                                default:
                                    return Stay();
                            }
                        }
                    case HeartbeatTimer ht when @event.StateData is AssociatedWaitHandler awh:
                        return HandleTimers(awh.WrappedHandle);
                    case HeartbeatTimer ht when @event.StateData is ListenerReady lr:
                        return HandleTimers(lr.WrappedHandle);
                    case DisassociateUnderlying dl:
                        {
                            AssociationHandle GetHandle(ProtocolStateData data)
                            {
                                switch (data)
                                {
                                    case ListenerReady lr:
                                        return lr.WrappedHandle;
                                    case AssociatedWaitHandler awh:
                                        return awh.WrappedHandle;
                                    default:
                                        throw new AkkaProtocolException(
                                            $"Unhandled message in state Open(DisassociateUnderlying) with type [{@event.FsmEvent.GetType()}]");
                                }
                            }

                            var handle = GetHandle(@event.StateData);

                            // No debug logging here as sending DisassociateUnderlying(Unknown) should have been logged from where
                            // it was sent
                            SendDisassociate(handle, dl.Info);
                            return Stop();
                        }
                    case HandleListenerRegistered hlr when @event.StateData is AssociatedWaitHandler awh:
                        foreach (var p in awh.Queue)
                        {
                            hlr.Listener.Notify(new InboundPayload(p));
                        }

                        return Stay().Using(new ListenerReady(hlr.Listener, awh.WrappedHandle));
                    case UnderlyingTransportError e:
                        PublishError(e);
                        return Stay();
                    default:
                        return null;
                }
            });

            OnTermination(@event =>
            {
                switch (@event.StateData)
                {
                    case OutboundUnassociated ou:
                        ou.StatusCompletionSource.TrySetException(@event.Reason is Failure
                            ? new AkkaProtocolException(@event.Reason.ToString())
                            : new AkkaProtocolException("Transport disassociated before handshake finished"));
                        break;
                    
                    case OutboundUnderlyingAssociated oua:
                        Exception associationFailure = null;
                        switch (@event.Reason)
                        {
                            case Failure f:
                                switch (f.Cause)
                                {
                                    case TimeoutReason timeout:
                                        associationFailure = new AkkaProtocolException(timeout.ErrorMessage);
                                        break;
                                    case ForbiddenUidReason _:
                                        associationFailure = new AkkaProtocolException("The remote system has a UID that has been quarantined. Association aborted.");
                                        break;
                                    case DisassociateInfo info:
                                        associationFailure = DisassociateException(info);
                                        break;
                                    default:
                                        associationFailure = new AkkaProtocolException($"Unknown Failure cause: [{f.Cause}]");
                                        break;
                                }
                                break;
                            default:
                                associationFailure = new AkkaProtocolException("Transport disassociated before handshake finished");
                                break;
                        }
                        oua.StatusCompletionSource.TrySetException(associationFailure);
                        oua.WrappedHandle.Disassociate(DisassociationReason(@event.Reason), _log);
                        break;
                 
                    case AssociatedWaitHandler awh:
                    {
                        // Invalidate exposed but still unfinished promise. The underlying association disappeared, so after
                        // registration immediately signal a disassociate
                        Disassociated disassociateNotification;
                        if (@event.Reason is Failure { Cause: DisassociateInfo disassociateInfo })
                        {
                            disassociateNotification =
                                new Disassociated(disassociateInfo);
                        }
                        else
                        {
                            disassociateNotification = new Disassociated(DisassociateInfo.Unknown);
                        }

                        awh.HandlerListener.ContinueWith(result => result.Result.Notify(disassociateNotification),
                            TaskContinuationOptions.ExecuteSynchronously);
                        awh.WrappedHandle.Disassociate(DisassociationReason(@event.Reason), _log);
                        break;
                    }
                    
                    case ListenerReady lr:
                    {
                        Disassociated disassociateNotification;
                        if (@event.Reason is Failure { Cause: DisassociateInfo disassociateInfo })
                        {
                            disassociateNotification =
                                new Disassociated(disassociateInfo);
                        }
                        else
                        {
                            disassociateNotification = new Disassociated(DisassociateInfo.Unknown);
                        }

                        lr.Listener.Notify(disassociateNotification);
                        lr.WrappedHandle.Disassociate(DisassociationReason(@event.Reason), _log);
                        break;
                    }
                    
                    case InboundUnassociated iu:
                        iu.WrappedHandle.Disassociate(DisassociationReason(@event.Reason), _log);
                        break;
                }
            });

            /*
             * Set the initial ProtocolStateActor state to CLOSED if OUTBOUND
             * Set the initial ProtocolStateActor state to WAITHANDSHAKE if INBOUND
             * */
            switch (_initialData)
            {
                case OutboundUnassociated d:
                    // attempt to open underlying transport to the remote address
                    // if using DotNetty, this is where the socket connection is opened.
                    d.Transport.Associate(d.RemoteAddress).ContinueWith(result => new HandleMsg(result.Result), TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self);
                    StartWith(AssociationState.Closed, d);
                    break;
                case InboundUnassociated d:
                    // inbound transport is opened already inside the ProtocolStateManager
                    // therefore we just have to set ourselves as listener and wait for
                    // incoming handshake attempts from the client.
                    d.WrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));

                    StartWith(AssociationState.WaitHandshake, d);
                    break;
            }
            InitHandshakeTimer();
        }

        private static string DisassociationReason(Reason reason)
        {
            switch (reason)
            {
                case Normal _:
                    return "the ProtocolStateActor was stopped normally";
                case Shutdown _:
                    return "the ProtocolStateActor was shutdown";
                case Failure f:
                    return $"the ProtocolStateActor failed: {f.Cause}";
                default:
                    throw new AkkaProtocolException($"Unrecognized shutdown reason: {reason}");
            }
        }

        /// <summary>
        /// Logs handshake and failure-detector termination details when the association FSM stops.
        /// </summary>
        /// <param name="reason">Reason the association state actor terminated.</param>
        protected override void LogTermination(Reason reason)
        {
            if (reason is Failure failure)
            {
                switch (failure.Cause)
                {
                    case DisassociateInfo _:
                        //no logging
                        break;
                    case ForbiddenUidReason _:
                        //no logging
                        break;
                    case TimeoutReason timeoutReason:
                        _log.Error(timeoutReason.ErrorMessage);
                        break;
                }
            }
            else
                base.LogTermination(reason);
        }

        #endregion

        protected override void PostStop()
        {
            CancelTimer("heartbeat-timer");
            base.PostStop(); //pass to OnTermination
        }


        #region Internal protocol messaging methods

        private static Exception DisassociateException(DisassociateInfo info)
        {
            switch (info)
            {
                case DisassociateInfo.Shutdown:
                    return new AkkaProtocolException("The remote system refused the association because it is shutting down.");
                case DisassociateInfo.Quarantined:
                    return new AkkaProtocolException("The remote system has quarantined this system. No further associations to the remote systems are possible until this system is restarted.");
                case DisassociateInfo.Unknown:
                default:
                    return new AkkaProtocolException("The remote system explicitly disassociated (reason unknown).");
            }
        }

        private State<AssociationState, ProtocolStateData> HandleTimers(AssociationHandle wrappedHandle)
        {
            if (_failureDetector.IsAvailable)
            {
                SendHeartBeat(wrappedHandle);
                return Stay();
            }
            else
            {
                if (_log.IsDebugEnabled)
                {
                    _log.Debug("Sending disassociate to [{0}] because failure detector triggered in state [{1}]", wrappedHandle, StateName);
                }

                //send disassociate just to be sure
                SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                return Stop(new Failure(new TimeoutReason($"No response from remote. Handshake timed out or transport failure detector triggered. (internal state was {StateName})")));
            }
        }

        private void ListenForListenerRegistration(TaskCompletionSource<IHandleEventListener> readHandlerSource)
        {
            readHandlerSource.Task.ContinueWith(rh => new HandleListenerRegistered(rh.Result),
                TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self);
        }

        private Task<IHandleEventListener> NotifyOutboundHandler(AssociationHandle wrappedHandle,
            HandshakeInfo handshakeInfo, TaskCompletionSource<AssociationHandle> statusPromise)
        {
            var readHandlerPromise = new TaskCompletionSource<IHandleEventListener>();
            ListenForListenerRegistration(readHandlerPromise);

            statusPromise.SetResult(new AkkaProtocolHandle(_localAddress, wrappedHandle.RemoteAddress,
                readHandlerPromise, wrappedHandle, handshakeInfo, Self, _codec));

            return readHandlerPromise.Task;
        }

        private Task<IHandleEventListener> NotifyInboundHandler(AssociationHandle wrappedHandle,
            HandshakeInfo handshakeInfo, IAssociationEventListener associationEventListener)
        {
            var readHandlerPromise = new TaskCompletionSource<IHandleEventListener>();
            ListenForListenerRegistration(readHandlerPromise);

            associationEventListener.Notify(
                new InboundAssociation(
                    new AkkaProtocolHandle(_localAddress, handshakeInfo.Origin, readHandlerPromise, wrappedHandle, handshakeInfo, Self, _codec)));
            return readHandlerPromise.Task;
        }

        private IAkkaPdu DecodePdu(ByteString pdu)
        {
            try
            {
                return _codec.DecodePdu(pdu);
            }
            catch (Exception ex)
            {
                throw new AkkaProtocolException($"Error while decoding incoming Akka PDU of length {pdu.Length}", ex);
            }
        }

        private void InitHeartbeatTimer()
        {
            SetTimer("heartbeat-timer", new HeartbeatTimer(), _settings.TransportHeartBeatInterval, true);
        }

        private void InitHandshakeTimer()
        {
            SetTimer(HandshakeTimerKey, new HandshakeTimer(), _settings.HandshakeTimeout, true);
        }

        private bool SendAssociate(AssociationHandle wrappedHandle, HandshakeInfo info)
        {
            try
            {
                return wrappedHandle.Write(_codec.ConstructAssociate(info));
            }
            catch (Exception ex)
            {
                throw new AkkaProtocolException("Error writing ASSOCIATE to transport", ex);
            }
        }

        private void SendDisassociate(AssociationHandle wrappedHandle, DisassociateInfo info)
        {
            try
            {
                wrappedHandle.Write(_codec.ConstructDisassociate(info));
            }
            catch (Exception ex)
            {
                throw new AkkaProtocolException("Error writing DISASSOCIATE to transport", ex);
            }
        }

        private bool SendHeartBeat(AssociationHandle wrappedHandle)
        {
            try
            {
                return wrappedHandle.Write(_codec.ConstructHeartbeat());
            }
            catch (Exception ex)
            {
                throw new AkkaProtocolException("Error writing HEARTBEAT to transport", ex);
            }
        }

        /// <summary>
        /// Publishes a transport error to the message stream
        /// </summary>
        private void PublishError(UnderlyingTransportError transportError)
        {
            _log.Error(transportError.Cause, transportError.Message);
        }

        #endregion

        #region Static methods



        /// <summary>
        /// <see cref="Props"/> used when creating outbound associations to remote endpoints.
        ///
        /// These <see cref="Props"/> create outbound <see cref="ProtocolStateActor"/> instances,
        /// which start in the closed state and initiate the underlying connection.
        /// </summary>
        /// <param name="handshakeInfo">Local endpoint address and UID to advertise during the handshake.</param>
        /// <param name="remoteAddress">Remote address to connect to.</param>
        /// <param name="statusCompletionSource">Completion source for the protocol association handle.</param>
        /// <param name="transport">Underlying transport used to establish the connection.</param>
        /// <param name="settings">Protocol handshake and heartbeat settings.</param>
        /// <param name="codec">Codec used to encode and decode protocol PDUs.</param>
        /// <param name="failureDetector">Failure detector used to monitor the association.</param>
        /// <param name="refuseUid">Optional remote UID that the actor must reject.</param>
        /// <returns>Props that create an outbound protocol state actor.</returns>
        public static Props OutboundProps(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource,
            Transport transport, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, long? refuseUid = null)
        {
            return Props.Create(() => new ProtocolStateActor(handshakeInfo, remoteAddress, statusCompletionSource, transport, settings, codec, failureDetector, refuseUid));
        }

        /// <summary>
        /// Creates props for a protocol state actor managing an inbound association.
        /// </summary>
        /// <param name="handshakeInfo">Local endpoint address and UID to advertise during the handshake.</param>
        /// <param name="wrappedHandle">Already established underlying association handle.</param>
        /// <param name="associationEventListener">Listener notified when the protocol association is ready.</param>
        /// <param name="settings">Protocol handshake and heartbeat settings.</param>
        /// <param name="codec">Codec used to encode and decode protocol PDUs.</param>
        /// <param name="failureDetector">Failure detector used to monitor the association.</param>
        /// <returns>Props that create an inbound protocol state actor.</returns>
        public static Props InboundProps(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle,
            IAssociationEventListener associationEventListener, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector)
        {
            return Props.Create(() => new ProtocolStateActor(handshakeInfo, wrappedHandle, associationEventListener, settings, codec, failureDetector));
        }

        #endregion
    }
}
