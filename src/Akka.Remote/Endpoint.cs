using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Remote.Transport;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    // ReSharper disable once InconsistentNaming
    internal interface InboundMessageDispatcher
    {
        void Dispatch(InternalActorRef recipient, Address recipientAddress, SerializedMessage message,
            ActorRef senderOption = null);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class DefaultMessageDispatcher : InboundMessageDispatcher
    {
        private ActorSystem system;
        private RemoteActorRefProvider provider;
        private LoggingAdapter log;
        private RemoteDaemon remoteDaemon;
        private RemoteSettings settings;

        public DefaultMessageDispatcher(ActorSystem system, RemoteActorRefProvider provider, LoggingAdapter log)
        {
            this.system = system;
            this.provider = provider;
            this.log = log;
            remoteDaemon = provider.RemoteDaemon;
            settings = provider.RemoteSettings;
        }

        public void Dispatch(InternalActorRef recipient, Address recipientAddress, SerializedMessage message,
            ActorRef senderOption = null)
        {
            var payload = MessageSerializer.Deserialize(system, message);
            Type payloadClass = payload == null ? null : payload.GetType();
            var sender = senderOption ?? system.DeadLetters;
            var originalReceiver = recipient.Path;

            var msgLog = string.Format("RemoteMessage: {0} to {1}<+{2} from {3}", payload, recipient, originalReceiver,
                sender);

            if (recipient == remoteDaemon)
            {
                if (settings.UntrustedMode) log.Debug("dropping daemon message in untrusted mode");
                else
                {
                    if (settings.LogReceive) log.Debug("received daemon message [{0}]", msgLog);
                    remoteDaemon.Tell(payload);
                }
            }
            else if (recipient is LocalRef && recipient.IsLocal) //TODO: update this to include support for RepointableActorRefs if they get implemented
            {
                var l = recipient.AsInstanceOf<LocalActorRef>();
                if (settings.LogReceive) log.Debug("received local message [{0}]", msgLog);
                payload.Match()
                    .With<ActorSelectionMessage>(sel =>
                    {
                        var actorPath = "/" + string.Join("/", sel.Elements.Select(x => x.ToString()));
                        if (settings.UntrustedMode
                            && !settings.TrustedSelectionPaths.Contains(actorPath)
                            || sel.Message is PossiblyHarmful
                            || l != provider.Guardian)
                        {
                            log.Debug(
                                "operating in UntrustedMode, dropping inbound actor selection to [{0}], allow it" +
                                "by adding the path to 'akka.remote.trusted-selection-paths' in configuration",
                                actorPath);
                        }
                        else
                        {
                            //run the receive logic for ActorSelectionMessage here to make sure it is not stuck on busy user actor
                            ActorSelection.DeliverSelection(l, sender, sel);
                        }
                    })
                    .With<PossiblyHarmful>(msg =>
                    {
                        if (settings.UntrustedMode)
                        {
                            log.Debug("operating in UntrustedMode, dropping inbound PossiblyHarmful message of type {0}", msg.GetType());
                        }
                    })
                    .With<SystemMessage>(msg => { l.Tell(msg); })
                    .Default(msg => { l.Tell(msg, sender); });
            }
            else if (recipient is RemoteRef && !recipient.IsLocal && !settings.UntrustedMode)
            {
                if (settings.LogReceive) log.Debug("received remote-destined message {0}", msgLog);
                if (provider.Transport.Addresses.Contains(recipientAddress))
                {
                    //if it was originally addressed to us but is in fact remote from our point of view (i.e. remote-deployed)
                    recipient.Tell(payload, sender);
                }
                else
                {
                    log.Error(
                        "Dropping message [{0}] for non-local recipient [{1}] arriving at [{2}] inbound addresses [{3}]",
                        payloadClass, recipient, string.Join(",", provider.Transport.Addresses));
                }
            }
            else
            {
                log.Error(
                        "Dropping message [{0}] for non-local recipient [{1}] arriving at [{2}] inbound addresses [{3}]",
                        payloadClass, recipient, string.Join(",", provider.Transport.Addresses));
            }
        }
    }

    #region Endpoint Exception Types

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class EndpointException : AkkaException
    {
        public EndpointException(string msg, Exception cause = null) : base(msg, cause) { }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal interface IAssociationProblem { }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class ShutDownAssociation : EndpointException, IAssociationProblem
    {
        public ShutDownAssociation(Address localAddress, Address remoteAddress, Exception cause = null)
            : base(string.Format("Shut down address: {0}", remoteAddress), cause)
        {
            RemoteAddress = remoteAddress;
            LocalAddress = localAddress;
        }

        public Address LocalAddress { get; private set; }

        public Address RemoteAddress { get; private set; }
    }

    internal sealed class InvalidAssociation : EndpointException, IAssociationProblem
    {
        public InvalidAssociation(Address localAddress, Address remoteAddress, Exception cause = null)
            : base(string.Format("Invalid address: {0}", remoteAddress), cause)
        {
            RemoteAddress = remoteAddress;
            LocalAddress = localAddress;
        }

        public Address LocalAddress { get; private set; }

        public Address RemoteAddress { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class HopelessAssociation : EndpointException, IAssociationProblem
    {
        public HopelessAssociation(Address localAddress, Address remoteAddress, int? uid = null, Exception cause = null)
            : base("Catastrophic association error.", cause)
        {
            RemoteAddress = remoteAddress;
            LocalAddress = localAddress;
            Uid = uid;
        }

        public Address LocalAddress { get; private set; }

        public Address RemoteAddress { get; private set; }

        public int? Uid { get; private set; }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class EndpointDisassociatedException : EndpointException
    {
        public EndpointDisassociatedException(string msg) : base(msg)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class EndpointAssociatedException : EndpointException
    {
        public EndpointAssociatedException(string msg) : base(msg)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OversizedPayloadException : EndpointException
    {
        public OversizedPayloadException(string msg) : base(msg)
        {
        }
    }

    #endregion

    /// <summary>
    /// INTERNAL API
    /// 
    /// <remarks>
    /// [Aaronontheweb] so this class is responsible for maintaining a buffer of retriable messages in
    /// Akka and it expects an ACK / NACK response pattern before it considers a message to be sent or received.
    /// 
    /// Currently AkkaDotNet does not have any form of guaranteed message delivery in the stack, since that was
    /// considered outside the scope of V1. However, this class needs to be revisited and updated to support it,
    /// along with others.
    /// 
    /// For the time being, the class remains just a proxy for spawning <see cref="EndpointWriter"/> actors and
    /// forming any outbound associations.
    /// </remarks>
    /// </summary>
    internal class ReliableDeliverySupervisor : ActorBase, IActorLogging
    {
        private LoggingAdapter _log = Logging.GetLogger(Context);
        public LoggingAdapter Log { get { return _log; } }

        private AkkaProtocolHandle handleOrActive;
        private Address localAddress;
        private Address remoteAddress;
        private int? refuseUid;
        private AkkaProtocolTransport transport;
        private RemoteSettings settings;
        private AkkaPduCodec codec;
        private AkkaProtocolHandle currentHandle;
        private ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers;
        private EndpointRegistry _endpoints = new EndpointRegistry();

        public ReliableDeliverySupervisor(AkkaProtocolHandle handleOrActive, Address localAddress, Address remoteAddress, 
            int? refuseUid, AkkaProtocolTransport transport, RemoteSettings settings, AkkaPduCodec codec, ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers)
        {
            this.handleOrActive = handleOrActive;
            this.localAddress = localAddress;
            this.remoteAddress = remoteAddress;
            this.refuseUid = refuseUid;
            this.transport = transport;
            this.settings = settings;
            this.codec = codec;
            currentHandle = handleOrActive;
            this.receiveBuffers = receiveBuffers;
            Reset();
            _writer = CreateWriter();
            Uid = handleOrActive != null ? (int?) handleOrActive.HandshakeInfo.Uid : null;
            UidConfirmed = Uid.HasValue;
        }

        public int? Uid { get; set; }

        /// <summary>
        /// Processing of <see cref="Ack"/>s has to be delayed until the UID is discovered after a reconnect. Dependig whether the
        /// UID matches the expected one, pending Acks can be processed or must be dropped. It is guaranteed that for any inbound
        /// connectiosn (calling <see cref="CreateWriter"/>) the first message from that connection is <see cref="GotUid"/>, therefore it serves
        /// a separator.
        /// 
        /// If we already have an inbound handle then UID is initially confirmed.
        /// (This actor is never restarted.)
        /// </summary>
        public bool UidConfirmed { get; set; }

        public Deadline BailoutAt
        {
            get { return Deadline.Now + settings.InitialSysMsgDeliveryTimeout; }
        }

        private CancellationTokenSource _autoResendTimer = null;
        private AckedSendBuffer<EndpointManager.Send> _resendBuffer = null;
        private SeqNo _lastCumulativeAck = null;
        private long _seqCounter;
        private List<Ack> _pendingAcks = null;

        private ActorRef _writer;

        private void Reset()
        {
            _resendBuffer = new AckedSendBuffer<EndpointManager.Send>(settings.SysMsgBufferSize);
            ScheduleAutoResend();
            _lastCumulativeAck = new SeqNo(-1);
            _seqCounter = 0L;
            _pendingAcks = new List<Ack>();
        }

        private void ScheduleAutoResend()
        {
            if (_autoResendTimer == null)
            {
                _autoResendTimer = new CancellationTokenSource();
                Context.System.Scheduler.ScheduleOnce(settings.SysResendTimeout, Self, new AttemptSysMsgRedelivery(),
                    _autoResendTimer.Token);
            }
        }

        private void RescheduleAutoResend()
        {
            _autoResendTimer.Cancel();
            _autoResendTimer = null;
            ScheduleAutoResend();
        }

        private SeqNo NextSeq()
        {
            var tmp = _seqCounter;
            _seqCounter++;
            return new SeqNo(tmp);
        }

        private void UnstashAcks()
        {
            _pendingAcks.ForEach(ack => Self.Tell(ack));
            _pendingAcks = new List<Ack>();
        }

        #region ActorBase methods and Behaviors

        protected override void PostStop()
        {
            foreach (var msg in _resendBuffer.Nacked.Concat(_resendBuffer.NonAcked))
            {
                Context.System.DeadLetters.Tell(msg.Copy(opt:null));
            }
            EndpointManager.ResendState value;
            receiveBuffers.TryRemove(new EndpointManager.Link(localAddress, remoteAddress), out value);
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalActorStateException("BUG: ReliableDeliverySupervisor has been attempted to be restarted. This must not happen.");
        }

        protected override void OnReceive(object message)
        {
            message.Match()
                .With<EndpointWriter.FlushAndStop>(flush =>
                {
                    //Trying to serve untilour last breath
                    ResendAll();
                    _writer.Tell(new EndpointWriter.FlushAndStop());
                    Context.Become(FlushWait);
                })
                .With<EndpointManager.Send>(HandleSend)
                .With<Ack>(ack =>
                {
                    if (!UidConfirmed) _pendingAcks.Add(ack);
                    else
                    {
                        try
                        {
                            _resendBuffer = _resendBuffer.Acknowledge(ack);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidAssociationException(
                                string.Format(
                                    "Error encounted while processing system message acknowledgement {0} {1}",
                                    _resendBuffer, ack), ex);
                        }

                        if (_lastCumulativeAck < ack.CumulativeAck)
                        {
                            _lastCumulativeAck = ack.CumulativeAck;
                            // Cumulative ack is progressing, we might not need to resend non-acked messages yet.
                            // If this progression stops, the timer will eventually kick in, since scheduleAutoResend
                            // does not cancel existing timers (see the "else" case).
                            RescheduleAutoResend();
                        }
                        else
                        {
                            ScheduleAutoResend();
                        }

                        ResendNacked();
                    }
                })
                .With<AttemptSysMsgRedelivery>(sysmsg =>
                {
                    if (UidConfirmed) ResendAll();
                })
                .With<Terminated>(terminated =>
                {
                    currentHandle = null;
                    Context.Parent.Tell(new EndpointWriter.StoppedReading(Self));
                    if (_resendBuffer.NonAcked.Count > 0 || _resendBuffer.Nacked.Count > 0)
                        Context.System.Scheduler.ScheduleOnce(settings.SysResendTimeout, Self,
                            new AttemptSysMsgRedelivery());
                    Context.Become(Idle);
                })
                .With<GotUid>(g =>
                {
                    Context.Parent.Tell(g);
                    //New system that has the same address as the old - need to start from fresh state
                    UidConfirmed = true;
                    if(Uid.HasValue && Uid.Value != g.Uid) Reset();
                    else UnstashAcks();
                    Uid = refuseUid;
                })
                .With<EndpointWriter.StopReading>(stopped =>
                {
                    _writer.Tell(stopped, Sender); //forward the request
                });
        }

        protected void Gated(object message)
        {
            message.Match()
                .With<Terminated>(
                    terminated => Context.System.Scheduler.ScheduleOnce(settings.RetryGateClosedFor, Self, new Ungate()))
                .With<Ungate>(ungate =>
                {
                    if (_resendBuffer.NonAcked.Count > 0 || _resendBuffer.Nacked.Count > 0)
                    {
                        // If we talk to a system we have not talked to before (or has given up talking to in the past) stop
                        // system delivery attempts after the specified time. This act will drop the pending system messages and gate the
                        // remote address at the EndpointManager level stopping this actor. In case the remote system becomes reachable
                        // again it will be immediately quarantined due to out-of-sync system message buffer and becomes quarantined.
                        // In other words, this action is safe.
                        if (!UidConfirmed && BailoutAt.IsOverdue)
                        {
                            throw new InvalidAssociation(localAddress, remoteAddress,
                                new TimeoutException("Delivery of system messages timed out and theywere dropped"));
                        }

                        _writer = CreateWriter();
                        //Resending will be triggered by the incoming GotUid message after the connection finished
                        Context.Become(OnReceive);
                    }
                    else
                    {
                        Context.Become(Idle);
                    }
                })
                .With<EndpointManager.Send>(send =>
                {
                    if (send.Message is SystemMessage)
                    {
                        TryBuffer(send.Copy(NextSeq()));
                    }
                    else
                    {
                        Context.System.DeadLetters.Tell(send);
                    }
                })
                .With<EndpointWriter.FlushAndStop>(flush => Context.Stop(Self))
                .With<EndpointWriter.StopReading>(w => Sender.Tell(new EndpointWriter.StoppedReading(w.Writer)));
        }

        protected void Idle(object message)
        {
            message.Match()
                .With<EndpointManager.Send>(send =>
                {
                    _writer = CreateWriter();
                    //Resending will be triggered by the incoming GotUid message after the connection finished
                    HandleSend(send);
                    Context.Become(OnReceive);
                })
                .With<AttemptSysMsgRedelivery>(sys =>
                {
                    _writer = CreateWriter();
                    //Resending will be triggered by the incoming GotUid message after the connection finished
                    Context.Become(OnReceive);
                })
                .With<EndpointWriter.FlushAndStop>(stop => Context.Stop(Self))
                .With<EndpointWriter.StopReading>(stop => Sender.Tell(new EndpointWriter.StoppedReading(stop.Writer)));
        }

       

        #endregion

        #region Static methods and Internal Message Types

        public class AttemptSysMsgRedelivery { }

        public class Ungate { }

        public sealed class GotUid
        {
            public GotUid(int uid)
            {
                Uid = uid;
            }

            public int Uid { get; private set; }
        }

        public static Props ReliableDeliverySupervisorProps(AkkaProtocolHandle handleOrActive, Address localAddress, Address remoteAddress,
            int? refuseUid, AkkaProtocolTransport transport, RemoteSettings settings, AkkaPduCodec codec, ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers)
        {
            return
                Props.Create(
                    () =>
                        new ReliableDeliverySupervisor(handleOrActive, localAddress, remoteAddress, refuseUid, transport,
                            settings, codec, receiveBuffers));
        }

        #endregion

        protected void FlushWait(object message)
        {
            message.Match()
                .With<Terminated>(terminated =>
                {
                    //Clear buffer to prevent sending system messages to dead letters -- at this point we are shutting down and
                    //don't know if they were properly delivered or not
                    _resendBuffer = new AckedSendBuffer<EndpointManager.Send>(0);
                    Context.Stop(Self);
                });
        }

        private void HandleSend(EndpointManager.Send send)
        {
            if (send.Message is SystemMessage)
            {
                var sequencedSend = send.Copy(NextSeq());
                TryBuffer(sequencedSend);
                //If we have not confirmed the remote UID we cannot transfer the system message at this point, so just buffer it.
                // GotUid will kick ResendAll causing the messages to be properly written.
                if (UidConfirmed) _writer.Tell(sequencedSend);
            }
            else
            {
                _writer.Tell(send);
            }
        }

        private void ResendNacked()
        {
            _resendBuffer.Nacked.ForEach(nacked => _writer.Tell(nacked));
        }

        private void ResendAll()
        {
            ResendNacked();
            _resendBuffer.NonAcked.ForEach(nonacked => _writer.Tell(nonacked));
            RescheduleAutoResend();
        }

        private void TryBuffer(EndpointManager.Send s)
        {
            try
            {
                _resendBuffer = _resendBuffer.Buffer(s);
            }
            catch (Exception ex)
            {
                throw new HopelessAssociation(localAddress, remoteAddress, Uid, ex);
            }
        }

        #region Writer create

        private ActorRef CreateWriter()
        {
            var writer =
                Context.ActorOf(
                    EndpointWriter.EndpointWriterProps(currentHandle, localAddress, remoteAddress, refuseUid, transport,
                        settings, new AkkaPduProtobuffCodec(), receiveBuffers, Self).WithDeploy(Deploy.Local),
                    "endpointWriter");
            Context.Watch(writer);
            return writer;
        }

        #endregion

    }

    /// <summary>
    /// Abstract base class for <see cref="EndpointReader"/> classes
    /// </summary>
    internal abstract class EndpointActor : UntypedActor, IActorLogging
    {
        protected readonly Address LocalAddress;
        protected Address RemoteAddress;
        protected RemoteSettings Settings;
        protected Transport.Transport Transport;

        private readonly LoggingAdapter _log = Logging.GetLogger(Context);
        public LoggingAdapter Log { get { return _log; } }

        private readonly EventPublisher _eventPublisher;
        protected bool Inbound { get; set; }

        protected EndpointActor(Address localAddress, Address remoteAddress, Transport.Transport transport,
            RemoteSettings settings)
        {
            _eventPublisher = new EventPublisher(Context.System, Log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
            this.LocalAddress = localAddress;
            this.RemoteAddress = remoteAddress;
            this.Transport = transport;
            this.Settings = settings;
        }

        #region Event publishing methods

        protected void PublishError(Exception ex, LogLevel level)
        {
            TryPublish(new AssociationErrorEvent(ex, LocalAddress, RemoteAddress, Inbound, level));
        }

        protected void PublishDisassociated()
        {
            TryPublish(new DisassociatedEvent(LocalAddress, RemoteAddress, Inbound));
        }

        private void TryPublish(RemotingLifecycleEvent ev)
        {
            try
            {
                _eventPublisher.NotifyListeners(ev);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Unable to publish error event to EventStream");
            }
        }

        #endregion

    }

    /// <summary>
    /// INTERNAL API.
    /// 
    /// Abstract base class for Endpoint writers that require a <see cref="FSM{TS,TD}"/> implementation.
    /// </summary>
    internal abstract class EndpointActor<TS,TD> : FSM<TS,TD>
    {
        protected readonly Address LocalAddress;
        protected Address RemoteAddress;
        protected RemoteSettings Settings;
        protected Transport.Transport Transport;

        private readonly EventPublisher _eventPublisher;

        protected bool Inbound { get; set; }

        protected EndpointActor(Address localAddress, Address remoteAddress, Transport.Transport transport,
            RemoteSettings settings)
        {
            _eventPublisher = new EventPublisher(Context.System, Log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
            LocalAddress = localAddress;
            RemoteAddress = remoteAddress;
            Transport = transport;
            Settings = settings;
        }

        #region Event publishing methods

        protected void PublishError(Exception ex, LogLevel level)
        {
            TryPublish(new AssociationErrorEvent(ex, LocalAddress, RemoteAddress, Inbound, level));
        }

        protected void PublishDisassociated()
        {
            TryPublish(new DisassociatedEvent(LocalAddress, RemoteAddress, Inbound));
        }

        private void TryPublish(RemotingLifecycleEvent ev)
        {
            try
            {
                _eventPublisher.NotifyListeners(ev);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Unable to publish error event to EventStream");
            }
        }

        #endregion

    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class EndpointWriter : EndpointActor<EndpointWriter.State, bool>
    {
        public EndpointWriter(AkkaProtocolHandle handleOrActive, Address localAddress, Address remoteAddress, 
            int? refuseUid, AkkaProtocolTransport transport, RemoteSettings settings, 
            AkkaPduCodec codec,  ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers, ActorRef reliableDeliverySupervisor = null) : 
            base(localAddress, remoteAddress, transport, settings)
        {
            this.handleOrActive = handleOrActive;
            this.refuseUid = refuseUid;
            this.codec = codec;
            this.reliableDeliverySupervisor = reliableDeliverySupervisor;
            system = Context.System;
            provider = (RemoteActorRefProvider) Context.System.Provider;
            msgDispatcher = new DefaultMessageDispatcher(system, provider, Log);
            this.receiveBuffers = receiveBuffers;
            Inbound = handleOrActive != null;
        }

        private AkkaProtocolHandle handleOrActive;
        private int? refuseUid;
        private AkkaPduCodec codec;
        private ActorRef reliableDeliverySupervisor;
        private ActorSystem system;
        private RemoteActorRefProvider provider;
        private ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers;
        private DisassociateInfo stopReason = DisassociateInfo.Unknown;

        private ActorRef reader;
        private InboundMessageDispatcher msgDispatcher;

        #region FSM definitions
        #endregion

        #region ActorBase methods

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                //we're going to throw an exception anyway
                PublishAndThrow(ex, LogLevel.ErrorLevel);
                return Directive.Escalate;
            });
        }

        protected override void PostRestart(Exception reason)
        {
            handleOrActive = null; //Wipe out the possibly injected handle
            Inbound = false;
            PreStart();
        }

        protected override void PreStart()
        {
            base.PreStart();
        }

        #endregion

        #region Internal methods

        private void PublishAndThrow(Exception reason, LogLevel level)
        {
            reason.Match().With<EndpointDisassociatedException>(endpoint => PublishDisassociated())
                .Default(msg => PublishError(reason, level));

            throw reason;
        }

        private State<EndpointWriter.State, bool> LogAndStay(Exception reason)
        {
            Log.Error(reason, "Transient association error (association remains live)");
            return Stay();
        }

        #endregion

        #region Static methods and Internal messages

        public static Props EndpointWriterProps(AkkaProtocolHandle handleOrActive, Address localAddress,
            Address remoteAddress, int? refuseUid, AkkaProtocolTransport transport, RemoteSettings settings,
            AkkaPduCodec codec, ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers, ActorRef reliableDeliverySupervisor = null)
        {
            return Props.Create(
                () =>
                    new EndpointWriter(handleOrActive, localAddress, remoteAddress, refuseUid, transport, settings,
                        codec, receiveBuffers, reliableDeliverySupervisor));
        }

        /// <summary>
        /// This message signals that the current association maintained by the local <see cref="EndpointWriter"/> and
        /// <see cref="EndpointReader"/> is to be overridden by a new inbound association. This is needed to avoid parallel inbound
        /// associations from the same remote endpoint: when a parallel inbound association is detected, the old one is removed and the new
        /// one is used instead.
        /// </summary>
        public sealed class TakeOver : NoSerializationVerificationNeeded
        {
            /// <summary>
            /// Create a new TakeOver command
            /// </summary>
            /// <param name="protocolHandle">The handle of the new association</param>
            public TakeOver(AkkaProtocolHandle protocolHandle)
            {
                ProtocolHandle = protocolHandle;
            }

            public AkkaProtocolHandle ProtocolHandle { get; private set; }
        }

        public sealed class TookOver : NoSerializationVerificationNeeded
        {
            public TookOver(ActorRef writer, AkkaProtocolHandle protoclHandle)
            {
                ProtoclHandle = protoclHandle;
                Writer = writer;
            }

            public ActorRef Writer { get; private set; }

            public AkkaProtocolHandle ProtoclHandle { get; private set; }
        }

        public sealed class BackoffTimer { }

        public sealed class FlushAndStop { }

        public sealed class AckIdleCheckTimer { }

        public sealed class Handle : NoSerializationVerificationNeeded
        {
            public Handle(AkkaProtocolHandle protocolHandle)
            {
                ProtocolHandle = protocolHandle;
            }

            public AkkaProtocolHandle ProtocolHandle { get; private set; }
        }

        public sealed class StopReading
        {
            public StopReading(ActorRef writer)
            {
                Writer = writer;
            }

            public ActorRef Writer { get; private set; }
        }

        public sealed class StoppedReading
        {
            public StoppedReading(ActorRef writer)
            {
                Writer = writer;
            }

            public ActorRef Writer { get; private set; }
        }

        /// <summary>
        /// Internal state descriptors used to power this FSM
        /// </summary>
        public enum State
        {
            Initializing = 0,
            Buffering = 1,
            Writing = 2,
            Handoff = 3
        }

        private const string AckIdleTimerName = "AckIdleTimer";

        #endregion
    }

    internal class EndpointReader : EndpointActor
    {
        public EndpointReader(Address localAddress, Address remoteAddress, Transport.Transport transport, RemoteSettings settings) :
            base(localAddress, remoteAddress, transport, settings)
        {
        }

        protected override void OnReceive(object message)
        {

        }

        protected void NotReading(object message)
        {

        }

        #region Lifecycle event handlers



        #endregion
    }

    //protected override void OnReceive(object message)
    //{
    //    message
    //        .Match()
    //        .With<Send>(Send);
    //}


    //private void Send(Send send)
    //{
    //    //TODO: should this be here?
    //    Akka.Serialization.Serialization.CurrentTransportInformation = new Information
    //    {
    //        System = Context.System,
    //        Address = localAddress,
    //    };

    //    string publicPath;
    //    if (send.Sender is NoSender)
    //    {
    //        publicPath = "";
    //    }
    //    else if (send.Sender is LocalActorRef)
    //    {
    //        publicPath = send.Sender.Path.ToStringWithAddress(localAddress);
    //    }
    //    else
    //    {
    //        publicPath = send.Sender.Path.ToString();
    //    }

    //    SerializedMessage serializedMessage = MessageSerializer.Serialize(Context.System, send.Message);

    //    RemoteEnvelope remoteEnvelope = new RemoteEnvelope.Builder()
    //        .SetSender(new ActorRefData.Builder()
    //            .SetPath(publicPath))
    //        .SetRecipient(new ActorRefData.Builder()
    //            .SetPath(send.Recipient.Path.ToStringWithAddress()))
    //        .SetMessage(serializedMessage)
    //        .SetSeq(1)
    //        .Build();

    //    remoteEnvelope.WriteDelimitedTo(stream);
    //    stream.Flush();
    //}



}