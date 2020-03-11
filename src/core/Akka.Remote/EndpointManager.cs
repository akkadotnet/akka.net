//-----------------------------------------------------------------------
// <copyright file="EndpointManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Dispatch;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Util.Internal;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class EndpointManager : ReceiveActor, IRequiresMessageQueue<IUnboundedMessageQueueSemantics>
    {

        #region Policy definitions

        /// <summary>
        /// Defines how we're going to treat incoming and outgoing connections for specific endpoints
        /// </summary>
        public abstract class EndpointPolicy
        {
            /// <summary>
            /// Indicates that the policy does not contain an active endpoint, but it is a tombstone of a previous failure
            /// </summary>
            public readonly bool IsTombstone;

            protected EndpointPolicy(bool isTombstone)
            {
                IsTombstone = isTombstone;
            }
        }

        /// <summary>
        /// We will always accept a connection from this remote node.
        /// </summary>
        public sealed class Pass : EndpointPolicy
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="endpoint">TBD</param>
            /// <param name="uid">TBD</param>
            public Pass(IActorRef endpoint, int? uid)
                : base(false)
            {
                Uid = uid;
                Endpoint = endpoint;
            }

            /// <summary>
            /// The actor who owns the current endpoint
            /// </summary>
            public IActorRef Endpoint { get; }

            /// <summary>
            /// The endpoint UID, if it's currently known
            /// </summary>
            public int? Uid { get; }
        }

        /// <summary>
        /// A Gated node can't be connected to from this process for <see cref="TimeOfRelease"/>,
        /// but we may accept an inbound connection from it if the remote node recovers on its own.
        /// </summary>
        public sealed class Gated : EndpointPolicy
        {
            public Gated(Deadline deadline)
                : base(true)
            {
                TimeOfRelease = deadline;
            }

            public Deadline TimeOfRelease { get; }
        }

        /// <summary>
        /// We do not accept connection attempts for a quarantined node until it restarts and resets its UID.
        /// </summary>
        public sealed class Quarantined : EndpointPolicy
        {
            public Quarantined(int uid, Deadline deadline)
                : base(true)
            {
                Uid = uid;
                Deadline = deadline;
            }

            public int Uid { get; }

            public Deadline Deadline { get; }
        }

        #endregion

        #region RemotingCommands and operations

        /// <summary>
        /// Messages sent between <see cref="Remoting"/> and <see cref="EndpointManager"/>
        /// </summary>
        public abstract class RemotingCommand : INoSerializationVerificationNeeded { }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Listen : RemotingCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="addressesPromise">TBD</param>
            public Listen(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise)
            {
                AddressesPromise = addressesPromise;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class StartupFinished : RemotingCommand { }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ShutdownAndFlush : RemotingCommand { }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Send : RemotingCommand, IHasSequenceNumber
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="message">TBD</param>
            /// <param name="recipient">TBD</param>
            /// <param name="senderOption">TBD</param>
            /// <param name="seqOpt">TBD</param>
            public Send(object message, RemoteActorRef recipient, IActorRef senderOption = null, SeqNo seqOpt = null)
            {
                Recipient = recipient;
                SenderOption = senderOption;
                Message = message;
                _seq = seqOpt;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public object Message { get; private set; }

            /// <summary>
            /// Can be null!
            /// </summary>
            public IActorRef SenderOption { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public RemoteActorRef Recipient { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            /// <returns>TBD</returns>
            public override string ToString()
            {
                return string.Format("Remote message {0} -> {1}", SenderOption, Recipient);
            }

            private readonly SeqNo _seq;

            /// <summary>
            /// TBD
            /// </summary>
            public SeqNo Seq
            {
                get
                {
                    return _seq;
                }
            }

            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="opt">TBD</param>
            /// <returns>TBD</returns>
            public Send Copy(SeqNo opt)
            {
                return new Send(Message, Recipient, SenderOption, opt);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Quarantine : RemotingCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="remoteAddress">TBD</param>
            /// <param name="uid">TBD</param>
            public Quarantine(Address remoteAddress, int? uid)
            {
                Uid = uid;
                RemoteAddress = remoteAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Address RemoteAddress { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public int? Uid { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ManagementCommand : RemotingCommand
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="cmd">TBD</param>
            public ManagementCommand(object cmd)
            {
                Cmd = cmd;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public object Cmd { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ManagementCommandAck
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="status">TBD</param>
            public ManagementCommandAck(bool status)
            {
                Status = status;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public bool Status { get; private set; }
        }

        #endregion

        #region Messages internal to EndpointManager

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class Prune : INoSerializationVerificationNeeded { }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ListensResult : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="addressesPromise">TBD</param>
            /// <param name="results">TBD</param>
            public ListensResult(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise, List<(ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>)> results)
            {
                Results = results;
                AddressesPromise = addressesPromise;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public IList<(ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>)> Results
            { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ListensFailure : INoSerializationVerificationNeeded
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="addressesPromise">TBD</param>
            /// <param name="cause">TBD</param>
            public ListensFailure(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise, Exception cause)
            {
                Cause = cause;
                AddressesPromise = addressesPromise;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public Exception Cause { get; private set; }
        }

        /// <summary>
        /// Helper class to store address pairs
        /// </summary>
        public sealed class Link
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="localAddress">TBD</param>
            /// <param name="remoteAddress">TBD</param>
            public Link(Address localAddress, Address remoteAddress)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public Address LocalAddress { get; }

            /// <summary>
            /// TBD
            /// </summary>
            public Address RemoteAddress { get; }

            /// <summary>
            /// Overrode this to make sure that the <see cref="ReliableDeliverySupervisor"/> can correctly store
            /// <see cref="AckedReceiveBuffer{T}"/> data for each <see cref="Link"/> individually, since the HashCode
            /// is what Dictionary types use internally for equality checking by default.
            /// </summary>
            /// <returns>TBD</returns>
            public override int GetHashCode()
            {
                unchecked
                {
                    return (LocalAddress.GetHashCode() * 397) ^ RemoteAddress.GetHashCode();
                }
            }

            private bool Equals(Link other)
            {
                return LocalAddress.Equals(other.LocalAddress) && RemoteAddress.Equals(other.RemoteAddress);
            }

            public override bool Equals(object obj)
            {
                return ReferenceEquals(this, obj) || obj is Link other && Equals(other);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public sealed class ResendState : IEquatable<ResendState>
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="uid">TBD</param>
            /// <param name="buffer">TBD</param>
            public ResendState(int uid, AckedReceiveBuffer<Message> buffer)
            {
                Buffer = buffer;
                Uid = uid;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public int Uid { get; private set; }

            /// <summary>
            /// TBD
            /// </summary>
            public AckedReceiveBuffer<Message> Buffer { get; private set; }

            public bool Equals(ResendState other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return Uid == other.Uid && Buffer.Equals(other.Buffer);
            }

            public override bool Equals(object obj)
            {
                return ReferenceEquals(this, obj) || obj is ResendState other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Uid * 397) ^ Buffer.GetHashCode();
                }
            }
        }

        #endregion

        /// <summary>
        /// Creates a new <see cref="EndpointManager"/> instance.
        /// </summary>
        /// <param name="config">The HOCON configuration for the current <see cref="ActorSystem"/>.</param>
        /// <param name="log">The "remoting" logging source.</param>
        public EndpointManager(Config config, ILoggingAdapter log)
        {
            _conf = config;
            _settings = new RemoteSettings(_conf);
            _log = log;
            _eventPublisher = new EventPublisher(Context.System, log, Logging.LogLevelFor(_settings.RemoteLifecycleEventsLogLevel));

            Receiving();
        }

        /// <summary>
        /// Mapping between addresses and endpoint actors. If passive connections are turned off, incoming connections
        /// will not be part of this map!
        /// </summary>
        private readonly EndpointRegistry _endpoints = new EndpointRegistry();
        private readonly RemoteSettings _settings;
        private readonly Config _conf;
        private readonly AtomicCounterLong _endpointId = new AtomicCounterLong(0L);
        private readonly ILoggingAdapter _log;
        private readonly EventPublisher _eventPublisher;

        /// <summary>
        /// Used to indicate when an abrupt shutdown occurs
        /// </summary>
        private bool _normalShutdown = false;

        /// <summary>
        /// Mapping between transports and the local addresses they listen to
        /// </summary>
        private Dictionary<Address, AkkaProtocolTransport> _transportMapping =
            new Dictionary<Address, AkkaProtocolTransport>();

        private readonly ConcurrentDictionary<Link, ResendState> _receiveBuffers = new ConcurrentDictionary<Link, ResendState>();

        private bool RetryGateEnabled => _settings.RetryGateClosedFor > TimeSpan.Zero;

        private TimeSpan PruneInterval
        {
            get
            {
                //PruneInterval = 2x the RetryGateClosedFor value, if available
                if (RetryGateEnabled) return _settings.RetryGateClosedFor.Add(_settings.RetryGateClosedFor).Max(TimeSpan.FromSeconds(1)).Min(TimeSpan.FromSeconds(10));
                else return TimeSpan.Zero;
            }
        }

        private ICancelable _pruneTimeCancelable;

        /// <summary>
        /// Cancelable for terminating <see cref="Prune"/> operations.
        /// </summary>
        private ICancelable PruneTimerCancelleable
        {
            get
            {
                if (RetryGateEnabled && _pruneTimeCancelable == null)
                {
                    return _pruneTimeCancelable = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(PruneInterval, PruneInterval, Self, new Prune(), Self);
                }
                return _pruneTimeCancelable;
            }
        }

        private Dictionary<IActorRef, AkkaProtocolHandle> _pendingReadHandoffs = new Dictionary<IActorRef, AkkaProtocolHandle>();
        private Dictionary<IActorRef, List<InboundAssociation>> _stashedInbound = new Dictionary<IActorRef, List<InboundAssociation>>();


        private void HandleStashedInbound(IActorRef endpoint, bool writerIsIdle)
        {
            var stashed = _stashedInbound.GetOrElse(endpoint, new List<InboundAssociation>());
            _stashedInbound.Remove(endpoint);
            foreach (var ia in stashed)
                HandleInboundAssociation(ia, writerIsIdle);
        }

        private void KeepQuarantinedOr(Address remoteAddress, Action body)
        {
            var uid = _endpoints.RefuseUid(remoteAddress);
            if (uid.HasValue)
            {
                _log.Info(
                    "Quarantined address [{0}] is still unreachable or has not been restarted. Keeping it quarantined.",
                    remoteAddress);
                // Restoring Quarantine marker overwritten by a Pass(endpoint, refuseUid) pair while probing remote system.
                _endpoints.MarkAsQuarantined(remoteAddress, uid.Value, Deadline.Now + _settings.QuarantineDuration);
            }
            else
            {
                body();
            }
        }


        #region ActorBase overrides

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                Directive Hopeless(HopelessAssociation e)
                {
                    switch (e)
                    {
                        case HopelessAssociation h when h.Uid != null:
                            _log.Error(e.InnerException ?? e, "Association to [{0}] with UID [{1}] is irrecoverably failed. Quarantining address.", h.RemoteAddress, h.Uid);
                            if (_settings.QuarantineDuration.HasValue && _settings.QuarantineDuration != TimeSpan.MaxValue)
                            {
                                // have a finite quarantine duration specified in settings.
                                // If we don't have one specified, don't bother quarantining - it's disabled.
                                _endpoints.MarkAsQuarantined(h.RemoteAddress, h.Uid.Value, Deadline.Now + _settings.QuarantineDuration);
                                _eventPublisher.NotifyListeners(new QuarantinedEvent(h.RemoteAddress, h.Uid.Value));
                            }

                            return Directive.Stop;
                        default: // no UID found
                            _log.Warning("Association to [{0}] with unknown UID is irrecoverably failed. Address cannot be quarantined without knowing the UID, gating instead for {1} ms.",
                                e.RemoteAddress, _settings.RetryGateClosedFor.TotalMilliseconds);
                            _endpoints.MarkAsFailed(Sender, Deadline.Now + _settings.RetryGateClosedFor);
                            return Directive.Stop;
                    }
                }

                switch (ex)
                {
                    case InvalidAssociation ia:
                        KeepQuarantinedOr(ia.RemoteAddress, () =>
                        {
                            var causedBy = ia.InnerException == null
                                ? ""
                                : $"Caused by: [{ia.InnerException}]";
                            _log.Warning("Tried to associate with unreachable remote address [{0}]. Address is now gated for {1} ms, all messages to this address will be delivered to dead letters. Reason: [{2}] {3}",
                                ia.RemoteAddress, _settings.RetryGateClosedFor.TotalMilliseconds, ia.Message, causedBy);
                            _endpoints.MarkAsFailed(Sender, Deadline.Now + _settings.RetryGateClosedFor);
                        });

                        if (ia.DisassociationInfo.HasValue && ia.DisassociationInfo == DisassociateInfo.Quarantined)
                            Context.System.EventStream.Publish(new ThisActorSystemQuarantinedEvent(ia.LocalAddress, ia.RemoteAddress));
                        return Directive.Stop;
                    case ShutDownAssociation shutdown:
                        KeepQuarantinedOr(shutdown.RemoteAddress, () =>
                        {
                            _log.Debug("Remote system with address [{0}] has shut down. Address is now gated for {1}ms, all messages to this address will be delivered to dead letters.",
                                shutdown.RemoteAddress, _settings.RetryGateClosedFor.TotalMilliseconds);
                            _endpoints.MarkAsFailed(Sender, Deadline.Now + _settings.RetryGateClosedFor);
                        });
                        return Directive.Stop;
                    case HopelessAssociation h:
                        return Hopeless(h);
                    case ActorInitializationException i when i.InnerException is HopelessAssociation h2:
                        return Hopeless(h2);
                    default:
                        if (ex is EndpointDisassociatedException || ex is EndpointAssociationException) { } //no logging
                        else { _log.Error(ex, ex.Message); }
                        _endpoints.MarkAsFailed(Sender, Deadline.Now + _settings.RetryGateClosedFor);
                        return Directive.Stop;
                }
            }, false);
        }

        protected override void PreStart()
        {
            if (PruneTimerCancelleable != null)
                _log.Debug("Starting prune timer for endpoint manager...");
            base.PreStart();
        }

        protected override void PostStop()
        {
            if (PruneTimerCancelleable != null)
                _pruneTimeCancelable.Cancel();
            foreach (var h in _pendingReadHandoffs.Values)
                h.Disassociate(DisassociateInfo.Shutdown);

            if (!_normalShutdown)
            {
                // Remaining running endpoints are children, so they will clean up themselves.
                // We still need to clean up any remaining transports because handles might be in mailboxes, and for example
                // DotNetty is not part of the actor hierarchy, so its handles will not be cleaned up if no actor is taking
                // responsibility of them (because they are sitting in a mailbox).
                _log.Error("Remoting system has been terminated abruptly. Attempting to shut down transports");
                foreach (var t in _transportMapping.Values)
                    t.Shutdown();
            }
        }

        private void Receiving()
        {
            /*
            * the first command the EndpointManager receives.
            * instructs the EndpointManager to fire off its "Listens" command, which starts
            * up all inbound transports and binds them to specific addresses via configuration.
            * those results will then be piped back to Remoting, who waits for the results of
            * listen.AddressPromise.
            * */
            Receive<Listen>(listen =>
            {
                Listens.ContinueWith<INoSerializationVerificationNeeded>(listens =>
                {
                    if (listens.IsFaulted)
                    {
                        return new ListensFailure(listen.AddressesPromise, listens.Exception);
                    }
                    else
                    {
                        return new ListensResult(listen.AddressesPromise, listens.Result);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                    .PipeTo(Self);
            });

            Receive<ListensResult>(listens =>
            {
                _transportMapping = (from mapping in listens.Results
                                     group mapping by mapping.Item1.Address
                                           into g
                                     select new { address = g.Key, transports = g.ToList() }).Select(x =>
                                     {
                                         if (x.transports.Count > 1)
                                         {
                                             throw new RemoteTransportException(
                                                 $"There are more than one transports listening on local address {x.address}");
                                         }
                                         return new KeyValuePair<Address, AkkaProtocolTransport>(x.address,
                                             x.transports.Head().Item1.ProtocolTransport);
                                     }).ToDictionary(x => x.Key, v => v.Value);

                //Register a listener to each transport and collect mapping to addresses
                var transportsAndAddresses = listens.Results.Select(x =>
                {
                    x.Item2.SetResult(new ActorAssociationEventListener(Self));
                    return x.Item1;
                }).ToList();

                listens.AddressesPromise.SetResult(transportsAndAddresses);
            });
            Receive<ListensFailure>(failure => failure.AddressesPromise.SetException(failure.Cause));

            // defer the inbound association until we can enter "Accepting" behavior

            Receive<InboundAssociation>(
                ia => Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(10), Self, ia, Self));
            Receive<ManagementCommand>(mc => Sender.Tell(new ManagementCommandAck(status: false)));
            Receive<StartupFinished>(sf => Become(Accepting));
            Receive<ShutdownAndFlush>(sf =>
             {
                 Sender.Tell(true);
                 Context.Stop(Self);
             });
        }

        /// <summary>
        /// Message-processing behavior when the <see cref="EndpointManager"/> is able to accept
        /// inbound association requests.
        /// </summary>
        protected void Accepting()
        {
            Receive<ManagementCommand>(mc =>
                {
                    /*
                     * applies a management command to all available transports.
                     * 
                     * Useful for things like global restart 
                     */
                    var sender = Sender;
                    var allStatuses = _transportMapping.Values.Select(x => x.ManagementCommand(mc.Cmd));
                    Task.WhenAll(allStatuses)
                        .ContinueWith(x =>
                        {
                            return new ManagementCommandAck(x.Result.All(y => y));
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                        .PipeTo(sender);
                });

            Receive<Quarantine>(quarantine =>
            {
                //Stop writers
                var policy =
                (_endpoints.WritableEndpointWithPolicyFor(quarantine.RemoteAddress), quarantine.Uid);
                if (policy.Item1 is Pass pass && policy.Item2 == null)
                {
                    var endpoint = pass.Endpoint;
                    Context.Stop(endpoint);
                    _log.Warning("Association to [{0}] with unknown UID is reported as quarantined, but " +
                    "address cannot be quarantined without knowing the UID, gating instead for {1} ms.", quarantine.RemoteAddress, _settings.RetryGateClosedFor.TotalMilliseconds);
                    _endpoints.MarkAsFailed(endpoint, Deadline.Now + _settings.RetryGateClosedFor);
                }
                else if (policy.Item1 is Pass p && policy.Item2 != null)
                {
                    var uidOption = p.Uid;
                    var quarantineUid = policy.Item2;
                    if (uidOption == quarantineUid)
                    {
                        _endpoints.MarkAsQuarantined(quarantine.RemoteAddress, quarantineUid.Value, Deadline.Now + _settings.QuarantineDuration);
                        _eventPublisher.NotifyListeners(new QuarantinedEvent(quarantine.RemoteAddress, quarantineUid.Value));
                        Context.Stop(p.Endpoint);
                    }
                    // or it does not match with the UID to be quarantined
                    else if (!uidOption.HasValue && _endpoints.RefuseUid(quarantine.RemoteAddress) != quarantineUid)
                    {
                        // the quarantine uid may be got fresh by cluster gossip, so update refuseUid for late handle when the writer got uid
                        _endpoints.RegisterWritableEndpointRefuseUid(quarantine.RemoteAddress, quarantineUid.Value, Deadline.Now + _settings.QuarantineDuration);
                    }
                    else
                    {
                        //the quarantine uid has lost the race with some failure, do nothing
                    }
                }
                else if (policy.Item1 is Quarantined && policy.Item2 != null && policy.Item1.AsInstanceOf<Quarantined>().Uid == policy.Item2.Value)
                {
                    // the UID to be quarantined already exists, do nothing
                }
                else if (policy.Item2 != null)
                {
                    // the current state is gated or quarantined, and we know the UID, update
                    _endpoints.MarkAsQuarantined(quarantine.RemoteAddress, policy.Item2.Value, Deadline.Now + _settings.QuarantineDuration);
                    _eventPublisher.NotifyListeners(new QuarantinedEvent(quarantine.RemoteAddress, policy.Item2.Value));
                }
                else
                {
                    // the current state is Gated, WasGated, or Quarantined and we don't know the UID, do nothing.
                }

                // Stop inbound read-only associations
                var readPolicy = (_endpoints.ReadOnlyEndpointFor(quarantine.RemoteAddress), quarantine.Uid);
                if (readPolicy.Item1?.Item1 != null && quarantine.Uid == null)
                    Context.Stop(readPolicy.Item1.Value.Item1);
                else if (readPolicy.Item1?.Item1 != null && quarantine.Uid != null && readPolicy.Item1?.Item2 == quarantine.Uid) { Context.Stop(readPolicy.Item1.Value.Item1); }
                else { } // nothing to stop

                bool MatchesQuarantine(AkkaProtocolHandle handle)
                {

                    return handle.RemoteAddress.Equals(quarantine.RemoteAddress) &&
                           quarantine.Uid == handle.HandshakeInfo.Uid;
                }

                // Stop all matching pending read handoffs
                _pendingReadHandoffs = _pendingReadHandoffs.Where(x =>
                {
                    var drop = MatchesQuarantine(x.Value);
                    // Side-effecting here
                    if (drop)
                    {
                        x.Value.Disassociate();
                        Context.Stop(x.Key);
                    }
                    return !drop;
                }).ToDictionary(key => key.Key, value => value.Value);

                // Stop all matching stashed connections
                _stashedInbound = _stashedInbound.Select(x =>
                {
                    var associations = x.Value.Where(assoc =>
                    {
                        var handle = assoc.Association.AsInstanceOf<AkkaProtocolHandle>();
                        var drop = MatchesQuarantine(handle);
                        if (drop)
                            handle.Disassociate();
                        return !drop;
                    }).ToList();
                    return new KeyValuePair<IActorRef, List<InboundAssociation>>(x.Key, associations);
                }).ToDictionary(k => k.Key, v => v.Value);

            });

            Receive<Send>(send =>
            {
                var recipientAddress = send.Recipient.Path.Address;
                IActorRef CreateAndRegisterWritingEndpoint() => _endpoints.RegisterWritableEndpoint(recipientAddress, 
                    CreateEndpoint(recipientAddress, send.Recipient.LocalAddressToUse, _transportMapping[send.Recipient.LocalAddressToUse], 
                        _settings, writing: true, handleOption: null), uid: null);

                // pattern match won't throw a NullReferenceException if one is returned by WritableEndpointWithPolicyFor
                switch (_endpoints.WritableEndpointWithPolicyFor(recipientAddress))
                {
                    case Pass pass:
                        pass.Endpoint.Tell(send);
                        break;
                    case Gated gated:
                        if (gated.TimeOfRelease.IsOverdue) CreateAndRegisterWritingEndpoint().Tell(send);
                        else Context.System.DeadLetters.Tell(send);
                        break;
                    case Quarantined quarantined:
                        // timeOfRelease is only used for garbage collection reasons, therefore it is ignored here. We still have
                        // the Quarantined tombstone and we know what UID we don't want to accept, so use it.
                        CreateAndRegisterWritingEndpoint().Tell(send);
                        break;
                    default:
                        CreateAndRegisterWritingEndpoint().Tell(send);
                        break;
                }
            });
            Receive<InboundAssociation>(ia => HandleInboundAssociation(ia, false));
            Receive<EndpointWriter.StoppedReading>(endpoint => AcceptPendingReader(endpoint.Writer));
            Receive<Terminated>(terminated =>
            {
                AcceptPendingReader(terminated.ActorRef);
                _endpoints.UnregisterEndpoint(terminated.ActorRef);
                HandleStashedInbound(terminated.ActorRef, writerIsIdle: false);
            });
            Receive<EndpointWriter.TookOver>(tookover => RemovePendingReader(tookover.Writer, tookover.ProtocolHandle));
            Receive<ReliableDeliverySupervisor.GotUid>(gotuid =>
            {
                var refuseUidOption = _endpoints.RefuseUid(gotuid.RemoteAddress);
                var policy = _endpoints.WritableEndpointWithPolicyFor(gotuid.RemoteAddress);
                switch (policy)
                {
                    case Pass pass:
                        if (refuseUidOption == gotuid.Uid)
                        {
                            _endpoints.MarkAsQuarantined(gotuid.RemoteAddress, gotuid.Uid,
                                Deadline.Now + _settings.QuarantineDuration);
                            _eventPublisher.NotifyListeners(new QuarantinedEvent(gotuid.RemoteAddress, gotuid.Uid));
                            Context.Stop(pass.Endpoint);
                        }
                        else
                        {
                            _endpoints.RegisterWritableEndpointUid(gotuid.RemoteAddress, gotuid.Uid);
                        }
                        HandleStashedInbound(Sender, writerIsIdle: false);
                        break;
                    default:
                        // the GotUid might have lost the race with some failure
                        break;
                }
            });
            Receive<ReliableDeliverySupervisor.Idle>(idle =>
            {
                HandleStashedInbound(Sender, writerIsIdle: true);
            });
            Receive<Prune>(prune => _endpoints.Prune());
            Receive<ShutdownAndFlush>(shutdown =>
            {
                //Shutdown all endpoints and signal to Sender when ready (and whether all endpoints were shutdown gracefully)
                var sender = Sender;

                // The construction of the Task for shutdownStatus has to happen after the flushStatus future has been finished
                // so that endpoints are shut down before transports.
                var shutdownStatus = Task.WhenAll(_endpoints.AllEndpoints.Select(
                    x => x.GracefulStop(_settings.FlushWait, EndpointWriter.FlushAndStop.Instance))).ContinueWith(
                        result =>
                        {
                            if (result.IsFaulted || result.IsCanceled)
                            {
                                if (result.Exception != null)
                                    result.Exception.Handle(e => true);
                                return false;
                            }
                            return result.Result.All(x => x);
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

                shutdownStatus.ContinueWith(tr => Task.WhenAll(_transportMapping.Values.Select(x => x.Shutdown())).ContinueWith(
                          result =>
                          {
                              if (result.IsFaulted || result.IsCanceled)
                              {
                                  if (result.Exception != null)
                                      result.Exception.Handle(e => true);
                                  return false;
                              }
                              return result.Result.All(x => x) && tr.Result;
                          }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)).Unwrap().PipeTo(sender);


                foreach (var handoff in _pendingReadHandoffs.Values)
                {
                    handoff.Disassociate(DisassociateInfo.Shutdown);
                }

                //Ignore all other writes
                _normalShutdown = true;
                Become(Flushing);
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected void Flushing()
        {
            Receive<Send>(send => Context.System.DeadLetters.Tell(send));
            Receive<InboundAssociation>(
                     ia => ia.Association.AsInstanceOf<AkkaProtocolHandle>().Disassociate(DisassociateInfo.Shutdown));
            Receive<Terminated>(terminated => { }); // why should we care now?
        }

        #endregion

        #region Internal methods

        private void HandleInboundAssociation(InboundAssociation ia, bool writerIsIdle)
        {
            var readonlyEndpoint = _endpoints.ReadOnlyEndpointFor(ia.Association.RemoteAddress);
            var handle = ((AkkaProtocolHandle)ia.Association);
            if (readonlyEndpoint != null)
            {
                var endpoint = readonlyEndpoint.Value.Item1;
                if (_pendingReadHandoffs.TryGetValue(endpoint, out var protocolHandle))
                    protocolHandle.Disassociate("the existing readOnly association was replaced by a new incoming one", _log);

                _pendingReadHandoffs[endpoint] = handle;
                endpoint.Tell(new EndpointWriter.TakeOver(handle, Self));
                switch (_endpoints.WritableEndpointWithPolicyFor(handle.RemoteAddress))
                {
                    case Pass pass:
                        pass.Endpoint.Tell(new ReliableDeliverySupervisor.Ungate());
                        break;
                }
            }
            else
            {
                if (_endpoints.IsQuarantined(handle.RemoteAddress, handle.HandshakeInfo.Uid))
                    handle.Disassociate(DisassociateInfo.Quarantined);
                else
                {
                    var policy = _endpoints.WritableEndpointWithPolicyFor(handle.RemoteAddress);
                    switch (policy)
                    {
                        case Pass pass when !pass.Uid.HasValue: // pass, but UID is unknown
                            // Idle writer will never send a GotUid or a Terminated so we need to "provoke it"
                            // to get an unstash event
                            if (!writerIsIdle)
                            {
                                pass.Endpoint.Tell(ReliableDeliverySupervisor.IsIdle.Instance);

                                var stashedInboundForEp = _stashedInbound.GetOrElse(pass.Endpoint,
                                    new List<InboundAssociation>());
                                stashedInboundForEp.Add(ia);
                                _stashedInbound[pass.Endpoint] = stashedInboundForEp;
                            }
                            else
                                CreateAndRegisterEndpoint(handle);
                            break;
                        case Pass pWithUid: // pass with known UID
                            if (handle.HandshakeInfo.Uid == pWithUid.Uid)
                            {
                                _pendingReadHandoffs.GetOrElse(pWithUid.Endpoint, null)?.Disassociate("the existing writable association was replaced by a new incoming one", _log);
                                _pendingReadHandoffs[pWithUid.Endpoint] = handle;
                                pWithUid.Endpoint.Tell(new EndpointWriter.StopReading(pWithUid.Endpoint, Self));
                                pWithUid.Endpoint.Tell(new ReliableDeliverySupervisor.Ungate());
                            }
                            else
                            {
                                Context.Stop(pWithUid.Endpoint);
                                _endpoints.UnregisterEndpoint(pWithUid.Endpoint);
                                _pendingReadHandoffs.Remove(pWithUid.Endpoint);
                                _endpoints.MarkAsQuarantined(handle.RemoteAddress, pWithUid.Uid.Value, Deadline.Now + _settings.QuarantineDuration);
                                CreateAndRegisterEndpoint(handle);
                            }

                            break;
                        default:
                            CreateAndRegisterEndpoint(handle);
                            break;
                    }
                }
            }
        }

        private Task<List<(ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>)>>
            _listens;
        private Task<List<(ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>)>>
            Listens
        {
            get
            {
                if (_listens == null)
                {
                    /*
                 * Constructs chains of adapters on top of each driven given in configuration. The result structure looks like the following:
                 * 
                 *      AkkaProtocolTransport <-- Adapter <-- ... <-- Adapter <-- Driver
                 * 
                 * The transports variable contains only the heads of each chains (the AkkaProtocolTransport instances)
                 */
                    var transports = new List<AkkaProtocolTransport>();
                    foreach (var transportSettings in _settings.Transports)
                    {
                        var args = new object[] { Context.System, transportSettings.Config };

                        //Loads the driver -- the bottom element of the chain
                        //The chain at this point:
                        //  Driver
                        Transport.Transport driver;
                        try
                        {
                            var driverType = Type.GetType(transportSettings.TransportClass);
                            if (driverType == null)
                                throw new TypeLoadException(
                                    $"Cannot instantiate transport [{transportSettings.TransportClass}]. Cannot find the type.");

                            if (!typeof(Transport.Transport).IsAssignableFrom(driverType))
                                throw new TypeLoadException(
                                    $"Cannot instantiate transport [{transportSettings.TransportClass}]. It does not implement [{typeof (Transport.Transport).FullName}].");

                            var constructorInfo = driverType.GetConstructor(new[] { typeof(ActorSystem), typeof(Config) });
                            if (constructorInfo == null)
                                throw new TypeLoadException(
                                    $"Cannot instantiate transport [{transportSettings.TransportClass}]. " +
                                    $"It has no public constructor with [{typeof (ActorSystem).FullName}] and [{typeof (Config).FullName}] parameters");

                            // ReSharper disable once AssignNullToNotNullAttribute
                            driver = (Transport.Transport)Activator.CreateInstance(driverType, args);
                        }
                        catch (Exception ex)
                        {
                            var ei = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                            var task = new Task<List<(ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>)>>(() =>
                            {
                                ei.Throw();
                                return null;
                            });
                            task.RunSynchronously();
                            _listens = task;
                            return _listens;
                        }

                        //Iteratively decorates the bottom level driver with a list of adapters
                        //The chain at this point:
                        //  Adapter <-- .. <-- Adapter <-- Driver
                        var wrappedTransport = transportSettings.Adapters.Select(x => TransportAdaptersExtension.For(Context.System).GetAdapterProvider(x)).Aggregate(driver,
                            (transport, provider) => provider.Create(transport, (ExtendedActorSystem)Context.System));

                        //Apply AkkaProtocolTransport wrapper to the end of the chain
                        //The chain at this point:
                        // AkkaProtocolTransport <-- Adapter <-- .. <-- Adapter <-- Driver
                        transports.Add(new AkkaProtocolTransport(wrappedTransport, Context.System, new AkkaProtocolSettings(_conf), new AkkaPduProtobuffCodec(Context.System)));
                    }

                    // Collect all transports, listen addresses, and listener promises in one Task
                    var tasks = transports.Select(x => x.Listen().ContinueWith(
                        result => (new ProtocolTransportAddressPair(x, result.Result.Item1), result.Result.Item2), TaskContinuationOptions.ExecuteSynchronously));
                    _listens = Task.WhenAll(tasks).ContinueWith(transportResults => transportResults.Result.ToList(), TaskContinuationOptions.ExecuteSynchronously);
                }
                return _listens;
            }
        }

        private void AcceptPendingReader(IActorRef takingOverFrom)
        {
            if (_pendingReadHandoffs.TryGetValue(takingOverFrom, out var handle))
            {
                _pendingReadHandoffs.Remove(takingOverFrom);
                _eventPublisher.NotifyListeners(new AssociatedEvent(handle.LocalAddress, handle.RemoteAddress, inbound: true));
                var endpoint = CreateEndpoint(handle.RemoteAddress, handle.LocalAddress,
                    _transportMapping[handle.LocalAddress], _settings, false, handle);
                _endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint, handle.HandshakeInfo.Uid);
            }
        }

        private void RemovePendingReader(IActorRef takingOverFrom, AkkaProtocolHandle withHandle)
        {
            if (_pendingReadHandoffs.TryGetValue(takingOverFrom, out var handle) && handle.Equals(withHandle))
                _pendingReadHandoffs.Remove(takingOverFrom);
        }

        private void CreateAndRegisterEndpoint(AkkaProtocolHandle handle)
        {
            var writing = _settings.UsePassiveConnections && !_endpoints.HasWritableEndpointFor(handle.RemoteAddress);
            _eventPublisher.NotifyListeners(new AssociatedEvent(handle.LocalAddress, handle.RemoteAddress, true));
            var endpoint = CreateEndpoint(
                handle.RemoteAddress,
                handle.LocalAddress,
                _transportMapping[handle.LocalAddress],
                _settings,
                writing,
                handle);

            if (writing)
            {
                _endpoints.RegisterWritableEndpoint(handle.RemoteAddress, endpoint, handle.HandshakeInfo.Uid);
            }
            else
            {
                _endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint, handle.HandshakeInfo.Uid);
                if (!_endpoints.HasWritableEndpointFor(handle.RemoteAddress))
                    _endpoints.RemovePolicy(handle.RemoteAddress);
            }
        }

        private IActorRef CreateEndpoint(
            Address remoteAddress,
            Address localAddress,
            AkkaProtocolTransport transport,
            RemoteSettings endpointSettings,
            bool writing,
            AkkaProtocolHandle handleOption = null)
        {
            System.Diagnostics.Debug.Assert(_transportMapping.ContainsKey(localAddress));
            // refuseUid is ignored for read-only endpoints since the UID of the remote system is already known and has passed
            // quarantine checks
            var refuseUid = _endpoints.RefuseUid(remoteAddress);

            IActorRef endpointActor;

            if (writing)
            {
                endpointActor =
                    Context.ActorOf(RARP.For(Context.System)
                    .ConfigureDispatcher(
                        ReliableDeliverySupervisor.ReliableDeliverySupervisorProps(handleOption, localAddress,
                            remoteAddress, refuseUid, transport, endpointSettings, new AkkaPduProtobuffCodec(Context.System),
                            _receiveBuffers, endpointSettings.Dispatcher)
                            .WithDeploy(Deploy.Local)),
                        $"reliableEndpointWriter-{AddressUrlEncoder.Encode(remoteAddress)}-{_endpointId.Next()}");
            }
            else
            {
                endpointActor =
                    Context.ActorOf(RARP.For(Context.System)
                    .ConfigureDispatcher(
                        EndpointWriter.EndpointWriterProps(handleOption, localAddress, remoteAddress, refuseUid,
                            transport, endpointSettings, new AkkaPduProtobuffCodec(Context.System), _receiveBuffers,
                            reliableDeliverySupervisor: null)
                            .WithDeploy(Deploy.Local)),
                        $"endpointWriter-{AddressUrlEncoder.Encode(remoteAddress)}-{_endpointId.Next()}");
            }

            Context.Watch(endpointActor);
            return endpointActor;
        }

        #endregion

    }
}

