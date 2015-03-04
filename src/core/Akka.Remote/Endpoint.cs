﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Serialization;
using Akka.Util;
using Google.ProtocolBuffers;
using Akka.Util.Internal;

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
        private InternalActorRef remoteDaemon;
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
            // message is intended for the RemoteDaemon, usually a command to create a remote actor
            if (recipient == remoteDaemon)
            {
                if (settings.UntrustedMode) log.Debug("dropping daemon message in untrusted mode");
                else
                {
                    if (settings.LogReceive) log.Debug("received daemon message [{0}]", msgLog);
                    remoteDaemon.Tell(payload);
                }
            }

            //message is intended for a local recipient
            else if ((recipient is LocalRef || recipient is RepointableActorRef) && recipient.IsLocal)
            {
                if (settings.LogReceive) log.Debug("received local message [{0}]", msgLog);
                payload.Match()
                    .With<ActorSelectionMessage>(sel =>
                    {
                        var actorPath = "/" + string.Join("/", sel.Elements.Select(x => x.ToString()));
                        if (settings.UntrustedMode
                            && (!settings.TrustedSelectionPaths.Contains(actorPath)
                            || sel.Message is PossiblyHarmful
                            || recipient != provider.Guardian))
                        {
                            log.Debug(
                                "operating in UntrustedMode, dropping inbound actor selection to [{0}], allow it" +
                                "by adding the path to 'akka.remote.trusted-selection-paths' in configuration",
                                actorPath);
                        }
                        else
                        {
                            //run the receive logic for ActorSelectionMessage here to make sure it is not stuck on busy user actor
                            ActorSelection.DeliverSelection(recipient, sender, sel);
                        }
                    })
                    .With<PossiblyHarmful>(msg =>
                    {
                        if (settings.UntrustedMode)
                        {
                            log.Debug("operating in UntrustedMode, dropping inbound PossiblyHarmful message of type {0}", msg.GetType());
                        }
                    })
                    .With<SystemMessage>(msg => { recipient.Tell(msg); })
                    .Default(msg =>
                    {
                        recipient.Tell(msg, sender);
                    });
            }

            // message is intended for a remote-deployed recipient
            else if ((recipient is RemoteRef || recipient is RepointableActorRef) && !recipient.IsLocal && !settings.UntrustedMode)
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
                        payloadClass, recipient, recipientAddress, string.Join(",", provider.Transport.Addresses));
                }
            }
            else
            {
                log.Error(
                        "Dropping message [{0}] for non-local recipient [{1}] arriving at [{2}] inbound addresses [{3}]",
                        payloadClass, recipient, recipientAddress, string.Join(",", provider.Transport.Addresses));
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
        public EndpointDisassociatedException(string msg)
            : base(msg)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class EndpointAssociationException : EndpointException
    {
        public EndpointAssociationException(string msg)
            : base(msg)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class OversizedPayloadException : EndpointException
    {
        public OversizedPayloadException(string msg)
            : base(msg)
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
    internal class ReliableDeliverySupervisor : UntypedActor
    {
        private readonly LoggingAdapter _log = Context.GetLogger();

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
            Uid = handleOrActive != null ? (int?)handleOrActive.HandshakeInfo.Uid : null;
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

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                var directive = Directive.Stop;
                ex.Match()
                    .With<IAssociationProblem>(problem => directive = Directive.Escalate)
                    .Default(e =>
                    {
                        _log.Warning("Association with remote system {0} has failed; address is now gated for {1} ms. Reason is: [{2}]", remoteAddress, settings.RetryGateClosedFor.TotalMilliseconds, ex.Message);
                        UidConfirmed = false;
                        Context.Become(Gated);
                        currentHandle = null;
                        Context.Parent.Tell(new EndpointWriter.StoppedReading(Self));
                        directive = Directive.Stop;
                    });

                return directive;
            });
        }

        protected override void PostStop()
        {
            foreach (var msg in _resendBuffer.Nacked.Concat(_resendBuffer.NonAcked))
            {
                Context.System.DeadLetters.Tell(msg.Copy(opt: null));
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
                    _writer.Tell(EndpointWriter.FlushAndStop.Instance);
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
                                    "Error encountered while processing system message acknowledgement {0} {1}",
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
                    if (Uid.HasValue && Uid.Value != g.Uid) Reset();
                    else UnstashAcks();
                    Uid = refuseUid;
                })
                .With<EndpointWriter.StopReading>(stopped =>
                {
                    _writer.Forward(stopped); //forward the request
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
                .With<EndpointWriter.StopReading>(stop =>
                {
                    stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer));
                    Sender.Tell(new EndpointWriter.StoppedReading(stop.Writer));
                });
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
                .With<EndpointWriter.StopReading>(stop => stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer)));
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
            if (message is Terminated)
            {
                //Clear buffer to prevent sending system messages to dead letters -- at this point we are shutting down and
                //don't know if they were properly delivered or not
                _resendBuffer = new AckedSendBuffer<EndpointManager.Send>(0);
                Context.Stop(Self);
            }
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
    internal abstract class EndpointActor : UntypedActor
    {
        protected readonly Address LocalAddress;
        protected Address RemoteAddress;
        protected RemoteSettings Settings;
        protected AkkaProtocolTransport Transport;

        private readonly LoggingAdapter _log = Context.GetLogger();

        protected readonly EventPublisher EventPublisher;
        protected bool Inbound { get; set; }

        protected EndpointActor(Address localAddress, Address remoteAddress, AkkaProtocolTransport transport,
            RemoteSettings settings)
        {
            EventPublisher = new EventPublisher(Context.System, _log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
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
                EventPublisher.NotifyListeners(ev);
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
    internal abstract class EndpointActor<TS, TD> : FSM<TS, TD>
    {
        private readonly LoggingAdapter _log = Context.GetLogger();

        protected readonly Address LocalAddress;
        protected Address RemoteAddress;
        protected RemoteSettings Settings;
        protected AkkaProtocolTransport Transport;

        protected readonly EventPublisher EventPublisher;

        protected bool Inbound { get; set; }

        protected EndpointActor(Address localAddress, Address remoteAddress, AkkaProtocolTransport transport,
            RemoteSettings settings)
        {
            EventPublisher = new EventPublisher(Context.System, _log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
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
                EventPublisher.NotifyListeners(ev);
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
    internal class EndpointWriter : EndpointActor
    {
        public EndpointWriter(AkkaProtocolHandle handleOrActive, Address localAddress, Address remoteAddress,
            int? refuseUid, AkkaProtocolTransport transport, RemoteSettings settings,
            AkkaPduCodec codec, ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            ActorRef reliableDeliverySupervisor = null) :
            base(localAddress, remoteAddress, transport, settings)
        {
            _handleOrActive = handleOrActive;
            _refuseUid = refuseUid;
            _codec = codec;
            _reliableDeliverySupervisor = reliableDeliverySupervisor;
            _system = Context.System;
            _provider = RARP.For(Context.System).Provider;
            _msgDispatcher = new DefaultMessageDispatcher(_system, _provider, _log);
            _receiveBuffers = receiveBuffers;
            Inbound = handleOrActive != null;
            _ackDeadline = NewAckDeadline();
            _handle = handleOrActive;

            if (_handle == null)
            {
                Context.Become(Initializing);
            }
            else
            {
                Context.Become(Writing);
            }

            _ackIdleTimer = new CancellationTokenSource();
        }

        private readonly LoggingAdapter _log = Context.GetLogger();
        private AkkaProtocolHandle _handleOrActive;
        private readonly int? _refuseUid;
        private readonly AkkaPduCodec _codec;
        private readonly ActorRef _reliableDeliverySupervisor;
        private readonly ActorSystem _system;
        private readonly RemoteActorRefProvider _provider;
        private readonly ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> _receiveBuffers;
        private DisassociateInfo _stopReason = DisassociateInfo.Unknown;

        private ActorRef _reader;
        private readonly AtomicCounter _readerId = new AtomicCounter(0);
        private readonly InboundMessageDispatcher _msgDispatcher;

        private Ack _lastAck = null;
        private Deadline _ackDeadline;
        private AkkaProtocolHandle _handle;

        private readonly CancellationTokenSource _ackIdleTimer;

        // Use an internal buffer instead of Stash for efficiency
        // stash/unstashAll is slow when many messages are stashed
        // IMPORTANT: sender is not stored, so sender() and forward must not be used in EndpointWriter
        private readonly LinkedList<object> _buffer = new LinkedList<object>();

        //buffer for IPriorityMessages - ensures that heartbeats get delivered before user-defined messages
        private readonly LinkedList<EndpointManager.Send> _prioBuffer = new LinkedList<EndpointManager.Send>();
        private long _largeBufferLogTimestamp = SystemNanoTime.GetNanos();

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
            throw new IllegalActorStateException("EndpointWriter must not be restarted");
        }

        protected override void PreStart()
        {
            if (_handle == null)
            {
                Transport.Associate(RemoteAddress, _refuseUid).ContinueWith(x => new Handle(x.Result),
                    TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.AttachedToParent)
                    .PipeTo(Self);
            }
            else
            {
                _reader = StartReadEndpoint(_handle);
            }

            var ackIdleInterval = new TimeSpan(Settings.SysMsgAckTimeout.Ticks / 2);
            Context.System.Scheduler.Schedule(ackIdleInterval, ackIdleInterval, Self, AckIdleCheckTimer.Instance,
                _ackIdleTimer.Token);
        }

        protected override void PostStop()
        {
            _ackIdleTimer.Cancel();
            while (_prioBuffer.Any())
            {
                _system.DeadLetters.Tell(_prioBuffer.First);
                _prioBuffer.RemoveFirst();
            }
            while (_buffer.Any())
            {
                _system.DeadLetters.Tell(_buffer.First);
                _buffer.RemoveFirst();
            }
            if (_handle != null) _handle.Disassociate(_stopReason);
            EventPublisher.NotifyListeners(new DisassociatedEvent(LocalAddress, RemoteAddress, Inbound));
        }

        #endregion

        #region Receives

        protected override void OnReceive(object message)
        {
            //This should never be hit.
            Unhandled(message);
        }

        private void Initializing(object message)
        {
            if (message is EndpointManager.Send)
            {
                EnqueueInBuffer(message);
            }
            else if (message is Status.Failure)
            {
                var failure = (Status.Failure)message;
                if (failure.Cause is InvalidAssociationException)
                {
                    PublishAndThrow(new InvalidAssociation(LocalAddress, RemoteAddress, failure.Cause),
                        LogLevel.WarningLevel);
                }
                else
                {
                    PublishAndThrow(
                        new EndpointAssociationException(string.Format("Association failed with {0}",
                            RemoteAddress)), LogLevel.DebugLevel);
                }
            }
            else if (message is Handle)
            {
                var inboundHandle = (Handle)message;
                Context.Parent.Tell(
                    new ReliableDeliverySupervisor.GotUid((int)inboundHandle.ProtocolHandle.HandshakeInfo.Uid));
                _handle = inboundHandle.ProtocolHandle;
                _reader = StartReadEndpoint(_handle);
                BecomeWritingOrSendBufferedMessages();
            }
            else
            {
                Unhandled(message);
            }
        }

        private void Buffering(object message)
        {
            if (message is EndpointManager.Send)
            {
                EnqueueInBuffer(message);
            }
            else if (message is BackoffTimer)
            {
                SendBufferedMessages();
            }
            else if (message is FlushAndStop)
            {
                _buffer.AddLast(message); //Flushing is postponed after the pending writes
                Context.System.Scheduler.ScheduleOnce(Settings.FlushWait, Self, FlushAndStopTimeout.Instance);
            }
            else if (message is FlushAndStopTimeout)
            {
                //enough
                DoFlushAndStop();
            }
            else
            {
                Unhandled(message);
            }
        }

        private void Writing(object message)
        {
            if (message is EndpointManager.Send)
            {
                var s = (EndpointManager.Send)message;
                if (!WriteSend(s))
                {
                    if (s.Seq == null) EnqueueInBuffer(s);
                    ScheduleBackoffTimer();
                    Context.Become(Buffering);
                }
            }
            else if (message is FlushAndStop)
            {
                DoFlushAndStop();
            }
            else if (message is AckIdleCheckTimer)
            {
                if (_ackDeadline.IsOverdue)
                {
                    TrySendPureAck();
                }
            }
            else
            {
                Unhandled(message);
            }
        }

        private void Handoff(object message)
        {
            if (message is Terminated)
            {
                _reader = StartReadEndpoint(_handle);
                BecomeWritingOrSendBufferedMessages();
            }
            else if (message is EndpointManager.Send)
            {
                EnqueueInBuffer(message);
            }
            else
            {
                Unhandled(message);
            }
        }

        protected override void Unhandled(object message)
        {
            if (message is Terminated)
            {
                var t = (Terminated)message;
                if (_reader == null || t.ActorRef.Equals(_reader))
                {
                    PublishAndThrow(new EndpointDisassociatedException("Disassociated"), LogLevel.DebugLevel);
                }
            }
            else if (message is StopReading)
            {
                var stop = (StopReading)message;
                if (_reader != null)
                {
                    _reader.Tell(stop, stop.ReplyTo);
                }
                else
                {
                    // initalizing, buffer and take care of it later when buffer is sent
                    EnqueueInBuffer(message);
                }
            }
            else if (message is TakeOver)
            {
                var takeover = (TakeOver)message;

                // Shutdown old reader
                _handle.Disassociate();
                _handle = takeover.ProtocolHandle;
                takeover.ReplyTo.Tell(new TookOver(Self, _handle));
                Context.Become(Handoff);
            }
            else if (message is FlushAndStop)
            {
                _stopReason = DisassociateInfo.Shutdown;
                Context.Stop(Self);
            }
            else if (message is OutboundAck)
            {
                var ack = (OutboundAck)message;
                _lastAck = ack.Ack;
                if (_ackDeadline.IsOverdue)
                    TrySendPureAck();
            }
            else if (message is AckIdleCheckTimer || message is FlushAndStopTimeout || message is BackoffTimer)
            {
                //ignore
            }
            else
            {
                base.Unhandled(message);
            }
        }

        #endregion

        #region Internal methods

        private Deadline NewAckDeadline()
        {
            return Deadline.Now + Settings.SysMsgAckTimeout;
        }

        private void PublishAndThrow(Exception reason, LogLevel level)
        {
            reason.Match().With<EndpointDisassociatedException>(endpoint => PublishDisassociated())
                .Default(msg => PublishError(reason, level));

            throw reason;
        }

        private ActorRef StartReadEndpoint(AkkaProtocolHandle handle)
        {
            var newReader =
                Context.ActorOf(
                    EndpointReader.ReaderProps(LocalAddress, RemoteAddress, Transport, Settings, _codec, _msgDispatcher,
                        Inbound, (int)handle.HandshakeInfo.Uid, _receiveBuffers, _reliableDeliverySupervisor)
                        .WithDeploy(Deploy.Local),
                    string.Format("endpointReader-{0}-{1}", AddressUrlEncoder.Encode(RemoteAddress), _readerId.Next()));
            Context.Watch(newReader);
            handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(newReader));
            return newReader;
        }

        private SerializedMessage SerializeMessage(object msg)
        {
            if (_handle == null)
            {
                throw new EndpointException("Internal error: No handle was presentduring serialization of outbound message.");
            }

            Akka.Serialization.Serialization.CurrentTransportInformation = new Information() { Address = _handle.LocalAddress, System = _system };
            return MessageSerializer.Serialize(_system, msg);
        }

        private int _writeCount = 0;
        private int _maxWriteCount = MaxWriteCount;
        private long _adaptiveBackoffNanos = 1000000L; // 1 ms
        private bool _fullBackoff = false;

        // FIXME remove these counters when tuning/testing is completed
        private int _fullBackoffCount = 1;
        private int _smallBackoffCount = 0;
        private int _noBackoffCount = 0;

        private void AdjustAdaptiveBackup()
        {
            _maxWriteCount = Math.Max(_writeCount, _maxWriteCount);
            if (_writeCount <= SendBufferBatchSize)
            {
                _fullBackoff = true;
                _adaptiveBackoffNanos = Math.Min(Convert.ToInt64(_adaptiveBackoffNanos * 1.2), MaxAdaptiveBackoffNanos);
            }
            else if (_writeCount >= _maxWriteCount * 0.6)
            {
                _adaptiveBackoffNanos = Math.Max(Convert.ToInt64(_adaptiveBackoffNanos * 0.9), MaxAdaptiveBackoffNanos);
            }
            else if (_writeCount <= _maxWriteCount * 0.2)
            {
                _adaptiveBackoffNanos = Math.Min(Convert.ToInt64(_adaptiveBackoffNanos * 1.1), MaxAdaptiveBackoffNanos);
            }
            _writeCount = 0;
        }

        private void ScheduleBackoffTimer()
        {
            if (_fullBackoff)
            {
                _fullBackoffCount += 1;
                _fullBackoff = false;
                Context.System.Scheduler.ScheduleOnce(Settings.BackoffPeriod, Self, BackoffTimer.Instance);
            }
            else
            {
                _smallBackoffCount += 1;
                var s = Self;
                var backoffDeadlineNanoTime = SystemNanoTime.GetNanos() + _adaptiveBackoffNanos;

                Task.Run(() =>
                {
                    Action backoff = null;
                    backoff = () =>
                    {
                        var backOffNanos = backoffDeadlineNanoTime - SystemNanoTime.GetNanos();
                        if (backOffNanos > 0)
                        {
                            Thread.Sleep(new TimeSpan(backOffNanos.ToTicks()));
                            backoff();
                        }
                    };
                    backoff();
                    s.Tell(BackoffTimer.Instance, ActorRef.NoSender);
                });
            }
        }

        private void DoFlushAndStop()
        {
            //Try to send last Ack message
            TrySendPureAck();
            _stopReason = DisassociateInfo.Shutdown;
            Context.Stop(Self);
        }

        private void TrySendPureAck()
        {
            if (_handle != null && _lastAck != null)
            {
                if (_handle.Write(_codec.ConstructPureAck(_lastAck)))
                {
                    _ackDeadline = NewAckDeadline();
                    _lastAck = null;
                }
            }
        }

        private void EnqueueInBuffer(object message)
        {
            var send = message as EndpointManager.Send;
            if (send != null && send.Message is IPriorityMessage)
                _prioBuffer.AddLast(send);
            else if (send != null && send.Message is ActorSelectionMessage &&
                     send.Message.AsInstanceOf<ActorSelectionMessage>().Message is IPriorityMessage)
            {
                _prioBuffer.AddLast(send);
            }
            else
            {
                _buffer.AddLast(message);
            }
        }

        private void BecomeWritingOrSendBufferedMessages()
        {
            if (!_buffer.Any())
            {
                Context.Become(Writing, true);
            }
            else
            {
                Context.Become(Buffering, true);
                SendBufferedMessages();
            }
        }

        private bool WriteSend(EndpointManager.Send send)
        {
            try
            {
                if (_handle == null)
                    throw new EndpointException(
                        "Internal error: Endpoint is in state Writing, but no association handle is present.");
                if (_provider.RemoteSettings.LogSend)
                {
                    var msgLog = string.Format("RemoteMessage: {0} to [{1}]<+[{2}] from [{3}]", send.Message,
                        send.Recipient, send.Recipient.Path, send.SenderOption ?? _system.DeadLetters);
                    _log.Debug(msgLog);
                }

                var pdu = _codec.ConstructMessage(send.Recipient.LocalAddressToUse, send.Recipient,
                    SerializeMessage(send.Message), send.SenderOption, send.Seq, _lastAck);

                //todo: RemoteMetrics https://github.com/akka/akka/blob/dc0547dd73b54b5de9c3e0b45a21aa865c5db8e2/akka-remote/src/main/scala/akka/remote/Endpoint.scala#L742

                //todo: max payload size validation

                var ok = _handle.Write(pdu);

                if (ok)
                {
                    _ackDeadline = NewAckDeadline();
                    _lastAck = null;
                    return true;
                }

                return false;
            }
            catch (SerializationException ex)
            {
                _log.Error(ex, "Transient association error (association remains live)");
                return true;
            }
            catch (EndpointException ex)
            {
                PublishAndThrow(ex, LogLevel.ErrorLevel);
            }
            catch (Exception ex)
            {
                PublishAndThrow(new EndpointException("Failed to write message to the transport", ex),
                    LogLevel.ErrorLevel);
            }

            return false;
        }

        private void SendBufferedMessages()
        {
            var sendDelegate = new Func<object, bool>(msg =>
            {
                if (msg is EndpointManager.Send)
                {
                    return WriteSend((EndpointManager.Send)msg);
                }
                else if (msg is FlushAndStop)
                {
                    DoFlushAndStop();
                    return false;
                }
                else if (msg is StopReading)
                {
                    var s = (StopReading)msg;
                    if (_reader != null) _reader.Tell(s, s.ReplyTo);
                }
                return true;
            });

            Func<int, bool> writeLoop = null;
            writeLoop = new Func<int, bool>(count =>
            {
                if (count > 0 && _buffer.Any())
                {
                    if (sendDelegate(_buffer.First.Value))
                    {
                        _buffer.RemoveFirst();
                        _writeCount += 1;
                        return writeLoop(count - 1);
                    }
                    return false;
                }
                
                return true;
            });

            Func<bool> writePrioLoop = null;
            writePrioLoop = () =>
            {
                if (!_prioBuffer.Any()) return true;
                if (WriteSend(_prioBuffer.First.Value))
                {
                    _prioBuffer.RemoveFirst();
                    return writePrioLoop();
                }
                return false;
            };

            var size = _buffer.Count;

            var ok = writePrioLoop() && writeLoop(SendBufferBatchSize);
            if (!_buffer.Any() && !_prioBuffer.Any())
            {
                // FIXME remove this when testing/tuning is completed
                if (_log.IsDebugEnabled)
                {
                    _log.Debug("Drained buffer with maxWriteCount: {0}, fullBackoffCount: {1}," +
                               "smallBackoffCount: {2}, noBackoffCount: {3}," +
                               "adaptiveBackoff: {4}", _maxWriteCount, _fullBackoffCount, _smallBackoffCount, _noBackoffCount, _adaptiveBackoffNanos / 100);
                }
                _fullBackoffCount = 1;
                _smallBackoffCount = 0;
                _noBackoffCount = 0;
                _writeCount = 0;
                _maxWriteCount = MaxWriteCount;
                Context.Become(Writing);
            }
            else if (ok)
            {
                _noBackoffCount += 1;
                Self.Tell(BackoffTimer.Instance);
            }
            else
            {
                if (size > Settings.LogBufferSizeExceeding)
                {
                    var now = SystemNanoTime.GetNanos();
                    if (now - _largeBufferLogTimestamp >= LogBufferSizeInterval)
                    {
                        _log.Warning("[{0}] buffered messages in EndpointWriter for [{1}]. " +
                                     "You should probablyimplement flow control to avoid flooding the remote connection.", size, RemoteAddress);
                        _largeBufferLogTimestamp = now;
                    }
                }
            }

            AdjustAdaptiveBackup();
            ScheduleBackoffTimer();
        }

        #endregion

        #region Static methods and Internal messages

        // These settings are not configurable because wrong configuration will break the auto-tuning
        private const int SendBufferBatchSize = 5;
        private const long MinAdaptiveBackoffNanos = 300000L; // 0.3 ms
        private const long MaxAdaptiveBackoffNanos = 2000000L; // 2 ms
        private const long LogBufferSizeInterval = 5000000000L; // 5 s, in nanoseconds
        private const int MaxWriteCount = 50;

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
            public TakeOver(AkkaProtocolHandle protocolHandle, ActorRef replyTo)
            {
                ProtocolHandle = protocolHandle;
                ReplyTo = replyTo;
            }

            public AkkaProtocolHandle ProtocolHandle { get; private set; }

            public ActorRef ReplyTo { get; private set; }
        }

        public sealed class TookOver : NoSerializationVerificationNeeded
        {
            public TookOver(ActorRef writer, AkkaProtocolHandle protocolHandle)
            {
                ProtocolHandle = protocolHandle;
                Writer = writer;
            }

            public ActorRef Writer { get; private set; }

            public AkkaProtocolHandle ProtocolHandle { get; private set; }
        }

        public sealed class BackoffTimer
        {
            private BackoffTimer() { }
            private static readonly BackoffTimer _instance = new BackoffTimer();
            public static BackoffTimer Instance { get { return _instance; } }
        }

        public sealed class FlushAndStop
        {
            private FlushAndStop() { }
            private static readonly FlushAndStop _instance = new FlushAndStop();
            public static FlushAndStop Instance { get { return _instance; } }
        }

        public sealed class AckIdleCheckTimer
        {
            private AckIdleCheckTimer() { }
            private static readonly AckIdleCheckTimer _instance = new AckIdleCheckTimer();
            public static AckIdleCheckTimer Instance { get { return _instance; } }
        }

        public sealed class FlushAndStopTimeout
        {
            private FlushAndStopTimeout() { }
            private static readonly FlushAndStopTimeout _instance = new FlushAndStopTimeout();
            public static FlushAndStopTimeout Instance { get { return _instance; } }
        }

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
            public StopReading(ActorRef writer, ActorRef replyTo)
            {
                Writer = writer;
                ReplyTo = replyTo;
            }

            public ActorRef Writer { get; private set; }

            public ActorRef ReplyTo { get; private set; }
        }

        public sealed class StoppedReading
        {
            public StoppedReading(ActorRef writer)
            {
                Writer = writer;
            }

            public ActorRef Writer { get; private set; }
        }

        public sealed class OutboundAck
        {
            public OutboundAck(Ack ack)
            {
                Ack = ack;
            }

            public Ack Ack { get; private set; }
        }

        private const string AckIdleTimerName = "AckIdleTimer";

        #endregion

    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class EndpointReader : EndpointActor
    {
        public EndpointReader(Address localAddress, Address remoteAddress, AkkaProtocolTransport transport,
            RemoteSettings settings, AkkaPduCodec codec, InboundMessageDispatcher msgDispatch, bool inbound,
            int uid, ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            ActorRef reliableDelvierySupervisor = null) :
            base(localAddress, remoteAddress, transport, settings)
        {
            _receiveBuffers = receiveBuffers;
            _msgDispatch = msgDispatch;
            Inbound = inbound;
            _uid = uid;
            _reliableDeliverySupervisor = reliableDelvierySupervisor;
            _codec = codec;
            _provider = RARP.For(Context.System).Provider;
        }

        private AkkaPduCodec _codec;
        private ActorRef _reliableDeliverySupervisor;
        private ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> _receiveBuffers;
        private int _uid;
        private InboundMessageDispatcher _msgDispatch;

        private RemoteActorRefProvider _provider;
        private AckedReceiveBuffer<Message> _ackedReceiveBuffer = new AckedReceiveBuffer<Message>();

        #region ActorBase overrides

        protected override void PreStart()
        {
            EndpointManager.ResendState resendState;
            if (_receiveBuffers.TryGetValue(new EndpointManager.Link(LocalAddress, RemoteAddress), out resendState))
            {
                _ackedReceiveBuffer = resendState.Buffer;
                DeliverAndAck();
            }
        }

        protected override void PostStop()
        {
            SaveState();
        }

        protected override void OnReceive(object message)
        {
            if (message is Disassociated)
            {
                HandleDisassociated(((Disassociated)message).Info);
            }
            else if (message is InboundPayload)
            {
                var payload = (InboundPayload)message;
                var ackAndMessage = TryDecodeMessageAndAck(payload.Payload);
                if (ackAndMessage.AckOption != null && _reliableDeliverySupervisor != null)
                    _reliableDeliverySupervisor.Tell(ackAndMessage.AckOption);
                if (ackAndMessage.MessageOption != null)
                {
                    if (ackAndMessage.MessageOption.ReliableDeliveryEnabled)
                    {
                        _ackedReceiveBuffer = _ackedReceiveBuffer.Receive(ackAndMessage.MessageOption);
                        DeliverAndAck();
                    }
                    else
                    {
                        _msgDispatch.Dispatch(ackAndMessage.MessageOption.Recipient,
                            ackAndMessage.MessageOption.RecipientAddress,
                            ackAndMessage.MessageOption.SerializedMessage,
                            ackAndMessage.MessageOption.SenderOptional);
                    }
                }
            }
            else if (message is EndpointWriter.StopReading)
            {
                var stop = (EndpointWriter.StopReading)message;
                SaveState();
                Context.Become(NotReading);
                stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer));
            }
            else
            {
                Unhandled(message);
            }
        }

        protected void NotReading(object message)
        {
            if (message is Disassociated)
            {
                HandleDisassociated(((Disassociated)message).Info);
            }
            else if (message is EndpointWriter.StopReading)
            {
                var stop = (EndpointWriter.StopReading)message;
                Sender.Tell(new EndpointWriter.StoppedReading(stop.Writer));
            } else if (message is InboundPayload)
            {
                var payload = (InboundPayload)message;
                var ackAndMessage = TryDecodeMessageAndAck(payload.Payload);
                if (ackAndMessage.AckOption != null && _reliableDeliverySupervisor != null)
                    _reliableDeliverySupervisor.Tell(ackAndMessage.AckOption);
            }
            else
            {
                Unhandled(message);
            }
        }

        #endregion



        #region Lifecycle event handlers

        private void SaveState()
        {
            var key = new EndpointManager.Link(LocalAddress, RemoteAddress);
            EndpointManager.ResendState previousValue;
            _receiveBuffers.TryGetValue(key, out previousValue);
            UpdateSavedState(key, previousValue);
        }

        private EndpointManager.ResendState Merge(EndpointManager.ResendState current,
            EndpointManager.ResendState oldState)
        {
            if (current.Uid == oldState.Uid) return new EndpointManager.ResendState(_uid, oldState.Buffer.MergeFrom(current.Buffer));
            return current;
        }

        private void UpdateSavedState(EndpointManager.Link key, EndpointManager.ResendState expectedState)
        {
            if (expectedState == null)
            {
                if (_receiveBuffers.ContainsKey(key))
                {
                    var updatedValue = new EndpointManager.ResendState(_uid, _ackedReceiveBuffer);
                    _receiveBuffers.AddOrUpdate(key, updatedValue, (link, state) => updatedValue);
                    UpdateSavedState(key, updatedValue);
                }
            }
            else
            {
                var canReplace = _receiveBuffers.ContainsKey(key) && _receiveBuffers[key].Equals(expectedState);
                if (canReplace)
                {
                    _receiveBuffers[key] = Merge(new EndpointManager.ResendState(_uid, _ackedReceiveBuffer),
                        expectedState);
                }
                else
                {
                    EndpointManager.ResendState previousValue;
                    _receiveBuffers.TryGetValue(key, out previousValue);
                    UpdateSavedState(key, previousValue);
                }
            }
        }

        private void HandleDisassociated(DisassociateInfo info)
        {
            switch (info)
            {
                case DisassociateInfo.Quarantined:
                    throw new InvalidAssociation(LocalAddress, RemoteAddress, new InvalidAssociationException("The remote system has quarantined this system. No further associations " +
                                                                                                              "to the remote system are possible until this system is restarted."));
                case DisassociateInfo.Shutdown:
                    throw new ShutDownAssociation(LocalAddress, RemoteAddress, new InvalidAssociationException("The remote system terminated the association because it is shutting down."));
                case DisassociateInfo.Unknown:
                default:
                    Context.Stop(Self);
                    break;
            }
        }

        private void DeliverAndAck()
        {
            var deliverable = _ackedReceiveBuffer.ExtractDeliverable;
            _ackedReceiveBuffer = deliverable.Buffer;

            // Notify writer that some messages can be acked
            Context.Parent.Tell(new EndpointWriter.OutboundAck(deliverable.Ack));
            deliverable.Deliverables.ForEach(msg => _msgDispatch.Dispatch(msg.Recipient, msg.RecipientAddress, msg.SerializedMessage, msg.SenderOptional));
        }

        private AckAndMessage TryDecodeMessageAndAck(ByteString pdu)
        {
            try
            {
                return _codec.DecodeMessage(pdu, _provider, LocalAddress);
            }
            catch (Exception ex)
            {
                throw new EndpointException("Error while decoding incoming Akka PDU", ex);
            }
        }

        #endregion

        #region Static members

        public static Props ReaderProps(Address localAddress, Address remoteAddress, AkkaProtocolTransport transport,
            RemoteSettings settings, AkkaPduCodec codec, InboundMessageDispatcher dispatcher, bool inbound, int uid,
            ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            ActorRef reliableDeliverySupervisor = null)
        {
            return
                Props.Create(
                    () =>
                        new EndpointReader(localAddress, remoteAddress, transport, settings, codec, dispatcher, inbound,
                            uid, receiveBuffers, reliableDeliverySupervisor));
        }

        #endregion
    }
}