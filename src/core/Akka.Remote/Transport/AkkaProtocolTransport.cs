//-----------------------------------------------------------------------
// <copyright file="AkkaProtocolTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.Remote.Serialization;
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
    internal class ProtocolTransportAddressPair
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

#if SERIALIZATION
        /// <summary>
        /// Initializes a new instance of the <see cref="AkkaProtocolException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo"/> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext"/> that contains contextual information about the source or destination.</param>
        protected AkkaProtocolException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
#endif
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
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="system">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="codec">TBD</param>
        public AkkaProtocolTransport(Transport wrappedTransport, ActorSystem system, AkkaProtocolSettings settings, AkkaPduCodec codec)
            : base(wrappedTransport, system)
        {
            Codec = codec;
            Settings = settings;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public AkkaProtocolSettings Settings { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        protected AkkaPduCodec Codec { get; private set; }

        private readonly SchemeAugmenter _schemeAugmenter = new SchemeAugmenter(RemoteSettings.AkkaScheme);

        /// <summary>
        /// TBD
        /// </summary>
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _schemeAugmenter; }
        }

        private string _managerName;
        /// <summary>
        /// TBD
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
        /// TBD
        /// </summary>
        protected override Props ManagerProps
        {
            get
            {
                return _managerProps ??
                       (_managerProps =
                           Props.Create(() => new AkkaProtocolManager(WrappedTransport, Settings))
                               .WithDeploy(Deploy.Local));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        public override Task<bool> ManagementCommand(object message)
        {
            return WrappedTransport.ManagementCommand(message);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="refuseUid">TBD</param>
        /// <returns>TBD</returns>
        public async Task<AkkaProtocolHandle> Associate(Address remoteAddress, int? refuseUid)
        {
            // Prepare a Task and pass its completion source to the manager
            var statusPromise = new TaskCompletionSource<AssociationHandle>();

            manager.Tell(new AssociateUnderlyingRefuseUid(SchemeAugmenter.RemoveScheme(remoteAddress), statusPromise, refuseUid));

            return (AkkaProtocolHandle)await statusPromise.Task.ConfigureAwait(false);
        }

#region Static properties

        /// <summary>
        /// TBD
        /// </summary>
        public static AtomicCounter UniqueId = new AtomicCounter(0);

#endregion
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class AkkaProtocolManager : ActorTransportAdapterManager
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="wrappedTransport">TBD</param>
        /// <param name="settings">TBD</param>
        public AkkaProtocolManager(Transport wrappedTransport, AkkaProtocolSettings settings)
        {
            _wrappedTransport = wrappedTransport;
            _settings = settings;
        }

        private Transport _wrappedTransport;

        private AkkaProtocolSettings _settings;

        /// <summary>
        /// The <see cref="AkkaProtocolTransport"/> does not handle recovery of associations, this task is implemented
        /// in the remoting itself. Hence the strategy <see cref="Directive.Stop"/>.
        /// </summary>
        private readonly SupervisorStrategy _supervisor = new OneForOneStrategy(exception => Directive.Stop);
        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return _supervisor;
        }

#region ActorBase / ActorTransportAdapterManager overrides

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        protected override void Ready(object message)
        {
            message.Match()
                .With<InboundAssociation>(ia => //need to create an Inbound ProtocolStateActor
                {
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
                })
                .With<AssociateUnderlying>(au => CreateOutboundStateActor(au.RemoteAddress, au.StatusPromise, null)) //need to create an Outbound ProtocolStateActor
                .With<AssociateUnderlyingRefuseUid>(au => CreateOutboundStateActor(au.RemoteAddress, au.StatusCompletionSource, au.RefuseUid));
        }

#endregion

#region Actor creation methods

        private string ActorNameFor(Address remoteAddress)
        {
            return string.Format("akkaProtocol-{0}-{1}", AddressUrlEncoder.Encode(remoteAddress), NextId());
        }

        private void CreateOutboundStateActor(Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusPromise, int? refuseUid)
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
            return FailureDetectorLoader.LoadFailureDetector(Context, _settings.TransportFailureDetectorImplementationClass,
                _settings.TransportFailureDetectorConfig);
        }

#endregion
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class AssociateUnderlyingRefuseUid : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="statusCompletionSource">TBD</param>
        /// <param name="refuseUid">TBD</param>
        public AssociateUnderlyingRefuseUid(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, int? refuseUid = null)
        {
            RefuseUid = refuseUid;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int? RefuseUid { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class HandshakeInfo
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="origin">TBD</param>
        /// <param name="uid">TBD</param>
        public HandshakeInfo(Address origin, int uid)
        {
            Origin = origin;
            Uid = uid;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address Origin { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public int Uid { get; private set; }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is HandshakeInfo && Equals((HandshakeInfo)obj);
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
    /// TBD
    /// </summary>
    internal class AkkaProtocolHandle : AbstractTransportAdapterHandle
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="originalLocalAddress">TBD</param>
        /// <param name="originalRemoteAddress">TBD</param>
        /// <param name="readHandlerCompletionSource">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        /// <param name="handshakeInfo">TBD</param>
        /// <param name="stateActor">TBD</param>
        /// <param name="codec">TBD</param>
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
        /// TBD
        /// </summary>
        public readonly HandshakeInfo HandshakeInfo;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly IActorRef StateActor;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly AkkaPduCodec Codec;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="payload">TBD</param>
        /// <returns>TBD</returns>
        public override bool Write(ByteString payload)
        {
            return WrappedHandle.Write(Codec.ConstructPayload(payload));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void Disassociate()
        {
            Disassociate(DisassociateInfo.Unknown);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="info">TBD</param>
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
        protected bool Equals(AkkaProtocolHandle other)
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
    /// TBD
    /// </summary>
    internal enum AssociationState
    {
        /// <summary>
        /// TBD
        /// </summary>
        Closed = 0,
        /// <summary>
        /// TBD
        /// </summary>
        WaitHandshake = 1,
        /// <summary>
        /// TBD
        /// </summary>
        Open = 2
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class HeartbeatTimer : INoSerializationVerificationNeeded { }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class HandleMsg : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handle">TBD</param>
        public HandleMsg(AssociationHandle handle)
        {
            Handle = handle;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public AssociationHandle Handle { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class HandleListenerRegistered : INoSerializationVerificationNeeded
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listener">TBD</param>
        public HandleListenerRegistered(IHandleEventListener listener)
        {
            Listener = listener;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IHandleEventListener Listener { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class ProtocolStateData { }
    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class InitialProtocolStateData : ProtocolStateData { }

    /// <summary>
    /// Neither the underlying nor the provided transport is associated
    /// </summary>
    internal sealed class OutboundUnassociated : InitialProtocolStateData
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="statusCompletionSource">TBD</param>
        /// <param name="transport">TBD</param>
        public OutboundUnassociated(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport transport)
        {
            Transport = transport;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Address RemoteAddress { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Transport Transport { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handshake of the Akka protocol is not yet finished
    /// </summary>
    internal sealed class OutboundUnderlyingAssociated : ProtocolStateData
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="statusCompletionSource">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        public OutboundUnderlyingAssociated(TaskCompletionSource<AssociationHandle> statusCompletionSource, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            StatusCompletionSource = statusCompletionSource;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handshake of the akka protocol is not yet finished
    /// </summary>
    internal sealed class InboundUnassociated : InitialProtocolStateData
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="associationEventListener">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        public InboundUnassociated(IAssociationEventListener associationEventListener, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            AssociationEventListener = associationEventListener;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IAssociationEventListener AssociationEventListener { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handler for the handle has not been provided yet
    /// </summary>
    internal sealed class AssociatedWaitHandler : ProtocolStateData
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handlerListener">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        /// <param name="queue">TBD</param>
        public AssociatedWaitHandler(Task<IHandleEventListener> handlerListener, AssociationHandle wrappedHandle, Queue<ByteString> queue)
        {
            Queue = queue;
            WrappedHandle = wrappedHandle;
            HandlerListener = handlerListener;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public Task<IHandleEventListener> HandlerListener { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public Queue<ByteString> Queue { get; private set; }
    }

    /// <summary>
    /// System ready!
    /// </summary>
    internal sealed class ListenerReady : ProtocolStateData
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="listener">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        public ListenerReady(IHandleEventListener listener, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            Listener = listener;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IHandleEventListener Listener { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// Message sent when a <see cref="FailureDetector.IsAvailable"/> returns false, signaling a transport timeout.
    /// </summary>
    internal class TimeoutReason
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="errorMessage">TBD</param>
        public TimeoutReason(string errorMessage)
        {
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"Timeout: {ErrorMessage}";
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal class ForbiddenUidReason { }

    /// <summary>
    /// TBD
    /// </summary>
    internal class ProtocolStateActor : FSM<AssociationState, ProtocolStateData>
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private InitialProtocolStateData _initialData;
        private HandshakeInfo _localHandshakeInfo;
        private int? _refuseUid;
        private AkkaProtocolSettings _settings;
        private Address _localAddress;
        private AkkaPduCodec _codec;
        private FailureDetector _failureDetector;

        /// <summary>
        /// Constructor for outbound ProtocolStateActors
        /// </summary>
        /// <param name="handshakeInfo">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="statusCompletionSource">TBD</param>
        /// <param name="transport">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="codec">TBD</param>
        /// <param name="failureDetector">TBD</param>
        /// <param name="refuseUid">TBD</param>
        public ProtocolStateActor(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport transport,
            AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, int? refuseUid = null)
            : this(
                new OutboundUnassociated(remoteAddress, statusCompletionSource, transport), handshakeInfo, settings, codec, failureDetector,
                refuseUid)
        {

        }

        /// <summary>
        /// Constructor for inbound ProtocolStateActors
        /// </summary>
        /// <param name="handshakeInfo">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        /// <param name="associationEventListener">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="codec">TBD</param>
        /// <param name="failureDetector">TBD</param>
        public ProtocolStateActor(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle, IAssociationEventListener associationEventListener, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector)
            : this(new InboundUnassociated(associationEventListener, wrappedHandle), handshakeInfo, settings, codec, failureDetector, refuseUid: null) { }

        /// <summary>
        /// Common constructor used by both the outbound and the inbound cases
        /// </summary>
        /// <param name="initialData">TBD</param>
        /// <param name="localHandshakeInfo">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="codec">TBD</param>
        /// <param name="failureDetector">TBD</param>
        /// <param name="refuseUid">TBD</param>
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
        protected ProtocolStateActor(InitialProtocolStateData initialData, HandshakeInfo localHandshakeInfo, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, int? refuseUid)
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
                State<AssociationState, ProtocolStateData> nextState = null;
                //Transport layer events for outbound associations
                fsmEvent.FsmEvent.Match()
                    .With<Status.Failure>(f => fsmEvent.StateData.Match()
                        .With<OutboundUnassociated>(ou =>
                        {
                            ou.StatusCompletionSource.SetException(f.Cause);
                            nextState = Stop();
                        }))
                    .With<HandleMsg>(h => fsmEvent.StateData.Match()
                        .With<OutboundUnassociated>(ou =>
                        {
                            /*
                             * Association has been established, but handshake is not yet complete.
                             * This actor, the outbound ProtocolStateActor, can now set itself as
                             * the read handler for the remainder of the handshake process.
                             */
                            AssociationHandle wrappedHandle = h.Handle;
                            var statusPromise = ou.StatusCompletionSource;
                            wrappedHandle.ReadHandlerSource.TrySetResult(new ActorHandleEventListener(Self));
                            if (SendAssociate(wrappedHandle, _localHandshakeInfo))
                            {
                                _failureDetector.HeartBeat();
                                InitTimers();
                                // wait for reply from the inbound side of the connection (WaitHandshake)
                                nextState =
                                    GoTo(AssociationState.WaitHandshake)
                                        .Using(new OutboundUnderlyingAssociated(statusPromise, wrappedHandle));
                            }
                            else
                            {
                                //Otherwise, retry
                                SetTimer("associate-retry", new HandleMsg(wrappedHandle),
                                    ((RemoteActorRefProvider)((ActorSystemImpl)Context.System).Provider) //TODO: rewrite using RARP ActorSystem Extension
                                        .RemoteSettings.BackoffPeriod, repeat: false);
                                nextState = Stay();
                            }
                        }))
                    .With<DisassociateUnderlying>(d =>
                    {
                        nextState = Stop();
                    })
                    .Default(m => { nextState = Stay(); });

                return nextState;
            });

            //Transport layer events for outbound associations
            When(AssociationState.WaitHandshake, @event =>
            {
                State<AssociationState, ProtocolStateData> nextState = null;

                @event.FsmEvent.Match()
                    .With<UnderlyingTransportError>(e =>
                    {
                        PublishError(e);
                        nextState = Stay();
                    })
                    .With<Disassociated>(d =>
                    {
                        nextState = Stop(new Failure(d.Info));
                    })
                    .With<InboundPayload>(m =>
                    {
                        var pdu = DecodePdu(m.Payload);
                        @event.StateData.Match()
                            .With<OutboundUnderlyingAssociated>(ola =>
                            {
                                /*
                                 * This state is used for OutboundProtocolState actors when they receive
                                 * a reply back from the inbound end of the association.
                                 */
                                var wrappedHandle = ola.WrappedHandle;
                                var statusCompletionSource = ola.StatusCompletionSource;
                                pdu.Match()
                                    .With<Associate>(a =>
                                    {

                                        var handshakeInfo = a.Info;
                                        if (_refuseUid.HasValue && _refuseUid == handshakeInfo.Uid) //refused UID
                                        {
                                            SendDisassociate(wrappedHandle, DisassociateInfo.Quarantined);
                                            nextState = Stop(new Failure(new ForbiddenUidReason()));
                                        }
                                        else //accepted UID
                                        {
                                            _failureDetector.HeartBeat();
                                            nextState =
                                                GoTo(AssociationState.Open)
                                                    .Using(
                                                        new AssociatedWaitHandler(
                                                            NotifyOutboundHandler(wrappedHandle, handshakeInfo,
                                                                statusCompletionSource), wrappedHandle,
                                                            new Queue<ByteString>()));
                                        }
                                    })
                                    .With<Disassociate>(d =>
                                    {
                                        //After receiving Disassociate we MUST NOT send back a Disassociate (loop)
                                        nextState = Stop(new Failure(d.Reason));
                                    })
                                    .Default(d =>
                                    {
                                        _log.Debug("Expected message of type Associate; instead received {0}", d);
                                        //Expect handshake to be finished, dropping connection
                                        SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                                        nextState = Stop();
                                    });
                            })
                            .With<InboundUnassociated>(iu =>
                            {
                                /*
                                 * This state is used by inbound protocol state actors
                                 * when they receive an association attempt from the
                                 * outbound side of the association.
                                 */
                                var associationHandler = iu.AssociationEventListener;
                                var wrappedHandle = iu.WrappedHandle;
                                pdu.Match()
                                    .With<Disassociate>(d =>
                                    {
                                        nextState = Stop(new Failure(d.Reason));
                                    })
                                    .With<Associate>(a =>
                                    {
                                        SendAssociate(wrappedHandle, _localHandshakeInfo);
                                        _failureDetector.HeartBeat();
                                        InitTimers();
                                        nextState =
                                            GoTo(AssociationState.Open)
                                                .Using(
                                                    new AssociatedWaitHandler(
                                                        NotifyInboundHandler(wrappedHandle, a.Info, associationHandler),
                                                        wrappedHandle, new Queue<ByteString>()));
                                    })
                                    .Default(d =>
                                    {
                                        SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                                        nextState = Stop();
                                    });
                            });

                    })
                    .With<HeartbeatTimer>(h => @event.StateData.Match()
                        .With<OutboundUnderlyingAssociated>(ou => nextState = HandleTimers(ou.WrappedHandle)));

                return nextState;
            });

            When(AssociationState.Open, @event =>
            {
                State<AssociationState, ProtocolStateData> nextState = null;
                @event.FsmEvent.Match()
                    .With<UnderlyingTransportError>(e =>
                    {
                        PublishError(e);
                        nextState = Stay();
                    })
                    .With<Disassociated>(d =>
                    {
                        nextState = Stop(new Failure(d.Info));
                    })
                    .With<InboundPayload>(ip =>
                    {
                        var pdu = DecodePdu(ip.Payload);
                        pdu.Match()
                            .With<Disassociate>(d =>
                            {
                                nextState = Stop(new Failure(d.Reason));
                            })
                            .With<Heartbeat>(h =>
                            {
                                _failureDetector.HeartBeat();
                                nextState = Stay();
                            })
                            .With<Payload>(p =>
                            {
                                _failureDetector.HeartBeat();
                                @event.StateData.Match()
                                    .With<AssociatedWaitHandler>(awh =>
                                    {
                                        var nQueue = new Queue<ByteString>(awh.Queue);
                                        nQueue.Enqueue(p.Bytes);
                                        nextState =
                                            Stay()
                                                .Using(new AssociatedWaitHandler(awh.HandlerListener, awh.WrappedHandle,
                                                    nQueue));
                                    })
                                    .With<ListenerReady>(lr =>
                                    {
                                        lr.Listener.Notify(new InboundPayload(p.Bytes));
                                        nextState = Stay();
                                    })
                                    .Default(msg =>
                                    {
                                        throw new AkkaProtocolException($"Unhandled message in state Open(InboundPayload) with type {msg.GetType()}");
                                    });
                            })
                            .Default(d =>
                            {
                                nextState = Stay();
                            });
                    })
                    .With<HeartbeatTimer>(hrt => @event.StateData.Match()
                        .With<AssociatedWaitHandler>(awh => nextState = HandleTimers(awh.WrappedHandle))
                        .With<ListenerReady>(lr => nextState = HandleTimers(lr.WrappedHandle)))
                    .With<DisassociateUnderlying>(du =>
                    {
                        AssociationHandle handle = null;
                        @event.StateData.Match()
                            .With<ListenerReady>(lr => handle = lr.WrappedHandle)
                            .With<AssociatedWaitHandler>(awh => handle = awh.WrappedHandle)
                            .Default(msg =>
                            {
                                throw new AkkaProtocolException($"unhandled message in state Open(DisassociateUnderlying) with type {msg.GetType()}");
                            });
                        SendDisassociate(handle, du.Info);
                        nextState = Stop();
                    })
                    .With<HandleListenerRegistered>(hlr => @event.StateData.Match()
                        .With<AssociatedWaitHandler>(awh =>
                        {
                            foreach (var msg in awh.Queue)
                                hlr.Listener.Notify(new InboundPayload(msg));
                            nextState = Stay().Using(new ListenerReady(hlr.Listener, awh.WrappedHandle));
                        }));

                return nextState;
            });

            OnTermination(@event => @event.StateData.Match()
                .With<OutboundUnassociated>(ou => ou.StatusCompletionSource.TrySetException(@event.Reason is Failure
                    ? new AkkaProtocolException(@event.Reason.ToString())
                    : new AkkaProtocolException("Transport disassociated before handshake finished")))
                .With<OutboundUnderlyingAssociated>(oua =>
                {
                    Exception associationFailure = null;
                    @event.Reason.Match()
                        .With<Failure>(f => f.Cause.Match()
                            .With<TimeoutReason>(
                                timeout =>
                                    associationFailure =
                                        new AkkaProtocolException(timeout.ErrorMessage))
                            .With<ForbiddenUidReason>(
                                forbidden =>
                                    associationFailure =
                                        new AkkaProtocolException(
                                            "The remote system has a UID that has been quarantined. Association aborted."))
                            .With<DisassociateInfo>(info => associationFailure = DisassociateException(info)))
                        .Default(
                                msg =>
                                    associationFailure =
                                        new AkkaProtocolException(
                                            "Transport disassociated before handshake finished"));

                    oua.StatusCompletionSource.TrySetException(associationFailure);
                    oua.WrappedHandle.Disassociate();
                })
                .With<AssociatedWaitHandler>(awh =>
                {
                    Disassociated disassociateNotification = null;
                    if (@event.Reason is Failure && @event.Reason.AsInstanceOf<Failure>().Cause is DisassociateInfo)
                    {
                        disassociateNotification =
                            new Disassociated(@event.Reason.AsInstanceOf<Failure>().Cause.AsInstanceOf<DisassociateInfo>());
                    }
                    else
                    {
                        disassociateNotification = new Disassociated(DisassociateInfo.Unknown);
                    }
                    awh.HandlerListener.ContinueWith(result => result.Result.Notify(disassociateNotification),
                        TaskContinuationOptions.ExecuteSynchronously);
                })
                .With<ListenerReady>(lr =>
                {
                    Disassociated disassociateNotification = null;
                    if (@event.Reason is Failure && ((Failure)@event.Reason).Cause is DisassociateInfo)
                    {
                        disassociateNotification =
                            new Disassociated(((Failure)@event.Reason).Cause.AsInstanceOf<DisassociateInfo>());
                    }
                    else
                    {
                        disassociateNotification = new Disassociated(DisassociateInfo.Unknown);
                    }
                    lr.Listener.Notify(disassociateNotification);
                    lr.WrappedHandle.Disassociate();
                })
                .With<InboundUnassociated>(iu =>
                    iu.WrappedHandle.Disassociate()));

            /*
             * Set the initial ProtocolStateActor state to CLOSED if OUTBOUND
             * Set the initial ProtocolStateActor state to WAITHANDSHAKE if INBOUND
             * */
            _initialData.Match()
                .With<OutboundUnassociated>(d =>
                {
                    // attempt to open underlying transport to the remote address
                    // if using DotNetty, this is where the socket connection is opened.
                    d.Transport.Associate(d.RemoteAddress).ContinueWith(result => new HandleMsg(result.Result), TaskContinuationOptions.ExecuteSynchronously).PipeTo(Self);
                    StartWith(AssociationState.Closed, d);
                })
                .With<InboundUnassociated>(d =>
                {
                    // inbound transport is opened already inside the ProtocolStateManager
                    // therefore we just have to set ourselves as listener and wait for
                    // incoming handshake attempts from the client.
                    d.WrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));
                    StartWith(AssociationState.WaitHandshake, d);
                });

        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reason">TBD</param>
        protected override void LogTermination(Reason reason)
        {
            var failure = reason as Failure;
            if (failure != null)
            {
                failure.Cause.Match()
                    .With<DisassociateInfo>(() => { }) //no logging
                    .With<ForbiddenUidReason>(() => { }) //no logging
                    .With<TimeoutReason>(timeoutReason => _log.Error(timeoutReason.ErrorMessage));
            }
            else
                base.LogTermination(reason);
        }

#endregion

#region Actor methods

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            CancelTimer("heartbeat-timer");
            base.PostStop(); //pass to OnTermination
        }

#endregion

#region Internal protocol messaging methods

        private Exception DisassociateException(DisassociateInfo info)
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
                //send disassociate just to be sure
                SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                return Stop(new Failure(new TimeoutReason("No response from remote. Handshake timed out or transport failure detector triggered.")));
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

            statusPromise.SetResult(new AkkaProtocolHandle(_localAddress, wrappedHandle.RemoteAddress, readHandlerPromise, wrappedHandle, handshakeInfo, Self, _codec));

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

        private void InitTimers()
        {
            SetTimer("heartbeat-timer", new HeartbeatTimer(), _settings.TransportHeartBeatInterval, true);
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
        /// <see cref="Props"/> used when creating OUTBOUND associations to remote endpoints.
        ///
        /// These <see cref="Props"/> create outbound <see cref="ProtocolStateActor"/> instances,
        /// which begin a state of
        /// </summary>
        /// <param name="handshakeInfo">TBD</param>
        /// <param name="remoteAddress">TBD</param>
        /// <param name="statusCompletionSource">TBD</param>
        /// <param name="transport">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="codec">TBD</param>
        /// <param name="failureDetector">TBD</param>
        /// <param name="refuseUid">TBD</param>
        /// <returns>TBD</returns>
        public static Props OutboundProps(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource,
            Transport transport, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, int? refuseUid = null)
        {
            return Props.Create(() => new ProtocolStateActor(handshakeInfo, remoteAddress, statusCompletionSource, transport, settings, codec, failureDetector, refuseUid));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="handshakeInfo">TBD</param>
        /// <param name="wrappedHandle">TBD</param>
        /// <param name="associationEventListener">TBD</param>
        /// <param name="settings">TBD</param>
        /// <param name="codec">TBD</param>
        /// <param name="failureDetector">TBD</param>
        /// <returns>TBD</returns>
        public static Props InboundProps(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle,
            IAssociationEventListener associationEventListener, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector)
        {
            return Props.Create(() => new ProtocolStateActor(handshakeInfo, wrappedHandle, associationEventListener, settings, codec, failureDetector));
        }

#endregion
    }
}

