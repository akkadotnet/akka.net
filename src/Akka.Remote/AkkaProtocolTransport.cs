using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Transport;
using Google.ProtocolBuffers;

namespace Akka.Remote
{
    /// <summary>
    /// Implementation of the Akka protocol as a (logical) <see cref="Transport"/> that wraps an underlying (physical) <see cref="Transport"/> instance.
    /// 
    /// Features provided by this transport include:
    ///  - Soft-state associations via the use of heartbeats and failure detectors
    ///  - Transparent origin address handling
    /// 
    /// This transport is loaded automatically by <see cref="Remoting"/> and will wrap all dynamically loaded transports.
    /// </summary>
    public class AkkaProtocolTransport : ActorTransportAdapter
    {
        public AkkaProtocolTransport(Transport.Transport wrappedTransport, ActorSystem system, AkkaProtocolSettings settings) : base(wrappedTransport, system)
        {
            Settings = settings;
        }

        public AkkaProtocolSettings Settings { get; private set; }

        private readonly SchemeAugmenter _schemeAugmenter = new SchemeAugmenter(RemoteSettings.AkkaScheme);
        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _schemeAugmenter; }
        }

        public override Task<bool> ManagementCommand(object message)
        {
            return WrappedTransport.ManagementCommand(message);
        }
    }

    public sealed class HandshakeInfo
    {
        public HandshakeInfo(Address origin, long uid)
        {
            Origin = origin;
            Uid = uid;
        }

        public Address Origin { get; private set; }

        public long Uid { get; private set; }
    }

    public class AkkaProtocolHandle : AbstractTransportAdapterHandle
    {
        public AkkaProtocolHandle(Address originalLocalAddress, Address originalRemoteAddress, 
            TaskCompletionSource<IHandleEventListener> readHandlerCompletionSource, AssociationHandle wrappedHandle,
            HandshakeInfo handshakeInfo, ActorRef stateActor) 
            : base(originalLocalAddress, originalRemoteAddress, wrappedHandle, RemoteSettings.AkkaScheme)
        {
            _handshakeInfo = handshakeInfo;
            _stateActor = stateActor;
            _readHandlerSource = readHandlerCompletionSource;
        }

        private TaskCompletionSource<IHandleEventListener> _readHandlerSource;

        private HandshakeInfo _handshakeInfo;

        private ActorRef _stateActor;

        public override bool Write(ByteString payload)
        {
            return WrappedHandle.Write(payload);
        }

        public override void Disassociate()
        {
            Disassociate(DisassociateInfo.Unknown);
        }

        public void Disassociate(DisassociateInfo info)
        {
            _stateActor.Tell(new DisassociateUnderlying(info));
        }
    }

    public enum AssociationState
    {
        Closed = 0,
        WaitHandshake = 1,
        Open = 2,
        HeartbeatTimer = 3
    }

    public sealed class HandleMsg : NoSerializationVerificationNeeded
    {
        public HandleMsg(AssociationHandle handle)
        {
            Handle = handle;
        }

        public AssociationHandle Handle { get; private set; }
    }

    public sealed class HandleListenerRegistered : NoSerializationVerificationNeeded
    {
        public HandleListenerRegistered(IHandleEventListener listener)
        {
            Listener = listener;
        }

        public IHandleEventListener Listener { get; private set; }
    }

    public abstract class ProtocolStateData { }
    public abstract class InitialProtocolStateData : ProtocolStateData { }

    /// <summary>
    /// Neither the underlying nor the provided transport is associated
    /// </summary>
    public sealed class OutboundUnassociated : InitialProtocolStateData
    {
        public OutboundUnassociated(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport.Transport transport)
        {
            Transport = transport;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        public Address RemoteAddress { get; private set; }

        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        public Transport.Transport Transport { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handshake of the Akka protocol is not yet finished
    /// </summary>
    public sealed class OutboundUnderlyingAssociated : ProtocolStateData
    {
        public OutboundUnderlyingAssociated(TaskCompletionSource<AssociationHandle> statusCompletionSource, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            StatusCompletionSource = statusCompletionSource;
        }

        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handshake of the akka protocol is not yet finished
    /// </summary>
    public sealed class InboundUnassociated : InitialProtocolStateData
    {
        public InboundUnassociated(IAssociationEventListener associationEventListener, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            AssociationEventListener = associationEventListener;
        }

        public IAssociationEventListener AssociationEventListener { get; private set; }

        public AssociationHandle WrappedHandle { get; private set; }
    }

    /// <summary>
    /// The underlying transport is associated, but the handler for the handle has not been provided yet
    /// </summary>
    public sealed class AssociatedWaitHandler : ProtocolStateData
    {
        public AssociatedWaitHandler(Task<IHandleEventListener> handlerListener, AssociationHandle wrappedHandle, Queue<ByteString> queue)
        {
            Queue = queue;
            WrappedHandle = wrappedHandle;
            HandlerListener = handlerListener;
        }

        public Task<IHandleEventListener> HandlerListener { get; private set; }

        public AssociationHandle WrappedHandle { get; private set; }

        public Queue<ByteString> Queue { get; private set; }
    }

    /// <summary>
    /// System ready!
    /// </summary>
    public sealed class ListenerReady : ProtocolStateData
    {
        public ListenerReady(IHandleEventListener listener, AssociationHandle wrappedHandle)
        {
            WrappedHandle = wrappedHandle;
            Listener = listener;
        }

        public IHandleEventListener Listener { get; private set; }

        public AssociationHandle WrappedHandle { get; private set; }
    }

    public class ProtocolStateActor : ActorBase
    {
        private InitialProtocolStateData _initialData;
        private HandshakeInfo _localHandshakeInfo;
        private int? _refuseUid;
        private AkkaProtocolSettings _settings;
        private Address _localAddress;

        /// <summary>
        /// Constructor for outbound ProtocolStateActors
        /// </summary>
        public ProtocolStateActor(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport.Transport transport,
            AkkaProtocolSettings settings, int? refuseUid = null)
            : this(
                new OutboundUnassociated(remoteAddress, statusCompletionSource, transport), handshakeInfo, settings,
                refuseUid)
        {
            
        }

        /// <summary>
        /// Constructor for inbound ProtocolStateActors
        /// </summary>
        public ProtocolStateActor(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle, IAssociationEventListener associationEventListener, AkkaProtocolSettings settings)
            : this(new InboundUnassociated(associationEventListener, wrappedHandle), handshakeInfo, settings, refuseUid:null) { }

        /// <summary>
        /// Common constructor used by both the outbound and the inboud cases
        /// </summary>
        protected ProtocolStateActor(InitialProtocolStateData initialData, HandshakeInfo localHandshakeInfo, AkkaProtocolSettings settings, int? refuseUid)
        {
            _initialData = initialData;
            _localHandshakeInfo = localHandshakeInfo;
            _settings = settings;
            _refuseUid = refuseUid;
            _localAddress = _localHandshakeInfo.Origin;
        }

        protected override void OnReceive(object message)
        {
            throw new System.NotImplementedException();
        }

        #region Static methods

        public static Props OutboundProps(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource,
            Transport.Transport transport, AkkaProtocolSettings settings, int? refuseUid = null)
        {
            return Props.Create(() => new ProtocolStateActor(handshakeInfo, remoteAddress, statusCompletionSource, transport, settings, refuseUid));
        }

        #endregion
    }
}