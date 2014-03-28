using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Tools;
using Google.ProtocolBuffers;

namespace Akka.Remote.Transport
{
    public class ProtocolTransportAddressPair
    {
        public ProtocolTransportAddressPair(AkkaProtocolTransport protocolTransport, Address address)
        {
            ProtocolTransport = protocolTransport;
            Address = address;
        }

        public AkkaProtocolTransport ProtocolTransport { get; private set; }

        public Address Address { get; private set; }
    }

    public class AkkaProtocolException : AkkaException
    {
        public AkkaProtocolException(string message, Exception cause = null) : base(message, cause) { }
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
    public class AkkaProtocolTransport : ActorTransportAdapter
    {
        public AkkaProtocolTransport(Transport wrappedTransport, ActorSystem system, AkkaProtocolSettings settings, AkkaPduCodec codec)
            : base(wrappedTransport, system)
        {
            Codec = codec;
            Settings = settings;
        }

        public AkkaProtocolSettings Settings { get; private set; }

        protected AkkaPduCodec Codec { get; private set; }

        private readonly SchemeAugmenter _schemeAugmenter = new SchemeAugmenter(RemoteSettings.AkkaScheme);

        protected override SchemeAugmenter SchemeAugmenter
        {
            get { return _schemeAugmenter; }
        }

        public override Task<bool> ManagementCommand(object message)
        {
            return WrappedTransport.ManagementCommand(message);
        }

        #region Static properties

        public static AtomicCounter UniqueId = new AtomicCounter(0);

        #endregion
    }

    public class AssociateUnderlyingRefuseUid : NoSerializationVerificationNeeded
    {
        public AssociateUnderlyingRefuseUid(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, int? refuseUid = null)
        {
            RefuseUid = refuseUid;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        public Address RemoteAddress { get; private set; }

        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        public int? RefuseUid { get; private set; }
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
            HandshakeInfo handshakeInfo, ActorRef stateActor, AkkaPduCodec codec)
            : base(originalLocalAddress, originalRemoteAddress, wrappedHandle, RemoteSettings.AkkaScheme)
        {
            _handshakeInfo = handshakeInfo;
            _stateActor = stateActor;
            _readHandlerSource = readHandlerCompletionSource;
        }

        private TaskCompletionSource<IHandleEventListener> _readHandlerSource;

        private HandshakeInfo _handshakeInfo;

        private ActorRef _stateActor;

        private AkkaPduCodec _codec;

        public override bool Write(ByteString payload)
        {
            return WrappedHandle.Write(_codec.ConstructPayload(payload));
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
        Open = 2
    }

    public class HeartbeatTimer : NoSerializationVerificationNeeded { }

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
        public OutboundUnassociated(Address remoteAddress, TaskCompletionSource<AssociationHandle> statusCompletionSource, Transport transport)
        {
            Transport = transport;
            StatusCompletionSource = statusCompletionSource;
            RemoteAddress = remoteAddress;
        }

        public Address RemoteAddress { get; private set; }

        public TaskCompletionSource<AssociationHandle> StatusCompletionSource { get; private set; }

        public Transport Transport { get; private set; }
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

    public class TimeoutReason { }
    public class ForbiddenUidReason { }

    public class ProtocolStateActor : FSM<AssociationState, ProtocolStateData>
    {
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
        public ProtocolStateActor(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle, IAssociationEventListener associationEventListener, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector)
            : this(new InboundUnassociated(associationEventListener, wrappedHandle), handshakeInfo, settings, codec, failureDetector, refuseUid: null) { }

        /// <summary>
        /// Common constructor used by both the outbound and the inboud cases
        /// </summary>
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
            _initialData.Match()
                .With<OutboundUnassociated>(d =>
                {
                    d.Transport.Associate(d.RemoteAddress).PipeTo(Self);
                    StartWith(AssociationState.Closed, d);
                })
                .With<InboundUnassociated>(d =>
                {
                    d.WrappedHandle.ReadHandlerSource.SetResult(new ActorHandleEventListener(Self));
                    StartWith(AssociationState.WaitHandshake, d);
                });

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
                    .With<AssociationHandle>(h => fsmEvent.StateData.Match()
                        .With<OutboundUnassociated>(ou =>
                        {
                            var wrappedHandle = h;
                            var statusPromise = ou.StatusCompletionSource;
                            wrappedHandle.ReadHandlerSource.TrySetResult(new ActorHandleEventListener(Self));
                            if (SendAssociate(wrappedHandle, _localHandshakeInfo))
                            {
                                _failureDetector.HeartBeat();
                                InitTimers();
                                nextState =
                                    GoTo(AssociationState.WaitHandshake)
                                        .Using(new OutboundUnderlyingAssociated(statusPromise, wrappedHandle));
                            }
                            else
                            {
                                SetTimer("associate-retry", wrappedHandle,
                                    Context.System.Provider.AsInstanceOf<RemoteActorRefProvider>()
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
                                        //Expect handshake to be finished, dropping connection
                                        SendDisassociate(wrappedHandle, DisassociateInfo.Unknown);
                                        nextState = Stop();
                                    });
                            })
                            .With<InboundUnassociated>(iu =>
                            {
                                var associationHandler = iu.AssociationEventListener;
                                var wrappedHandle = iu.WrappedHandle;
                                pdu.Match()
                                    .With<Disassociate>(d => nextState = Stop(new Failure(d.Reason)))
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

                    });

                return nextState;
            });
        }

        #endregion

        #region Internal protocol messaging methods

        private void ListenForListenerRegistration(TaskCompletionSource<IHandleEventListener> readHandlerSource)
        {
            readHandlerSource.Task.ContinueWith(rh => new HandleListenerRegistered(rh.Result),
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent).PipeTo(Self);
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
                throw new AkkaProtocolException(
                    string.Format("Error while decoding incoming Akka PDU of length", pdu.Length), ex);
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

        #endregion

        #region Static methods

        public static Props OutboundProps(HandshakeInfo handshakeInfo, Address remoteAddress,
            TaskCompletionSource<AssociationHandle> statusCompletionSource,
            Transport transport, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector, int? refuseUid = null)
        {
            return Props.Create(() => new ProtocolStateActor(handshakeInfo, remoteAddress, statusCompletionSource, transport, settings, codec, failureDetector, refuseUid));
        }

        public static Props InboundProps(HandshakeInfo handshakeInfo, AssociationHandle wrappedHandle,
            IAssociationEventListener associationEventListener, AkkaProtocolSettings settings, AkkaPduCodec codec, FailureDetector failureDetector)
        {
            return
                Props.Create(
                    () =>
                        new ProtocolStateActor(handshakeInfo, wrappedHandle, associationEventListener, settings, codec, failureDetector));
        }

        #endregion
    }
}