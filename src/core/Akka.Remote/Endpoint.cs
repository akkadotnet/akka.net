//-----------------------------------------------------------------------
// <copyright file="Endpoint.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Pattern;
using Akka.Remote.Transport;
using Akka.Serialization;
using Akka.Util;
using Akka.Util.Internal;
using Google.ProtocolBuffers;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    // ReSharper disable once InconsistentNaming
    internal interface IInboundMessageDispatcher
    {
        void Dispatch(IInternalActorRef recipient, Address recipientAddress, SerializedMessage message,
            IActorRef senderOption = null);
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class DefaultMessageDispatcher : IInboundMessageDispatcher
    {
        private ActorSystem system;
        private RemoteActorRefProvider provider;
        private ILoggingAdapter log;
        private IInternalActorRef remoteDaemon;
        private RemoteSettings settings;

        public DefaultMessageDispatcher(ActorSystem system, RemoteActorRefProvider provider, ILoggingAdapter log)
        {
            this.system = system;
            this.provider = provider;
            this.log = log;
            remoteDaemon = provider.RemoteDaemon;
            settings = provider.RemoteSettings;
        }

        public void Dispatch(IInternalActorRef recipient, Address recipientAddress, SerializedMessage message,
            IActorRef senderOption = null)
        {
            var payload = MessageSerializer.Deserialize(system, message);
            Type payloadClass = payload == null ? null : payload.GetType();
            var sender = senderOption ?? system.DeadLetters;
            var originalReceiver = recipient.Path;


            // message is intended for the RemoteDaemon, usually a command to create a remote actor
            if (recipient.Equals(remoteDaemon))
            {
                if (settings.UntrustedMode) log.Debug("dropping daemon message in untrusted mode");
                else
                {
                    if (settings.LogReceive)
                    {
                        var msgLog = string.Format("RemoteMessage: {0} to {1}<+{2} from {3}", payload, recipient, originalReceiver,sender);
                        log.Debug("received daemon message [{0}]", msgLog);
                    }
                    remoteDaemon.Tell(payload);
                }
            }

            //message is intended for a local recipient
            else if ((recipient is ILocalRef || recipient is RepointableActorRef) && recipient.IsLocal)
            {
                if (settings.LogReceive)
                {
                    var msgLog = string.Format("RemoteMessage: {0} to {1}<+{2} from {3}", payload, recipient, originalReceiver,sender);
                    log.Debug("received local message [{0}]", msgLog);
                }
                payload.Match()
                    .With<ActorSelectionMessage>(sel =>
                    {
                        var actorPath = "/" + string.Join("/", sel.Elements.Select(x => x.ToString()));
                        if (settings.UntrustedMode
                            && (!settings.TrustedSelectionPaths.Contains(actorPath)
                            || sel.Message is IPossiblyHarmful
                            || !recipient.Equals(provider.Guardian)))
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
                    .With<IPossiblyHarmful>(msg =>
                    {
                        if (settings.UntrustedMode)
                        {
                            log.Debug("operating in UntrustedMode, dropping inbound IPossiblyHarmful message of type {0}", msg.GetType());
                        }
                    })
                    .With<ISystemMessage>(msg => { recipient.Tell(msg); })
                    .Default(msg =>
                    {
                        recipient.Tell(msg, sender);
                    });
            }

            // message is intended for a remote-deployed recipient
            else if ((recipient is IRemoteRef || recipient is RepointableActorRef) && !recipient.IsLocal && !settings.UntrustedMode)
            {
                if (settings.LogReceive)
                {
                    var msgLog = string.Format("RemoteMessage: {0} to {1}<+{2} from {3}", payload, recipient, originalReceiver, sender);
                    log.Debug("received remote-destined message {0}", msgLog);
                }
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

        protected EndpointException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
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
        public InvalidAssociation(Address localAddress, Address remoteAddress, Exception cause = null, DisassociateInfo? disassociateInfo = null)
            : base(string.Format("Invalid address: {0}", remoteAddress), cause)
        {
            RemoteAddress = remoteAddress;
            LocalAddress = localAddress;
            DisassociationInfo = disassociateInfo;
        }

        public Address LocalAddress { get; private set; }

        public Address RemoteAddress { get; private set; }

        public DisassociateInfo? DisassociationInfo { get; private set; }
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

        public EndpointAssociationException(string msg, Exception inner) : base(msg, inner) { }
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
    /// </summary>
    internal class ReliableDeliverySupervisor : ReceiveActor
    {
        #region Internal message classes

        public class IsIdle
        {
            public static readonly IsIdle Instance = new IsIdle();
            private IsIdle() { }
        }

        public class Idle
        {
            public static readonly Idle Instance = new Idle();
            private Idle() { }
        }

        public class TooLongIdle
        {
            public static readonly TooLongIdle Instance = new TooLongIdle();
            private TooLongIdle() { }
        }

        #endregion

        private readonly ILoggingAdapter _log = Context.GetLogger();

        private readonly Address _localAddress;
        private readonly Address _remoteAddress;
        private readonly int? _refuseUid;
        private readonly AkkaProtocolTransport _transport;
        private readonly RemoteSettings _settings;
        private AkkaPduCodec _codec;
        private AkkaProtocolHandle _currentHandle;
        private readonly ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> _receiveBuffers;

        public ReliableDeliverySupervisor(
            AkkaProtocolHandle handleOrActive,
            Address localAddress,
            Address remoteAddress,
            int? refuseUid,
            AkkaProtocolTransport transport,
            RemoteSettings settings,
            AkkaPduCodec codec,
            ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers)
        {
            _localAddress = localAddress;
            _remoteAddress = remoteAddress;
            _refuseUid = refuseUid;
            _transport = transport;
            _settings = settings;
            _codec = codec;
            _currentHandle = handleOrActive;
            _receiveBuffers = receiveBuffers;
            Reset(); // needs to be called at startup
            _writer = CreateWriter(); // need to create writer at startup
            Uid = handleOrActive != null ? (int?)handleOrActive.HandshakeInfo.Uid : null;
            UidConfirmed = Uid.HasValue;
            Receiving();
            _autoResendTimer = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(_settings.SysResendTimeout, _settings.SysResendTimeout, Self, new AttemptSysMsgRedelivery(),
                    Self);
        }

        private readonly ICancelable _autoResendTimer;

        public int? Uid { get; set; }

        /// <summary>
        /// Processing of <see cref="Ack"/>s has to be delayed until the UID is discovered after a reconnect. Depending whether the
        /// UID matches the expected one, pending Acks can be processed or must be dropped. It is guaranteed that for any inbound
        /// connections (calling <see cref="CreateWriter"/>) the first message from that connection is <see cref="GotUid"/>, therefore it serves
        /// a separator.
        /// 
        /// If we already have an inbound handle then UID is initially confirmed.
        /// (This actor is never restarted.)
        /// </summary>
        public bool UidConfirmed { get; private set; }

        private Deadline _bailoutAt = null;

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                if (ex is IAssociationProblem)
                    return Directive.Escalate;

                _log.Warning("Association with remote system {0} has failed; address is now gated for {1} ms. Reason is: [{2}]", _remoteAddress, _settings.RetryGateClosedFor.TotalMilliseconds, ex);
                UidConfirmed = false; // Need confirmation of UID again
                if (_bufferWasInUse)
                {
                    if ((_resendBuffer.Nacked.Any() || _resendBuffer.NonAcked.Any()) && _bailoutAt == null)
                        _bailoutAt = Deadline.Now + _settings.InitialSysMsgDeliveryTimeout;
                    Become(() => Gated(writerTerminated: false, earlyUngateRequested: false));
                    _currentHandle = null;
                    Context.Parent.Tell(new EndpointWriter.StoppedReading(Self));
                    return Directive.Stop;
                }

                return Directive.Escalate;
            });
        }


        private ICancelable _maxSilenceTimer = null;
        private AckedSendBuffer<EndpointManager.Send> _resendBuffer;
        private long _seqCounter;

        private bool _bufferWasInUse;

        private IActorRef _writer;

        private void Reset()
        {
            _resendBuffer = new AckedSendBuffer<EndpointManager.Send>(_settings.SysMsgBufferSize);
            _seqCounter = 0L;
            _bailoutAt = null;
            _bufferWasInUse = false;
        }

        private SeqNo NextSeq()
        {
            var tmp = _seqCounter;
            _seqCounter++;
            return new SeqNo(tmp);
        }

        #region ActorBase methods and Behaviors

        protected override void PostStop()
        {
            // All remaining messages in the buffer has to be delivered to dead letters. It is important to clear the sequence
            // number otherwise deadLetters will ignore it to avoid reporting system messages as dead letters while they are
            // still possibly retransmitted.
            // Such a situation may arise when the EndpointWriter is shut down, and all of its mailbox contents are delivered
            // to dead letters. These messages should be ignored, as they still live in resendBuffer and might be delivered to
            // the remote system later.
            foreach (var msg in _resendBuffer.Nacked.Concat(_resendBuffer.NonAcked))
            {
                Context.System.DeadLetters.Tell(msg.Copy(opt: null));
            }
            EndpointManager.ResendState value;
            _receiveBuffers.TryRemove(new EndpointManager.Link(_localAddress, _remoteAddress), out value);
            _autoResendTimer.Cancel();
            _maxSilenceTimer?.Cancel();
        }

        protected override void PostRestart(Exception reason)
        {
            throw new IllegalActorStateException("BUG: ReliableDeliverySupervisor has been attempted to be restarted. This must not happen.");
        }

        protected void Receiving()
        {
            Receive<EndpointWriter.FlushAndStop>(flush =>
            {
                //Trying to serve until our last breath
                ResendAll();
                _writer.Tell(EndpointWriter.FlushAndStop.Instance);
                Become(FlushWait);
            });
            Receive<IsIdle>(idle => { }); // Do not reply, we will Terminate soon, or send a GotUid
            Receive<EndpointManager.Send>(send => HandleSend(send));
            Receive<Ack>(ack =>
            {
                // If we are not sure about the UID just ignore the ack. Ignoring is fine.
                if (UidConfirmed)
                {
                    try
                    {
                        _resendBuffer = _resendBuffer.Acknowledge(ack);
                    }
                    catch (Exception ex)
                    {
                        throw new HopelessAssociation(_localAddress, _remoteAddress, Uid,
                            new IllegalStateException($"Error encountered while processing system message acknowledgement buffer: {_resendBuffer} ack: {ack}", ex));
                    }

                    ResendNacked();
                }
            });
            Receive<AttemptSysMsgRedelivery>(sysmsg =>
            {
                if (UidConfirmed) ResendAll();
            });
            Receive<Terminated>(terminated =>
            {
                _currentHandle = null;
                Context.Parent.Tell(new EndpointWriter.StoppedReading(Self));
                if (_resendBuffer.NonAcked.Any() || _resendBuffer.Nacked.Any())
                    Context.System.Scheduler.ScheduleTellOnce(_settings.SysResendTimeout, Self,
                        new AttemptSysMsgRedelivery(), Self);
                GoToIdle();
            });
            Receive<GotUid>(g =>
            {
                _bailoutAt = null;
                Context.Parent.Tell(g);
                //New system that has the same address as the old - need to start from fresh state
                UidConfirmed = true;
                if (Uid.HasValue && Uid.Value != g.Uid) Reset();
                Uid = g.Uid;
                ResendAll();
            });
            Receive<EndpointWriter.StopReading>(stopped =>
            {
                _writer.Forward(stopped); //forward the request
            });
        }

        private void GoToIdle()
        {
            if (_bufferWasInUse && _maxSilenceTimer == null)
                _maxSilenceTimer =
                    Context.System.Scheduler.ScheduleTellOnceCancelable(_settings.QuarantineSilentSystemTimeout, Self,
                        TooLongIdle.Instance, Self);
            Become(IdleBehavior);
        }

        private void GoToActive()
        {
            _maxSilenceTimer?.Cancel();
            _maxSilenceTimer = null;
            Become(Receiving);
        }

        protected void Gated(bool writerTerminated, bool earlyUngateRequested)
        {
            Receive<Terminated>(terminated =>
            {
                if (!writerTerminated)
                {
                    if (earlyUngateRequested)
                        Self.Tell(new Ungate());
                }
                else
                    Context.System.Scheduler.ScheduleTellOnce(_settings.RetryGateClosedFor, Self, new Ungate(), Self);
                Become(() => Gated(true, earlyUngateRequested));
            });
            Receive<IsIdle>(idle => Sender.Tell(Idle.Instance));
            Receive<Ungate>(ungate =>
            {
                if (!writerTerminated)
                {
                    // Ungate was sent from EndpointManager, but we must wait for Terminated first.
                    Become(() => Gated(false, true));
                }
                else if (_resendBuffer.NonAcked.Any() || _resendBuffer.Nacked.Any())
                {
                    // If we talk to a system we have not talked to before (or has given up talking to in the past) stop
                    // system delivery attempts after the specified time. This act will drop the pending system messages and gate the
                    // remote address at the EndpointManager level stopping this actor. In case the remote system becomes reachable
                    // again it will be immediately quarantined due to out-of-sync system message buffer and becomes quarantined.
                    // In other words, this action is safe.
                    if (_bailoutAt != null && _bailoutAt.IsOverdue)
                    {
                        throw new HopelessAssociation(_localAddress, _remoteAddress, Uid,
                            new TimeoutException("Delivery of system messages timed out and they were dropped"));
                    }

                    _writer = CreateWriter();
                    //Resending will be triggered by the incoming GotUid message after the connection finished
                    GoToActive();
                }
                else
                {
                    GoToIdle();
                }
            });
            Receive<AttemptSysMsgRedelivery>(redelivery => { }); // Ignore
            Receive<EndpointManager.Send>(send => send.Message is ISystemMessage, send => TryBuffer(send.Copy(NextSeq())));
            Receive<EndpointManager.Send>(send => Context.System.DeadLetters.Tell(send));
            Receive<EndpointWriter.FlushAndStop>(flush => Context.Stop(Self));
            Receive<EndpointWriter.StopReading>(stop =>
            {
                stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer));
                Sender.Tell(new EndpointWriter.StoppedReading(stop.Writer));
            });
        }

        protected void IdleBehavior()
        {
            Receive<IsIdle>(idle => Sender.Tell(Idle.Instance));
            Receive<EndpointManager.Send>(send =>
            {
                _writer = CreateWriter();
                //Resending will be triggered by the incoming GotUid message after the connection finished
                HandleSend(send);
                GoToActive();
            });

            Receive<AttemptSysMsgRedelivery>(sys =>
            {
                if (_resendBuffer.Nacked.Any() || _resendBuffer.NonAcked.Any())
                {
                    _writer = CreateWriter();
                    //Resending will be triggered by the incoming GotUid message after the connection finished
                    GoToActive();
                }
            });
            Receive<TooLongIdle>(idle =>
            {
                HandleTooLongIdle();
            });
            Receive<EndpointWriter.FlushAndStop>(stop => Context.Stop(Self));
            Receive<EndpointWriter.StopReading>(stop => stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer)));
        }

        protected void FlushWait()
        {
            Receive<IsIdle>(idle => { }); // Do not reply, we will Terminate soon, which will do the inbound connection unstashing
            Receive<Terminated>(terminated =>
            {
                //Clear buffer to prevent sending system messages to dead letters -- at this point we are shutting down and
                //don't know if they were properly delivered or not
                _resendBuffer = new AckedSendBuffer<EndpointManager.Send>(0);
                Context.Stop(Self);
            });
            ReceiveAny(o => { }); // ignore
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

        public static Props ReliableDeliverySupervisorProps(
            AkkaProtocolHandle handleOrActive,
            Address localAddress,
            Address remoteAddress,
            int? refuseUid,
            AkkaProtocolTransport transport,
            RemoteSettings settings,
            AkkaPduCodec codec,
            ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            string dispatcher)
        {
            return
                Props.Create(
                    () =>
                        new ReliableDeliverySupervisor(handleOrActive, localAddress, remoteAddress, refuseUid, transport,
                            settings, codec, receiveBuffers))
                    .WithDispatcher(dispatcher);
        }

        #endregion

        // Extracted this method to solve a compiler issue with `Receive<TooLongIdle>`
        private void HandleTooLongIdle()
        {
            throw new HopelessAssociation(_localAddress, _remoteAddress, Uid,
                new TimeoutException("Delivery of system messages timed out and they were dropped"));
        }

        private void HandleSend(EndpointManager.Send send)
        {
            if (send.Message is ISystemMessage)
            {
                var sequencedSend = send.Copy(NextSeq());
                TryBuffer(sequencedSend);
                // If we have not confirmed the remote UID we cannot transfer the system message at this point just buffer it.
                // GotUid will kick ResendAll() causing the messages to be properly written.
                // Flow control by not sending more when we already have many outstanding.
                if (UidConfirmed && _resendBuffer.NonAcked.Count <= _settings.SysResendLimit) _writer.Tell(sequencedSend);
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
            _resendBuffer.NonAcked.Take(_settings.SysResendLimit).ForEach(nonacked => _writer.Tell(nonacked));
        }

        private void TryBuffer(EndpointManager.Send s)
        {
            try
            {
                _resendBuffer = _resendBuffer.Buffer(s);
                _bufferWasInUse = true;
            }
            catch (Exception ex)
            {
                throw new HopelessAssociation(_localAddress, _remoteAddress, Uid, ex);
            }
        }

        #region Writer create

        private IActorRef CreateWriter()
        {
            var writer =
                Context.ActorOf(RARP.For(Context.System)
                    .ConfigureDispatcher(
                        EndpointWriter.EndpointWriterProps(_currentHandle, _localAddress, _remoteAddress, _refuseUid, _transport,
                            _settings, new AkkaPduProtobuffCodec(), _receiveBuffers, Self)
                            .WithDeploy(Deploy.Local)),
                    "endpointWriter");
            Context.Watch(writer);
            return writer;
        }

        #endregion

    }

    /// <summary>
    /// Abstract base class for <see cref="EndpointReader"/> classes
    /// </summary>
    internal abstract class EndpointActor : ReceiveActor
    {
        protected readonly Address LocalAddress;
        protected Address RemoteAddress;
        protected RemoteSettings Settings;
        protected AkkaProtocolTransport Transport;

        private readonly ILoggingAdapter _log = Context.GetLogger();

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
        public EndpointWriter(
            AkkaProtocolHandle handleOrActive,
            Address localAddress,
            Address remoteAddress,
            int? refuseUid,
            AkkaProtocolTransport transport,
            RemoteSettings settings,
            AkkaPduCodec codec,
            ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            IActorRef reliableDeliverySupervisor = null) :
                base(localAddress, remoteAddress, transport, settings)
        {
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
                Initializing();
            }
            else
            {
                Writing();
            }
        }

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly int? _refuseUid;
        private readonly AkkaPduCodec _codec;
        private readonly IActorRef _reliableDeliverySupervisor;
        private readonly ActorSystem _system;
        private readonly RemoteActorRefProvider _provider;
        private readonly ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> _receiveBuffers;
        private DisassociateInfo _stopReason = DisassociateInfo.Unknown;

        private IActorRef _reader;
        private readonly AtomicCounter _readerId = new AtomicCounter(0);
        private readonly IInboundMessageDispatcher _msgDispatcher;

        private Ack _lastAck = null;
        private Deadline _ackDeadline;
        private AkkaProtocolHandle _handle;

        private ICancelable _ackIdleTimerCancelable;

        // Use an internal buffer instead of Stash for efficiency
        // stash/unstashAll is slow when many messages are stashed
        // IMPORTANT: sender is not stored, so .Sender and forward must not be used in EndpointWriter
        private readonly LinkedList<object> _buffer = new LinkedList<object>();

        //buffer for IPriorityMessages - ensures that heartbeats get delivered before user-defined messages
        private readonly LinkedList<EndpointManager.Send> _prioBuffer = new LinkedList<EndpointManager.Send>();
        private long _largeBufferLogTimestamp = MonotonicClock.GetNanos();

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
                Transport
                    .Associate(RemoteAddress, _refuseUid)
                    .ContinueWith(handle =>
                    {
                        if (handle.IsFaulted)
                        {
                            var inner = handle.Exception?.Flatten().InnerException;
                            return (object)new Status.Failure(new InvalidAssociationException("Association failure", inner));
                        }
                        return new Handle(handle.Result);
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                    .PipeTo(Self);
            }
            else
            {
                _reader = StartReadEndpoint(_handle);
            }

            var ackIdleInterval = new TimeSpan(Settings.SysMsgAckTimeout.Ticks / 2);
            _ackIdleTimerCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(ackIdleInterval, ackIdleInterval, Self, AckIdleCheckTimer.Instance, Self);
        }

        protected override void PostStop()
        {
            _ackIdleTimerCancelable.CancelIfNotNull();

            foreach (var msg in _prioBuffer)
            {
                _system.DeadLetters.Tell(msg);
            }
            _prioBuffer.Clear();

            foreach (var msg in _buffer)
            {
                _system.DeadLetters.Tell(msg);
            }
            _buffer.Clear();

            if (_handle != null) _handle.Disassociate(_stopReason);
            EventPublisher.NotifyListeners(new DisassociatedEvent(LocalAddress, RemoteAddress, Inbound));
        }

        #endregion

        #region Receives

        private void Initializing()
        {
            Receive<EndpointManager.Send>(send => EnqueueInBuffer(send));
            Receive<Status.Failure>(failure =>
            {
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
            });
            Receive<Handle>(handle =>
            {
                // Assert handle == None?
                Context.Parent.Tell(
                    new ReliableDeliverySupervisor.GotUid((int)handle.ProtocolHandle.HandshakeInfo.Uid));
                _handle = handle.ProtocolHandle;
                _reader = StartReadEndpoint(_handle);
                EventPublisher.NotifyListeners(new AssociatedEvent(LocalAddress, RemoteAddress, Inbound));
                BecomeWritingOrSendBufferedMessages();
            });
        }

        private void Buffering()
        {
            Receive<EndpointManager.Send>(send => EnqueueInBuffer(send));
            Receive<BackoffTimer>(backoff => SendBufferedMessages());
            Receive<FlushAndStop>(stop =>
            {
                _buffer.AddLast(stop); //Flushing is postponed after the pending writes
                Context.System.Scheduler.ScheduleTellOnce(Settings.FlushWait, Self, FlushAndStopTimeout.Instance, Self);
            });
            Receive<FlushAndStopTimeout>(timeout =>
            {
                // enough, ready to flush
                DoFlushAndStop();
            });
        }

        private void Writing()
        {
            Receive<EndpointManager.Send>(s =>
            {
                if (!WriteSend(s))
                {
                    if (s.Seq == null) EnqueueInBuffer(s);
                    ScheduleBackoffTimer();
                    Become(Buffering);
                }
            });
            Receive<FlushAndStop>(flush => DoFlushAndStop());
            Receive<AckIdleCheckTimer>(ack =>
            {
                if (_ackDeadline.IsOverdue)
                {
                    TrySendPureAck();
                }
            });
        }

        private void Handoff()
        {
            Receive<Terminated>(terminated =>
            {
                _reader = StartReadEndpoint(_handle);
                BecomeWritingOrSendBufferedMessages();
            });
            Receive<EndpointManager.Send>(send => EnqueueInBuffer(send));
        }

        protected override void Unhandled(object message)
        {
            if (message is Terminated)
            {
                var t = message as Terminated;
                if (_reader == null || t.ActorRef.Equals(_reader))
                {
                    PublishAndThrow(new EndpointDisassociatedException("Disassociated"), LogLevel.DebugLevel);
                }
            }
            else if (message is StopReading)
            {
                var stop = message as StopReading;
                if (_reader != null)
                {
                    _reader.Tell(stop, stop.ReplyTo);
                }
                else
                {
                    // initializing, buffer and take care of it later when buffer is sent
                    EnqueueInBuffer(message);
                }
            }
            else if (message is TakeOver)
            {
                var takeover = message as TakeOver;

                // Shutdown old reader
                _handle.Disassociate();
                _handle = takeover.ProtocolHandle;
                takeover.ReplyTo.Tell(new TookOver(Self, _handle));
                Become(Handoff);
            }
            else if (message is FlushAndStop)
            {
                _stopReason = DisassociateInfo.Shutdown;
                Context.Stop(Self);
            }
            else if (message is OutboundAck)
            {
                var ack = message as OutboundAck;
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

        private IActorRef StartReadEndpoint(AkkaProtocolHandle handle)
        {
            var newReader =
                Context.ActorOf(RARP.For(Context.System)
                    .ConfigureDispatcher(
                        EndpointReader.ReaderProps(LocalAddress, RemoteAddress, Transport, Settings, _codec, _msgDispatcher,
                            Inbound, (int)handle.HandshakeInfo.Uid, _receiveBuffers, _reliableDeliverySupervisor)
                            .WithDeploy(Deploy.Local)),
                    string.Format("endpointReader-{0}-{1}", AddressUrlEncoder.Encode(RemoteAddress), _readerId.Next()));
            Context.Watch(newReader);
            handle.ReadHandlerSource.SetResult(new ActorHandleEventListener(newReader));
            return newReader;
        }

        /// <summary>
        /// Serializes the outbound message going onto the wire.
        /// </summary>
        /// <param name="msg">The C# object we intend to serialize.</param>
        /// <returns>The Akka.NET envelope containing the serialized message and addressing information.</returns>
        /// <remarks>Differs from JVM implementation due to Scala implicits.</remarks>
        private SerializedMessage SerializeMessage(object msg)
        {
            if (_handle == null)
            {
                throw new EndpointException("Internal error: No handle was present during serialization of outbound message.");
            }
            return MessageSerializer.Serialize(_system, _handle.LocalAddress, msg);
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
                _adaptiveBackoffNanos = Math.Max(Convert.ToInt64(_adaptiveBackoffNanos * 0.9), MinAdaptiveBackoffNanos);
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
                Context.System.Scheduler.ScheduleTellOnce(Settings.BackoffPeriod, Self, BackoffTimer.Instance, Self, null);
            }
            else
            {
                _smallBackoffCount += 1;
                var backoffDeadlineNanoTime = TimeSpan.FromTicks((MonotonicClock.GetNanos() + _adaptiveBackoffNanos).ToTicks());

                Context.System.Scheduler.ScheduleTellOnce(backoffDeadlineNanoTime, Self, BackoffTimer.Instance, Self);
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
                Become(Writing);
            }
            else
            {
                Become(Buffering);
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
                    _log.Debug("RemoteMessage: {0} to [{1}]<+[{2}] from [{3}]", send.Message,
                        send.Recipient, send.Recipient.Path, send.SenderOption ?? _system.DeadLetters);
                }

                var pdu = _codec.ConstructMessage(send.Recipient.LocalAddressToUse, send.Recipient,
                    SerializeMessage(send.Message), send.SenderOption, send.Seq, _lastAck);

                //todo: RemoteMetrics https://github.com/akka/akka/blob/dc0547dd73b54b5de9c3e0b45a21aa865c5db8e2/akka-remote/src/main/scala/akka/remote/Endpoint.scala#L742

                if (pdu.Length > Transport.MaximumPayloadBytes)
                {
                    var reason = new OversizedPayloadException(
                        string.Format("Discarding oversized payload sent to {0}: max allowed size {1} bytes, actual size of encoded {2} was {3} bytes.",
                            send.Recipient,
                            Transport.MaximumPayloadBytes,
                            send.Message.GetType(),
                            pdu.Length));
                    _log.Error(reason, "Transient association error (association remains live)");
                    return true;
                }
                else
                {
                    var ok = _handle.Write(pdu);

                    if (ok)
                    {
                        _ackDeadline = NewAckDeadline();
                        _lastAck = null;
                        return true;
                    }
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
                    return WriteSend(msg as EndpointManager.Send);
                }
                else if (msg is FlushAndStop)
                {
                    DoFlushAndStop();
                    return false;
                }
                else if (msg is StopReading)
                {
                    var s = msg as StopReading;
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
                               "adaptiveBackoff: {4}", _maxWriteCount, _fullBackoffCount, _smallBackoffCount, _noBackoffCount, _adaptiveBackoffNanos / 1000);
                }
                _fullBackoffCount = 1;
                _smallBackoffCount = 0;
                _noBackoffCount = 0;
                _writeCount = 0;
                _maxWriteCount = MaxWriteCount;
                Become(Writing);
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
                    var now = MonotonicClock.GetNanos();
                    if (now - _largeBufferLogTimestamp >= LogBufferSizeInterval)
                    {
                        _log.Warning("[{0}] buffered messages in EndpointWriter for [{1}]. You should probably implement flow control to avoid flooding the remote connection.", size, RemoteAddress);
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
            AkkaPduCodec codec, ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers, IActorRef reliableDeliverySupervisor = null)
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
        public sealed class TakeOver : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// Create a new TakeOver command
            /// </summary>
            /// <param name="protocolHandle">The handle of the new association</param>
            /// <param name="replyTo"></param>
            public TakeOver(AkkaProtocolHandle protocolHandle, IActorRef replyTo)
            {
                ProtocolHandle = protocolHandle;
                ReplyTo = replyTo;
            }

            public AkkaProtocolHandle ProtocolHandle { get; private set; }

            public IActorRef ReplyTo { get; private set; }
        }

        public sealed class TookOver : INoSerializationVerificationNeeded
        {
            public TookOver(IActorRef writer, AkkaProtocolHandle protocolHandle)
            {
                ProtocolHandle = protocolHandle;
                Writer = writer;
            }

            public IActorRef Writer { get; private set; }

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

        private sealed class FlushAndStopTimeout
        {
            private FlushAndStopTimeout() { }
            private static readonly FlushAndStopTimeout _instance = new FlushAndStopTimeout();
            public static FlushAndStopTimeout Instance { get { return _instance; } }
        }

        public sealed class Handle : INoSerializationVerificationNeeded
        {
            public Handle(AkkaProtocolHandle protocolHandle)
            {
                ProtocolHandle = protocolHandle;
            }

            public AkkaProtocolHandle ProtocolHandle { get; private set; }
        }

        public sealed class StopReading
        {
            public StopReading(IActorRef writer, IActorRef replyTo)
            {
                Writer = writer;
                ReplyTo = replyTo;
            }

            public IActorRef Writer { get; private set; }

            public IActorRef ReplyTo { get; private set; }
        }

        public sealed class StoppedReading
        {
            public StoppedReading(IActorRef writer)
            {
                Writer = writer;
            }

            public IActorRef Writer { get; private set; }
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
        public EndpointReader(
            Address localAddress, 
            Address remoteAddress, 
            AkkaProtocolTransport transport,
            RemoteSettings settings, 
            AkkaPduCodec codec, 
            IInboundMessageDispatcher msgDispatch, 
            bool inbound,
            int uid, 
            ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            IActorRef reliableDeliverySupervisor = null) :
            base(localAddress, remoteAddress, transport, settings)
        {
            _receiveBuffers = receiveBuffers;
            _msgDispatch = msgDispatch;
            Inbound = inbound;
            _uid = uid;
            _reliableDeliverySupervisor = reliableDeliverySupervisor;
            _codec = codec;
            _provider = RARP.For(Context.System).Provider;
            Reading();
        }

        private readonly ILoggingAdapter _log = Context.GetLogger();
        private readonly AkkaPduCodec _codec;
        private readonly IActorRef _reliableDeliverySupervisor;
        private readonly ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> _receiveBuffers;
        private readonly int _uid;
        private readonly IInboundMessageDispatcher _msgDispatch;

        private readonly RemoteActorRefProvider _provider;
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

        private void Reading()
        {
            Receive<Disassociated>(disassociated => HandleDisassociated(disassociated.Info));
            Receive<InboundPayload>(inbound =>
            {
                var payload = inbound.Payload;
                if (payload.Length > Transport.MaximumPayloadBytes)
                {
                    var reason = new OversizedPayloadException(
                        string.Format("Discarding oversized payload received: max allowed size {0} bytes, actual size {1} bytes.",
                            Transport.MaximumPayloadBytes,
                            payload.Length));
                    _log.Error(reason, "Transient error while reading from association (association remains live)");
                }
                else
                {
                    var ackAndMessage = TryDecodeMessageAndAck(payload);
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
            });
            Receive<EndpointWriter.StopReading>(stop =>
            {
                SaveState();
                Become(NotReading);
                stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer));
            });
        }

        private void NotReading()
        {
            Receive<Disassociated>(disassociated => HandleDisassociated(disassociated.Info));
            Receive<EndpointWriter.StopReading>(stop => stop.ReplyTo.Tell(new EndpointWriter.StoppedReading(stop.Writer)));
            Receive<InboundPayload>(payload =>
            {
                var ackAndMessage = TryDecodeMessageAndAck(payload.Payload);
                if (ackAndMessage.AckOption != null && _reliableDeliverySupervisor != null)
                    _reliableDeliverySupervisor.Tell(ackAndMessage.AckOption);
            });
            ReceiveAny(o => {}); // ignore
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
            while (true)
            {
                if (expectedState == null)
                {
                    if (_receiveBuffers.ContainsKey(key))
                    {
                        var updatedValue = new EndpointManager.ResendState(_uid, _ackedReceiveBuffer);
                        _receiveBuffers.AddOrUpdate(key, updatedValue, (link, state) => updatedValue);
                        expectedState = updatedValue;
                        continue;
                    }
                }
                else
                {
                    var canReplace = _receiveBuffers.ContainsKey(key) && _receiveBuffers[key].Equals(expectedState);
                    if (canReplace)
                    {
                        _receiveBuffers[key] = Merge(new EndpointManager.ResendState(_uid, _ackedReceiveBuffer), expectedState);
                    }
                    else
                    {
                        EndpointManager.ResendState previousValue;
                        _receiveBuffers.TryGetValue(key, out previousValue);
                        expectedState = previousValue;
                        continue;
                    }
                }
                break;
            }
        }

        private void HandleDisassociated(DisassociateInfo info)
        {
            switch (info)
            {
                case DisassociateInfo.Quarantined:
                    throw new InvalidAssociation(LocalAddress, RemoteAddress, new InvalidAssociationException("The remote system has quarantined this system. No further associations " +
                                                                                                              "to the remote system are possible until this system is restarted."), DisassociateInfo.Quarantined);
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

        public static Props ReaderProps(
            Address localAddress, 
            Address remoteAddress, 
            AkkaProtocolTransport transport,
            RemoteSettings settings, 
            AkkaPduCodec codec, 
            IInboundMessageDispatcher dispatcher, 
            bool inbound, 
            int uid,
            ConcurrentDictionary<EndpointManager.Link, EndpointManager.ResendState> receiveBuffers,
            IActorRef reliableDeliverySupervisor = null)
        {
            return
                Props.Create(
                    () =>
                        new EndpointReader(localAddress, remoteAddress, transport, settings, codec, dispatcher, inbound,
                            uid, receiveBuffers, reliableDeliverySupervisor))
                            .WithDispatcher(settings.Dispatcher);
        }

        #endregion
    }
}

