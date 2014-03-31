using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;
using System.Diagnostics;
using Akka.Tools;

namespace Akka.Remote
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal class EndpointManager : UntypedActor
    {

        #region Policy definitions

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

        public class Pass : EndpointPolicy
        {
            public Pass(ActorRef endpoint, int? uid) : base(false)
            {
                Uid = uid;
                Endpoint = endpoint;
            }

            public ActorRef Endpoint { get; private set; }

            public int? Uid { get; private set; }
        }

        public class Gated : EndpointPolicy
        {
            public Gated(Deadline deadline) : base(true)
            {
                TimeOfRelease = deadline;
            }

            public Deadline TimeOfRelease { get; private set; }
        }

        public class Quarantined : EndpointPolicy
        {
            public Quarantined(long uid, Deadline deadline) : base(true)
            {
                Uid = uid;
                Deadline = deadline;
            }

            public long Uid { get; private set; }

            public Deadline Deadline { get; private set; }
        }

        #endregion

        #region RemotingCommands and operations

        /// <summary>
        /// Messages sent between <see cref="Remoting"/> and <see cref="EndpointManager"/>
        /// </summary>
        public abstract class RemotingCommand : NoSerializationVerificationNeeded { }

        public sealed class Listen : RemotingCommand
        {
            public Listen(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise)
            {
                AddressesPromise = addressesPromise;
            }

            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }
        }

        public sealed class StartupFinished : RemotingCommand { }

        public sealed class ShutdownAndFlush : RemotingCommand { }

        public sealed class Send : RemotingCommand, IHasSequenceNumber
        {
            public Send(object message, RemoteActorRef recipient, ActorRef senderOption = null, SeqNo seqOpt = null)
            {
                Recipient = recipient;
                SenderOption = senderOption;
                Message = message;
                _seq = seqOpt;
            }

            public object Message { get; private set; }

            /// <summary>
            /// Can be null!
            /// </summary>
            public ActorRef SenderOption { get; private set; }

            public RemoteActorRef Recipient { get; private set; }

            public override string ToString()
            {
                return string.Format("Remote message {0} -> {1}", SenderOption, Recipient);
            }

            private readonly SeqNo _seq;

            public SeqNo Seq
            {
                get
                {
                    //this MUST throw an exception to indicate that we attempted to put a nonsequenced message in one of the
                    //acknowledged delivery buffers
                    var magic = _seq.GetHashCode();
                    return _seq;
                }
            }

            public Send Copy(SeqNo opt)
            {
                return new Send(Message, Recipient, SenderOption, opt);
            }
        }

        public sealed class Quarantine : RemotingCommand
        {
            public Quarantine(Address remoteAddress, int? uid)
            {
                Uid = uid;
                RemoteAddress = remoteAddress;
            }

            public Address RemoteAddress { get; private set; }

            public int? Uid { get; private set; }
        }

        public sealed class ManagementCommand : RemotingCommand
        {
            public ManagementCommand(object cmd)
            {
                Cmd = cmd;
            }

            public object Cmd { get; private set; }
        }

        public sealed class ManagementCommandAck
        {
            public ManagementCommandAck(bool status)
            {
                Status = status;
            }

            public bool Status { get; private set; }
        }

        #endregion

        #region Messages internal to EndpointManager

        public sealed class Prune : NoSerializationVerificationNeeded { }

        public sealed class ListensResult : NoSerializationVerificationNeeded
        {
            public ListensResult(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise, IList<Tuple<AkkaProtocolTransport, Address, TaskCompletionSource<IAssociationEventListener>>> results)
            {
                Results = results;
                AddressesPromise = addressesPromise;
            }

            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }

            public IList<Tuple<AkkaProtocolTransport, Address, TaskCompletionSource<IAssociationEventListener>>> Results
            { get; private set; }
        }

        public sealed class ListensFailure : NoSerializationVerificationNeeded
        {
            public ListensFailure(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise, Exception cause)
            {
                Cause = cause;
                AddressesPromise = addressesPromise;
            }

            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }

            public Exception Cause { get; private set; }
        }

        /// <summary>
        /// Helper class to store address pairs
        /// </summary>
        public sealed class Link
        {
            public Link(Address localAddress, Address remoteAddress)
            {
                RemoteAddress = remoteAddress;
                LocalAddress = localAddress;
            }

            public Address LocalAddress { get; private set; }

            public Address RemoteAddress { get; private set; }

            /// <summary>
            /// Overrode this to make sure that the <see cref="ReliableDeliverySupervisor"/> can correctly store
            /// <see cref="AckedReceiveBuffer{T}"/> data for each <see cref="Link"/> individually, since the HashCode
            /// is what Dictionary types use internally for equality checking by default.
            /// </summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash*23 + (LocalAddress == null ? 0 : LocalAddress.GetHashCode());
                    hash = hash*23 + (RemoteAddress == null ? 0 : RemoteAddress.GetHashCode());
                    return hash;
                }
            }
        }

        public sealed class ResendState
        {
            public ResendState(int uid, AckedReceiveBuffer<Message> buffer)
            {
                Buffer = buffer;
                Uid = uid;
            }

            public int Uid { get; private set; }

            public AckedReceiveBuffer<Message> Buffer { get; private set; }
        }

        #endregion

        public EndpointManager(RemoteSettings settings, LoggingAdapter log)
        {
            this.settings = settings;
            this.log = log;
            eventPublisher = new EventPublisher(Context.System, log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
        }

        /// <summary>
        /// Mapping between addresses and endpoint actors. If passive connections are turned off, incoming connections
        /// will not be part of this map!
        /// </summary>
        private readonly EndpointRegistry endpoints = new EndpointRegistry();
        private readonly RemoteSettings settings;
        private AtomicCounterLong endpointId = new AtomicCounterLong(0L);
        private LoggingAdapter log;
        private EventPublisher eventPublisher;

        /// <summary>
        /// Mapping between transports and the local addresses they listen to
        /// </summary>
        private Dictionary<Address, AkkaProtocolTransport> transportMapping =
            new Dictionary<Address, AkkaProtocolTransport>();

        private ConcurrentDictionary<Link, ResendState> _receiveBuffers = new ConcurrentDictionary<Link, ResendState>();

        private bool RetryGateEnabled
        {
            get { return settings.RetryGateClosedFor > TimeSpan.Zero; }
        }

        private TimeSpan PruneInterval
        {
            get
            {
                //PruneInterval = 2x the RetryGateClosedFor value, if available
                if (RetryGateEnabled) return settings.RetryGateClosedFor.Add(settings.RetryGateClosedFor);
                else return TimeSpan.Zero;
            }
        }

        private CancellationTokenSource _pruneCancellationTokenSource;
        private Task _pruneTimeCancellableImpl;

        /// <summary>
        /// Cancellable task for terminating <see cref="Prune"/> operations.
        /// </summary>
        private Task PruneTimerCancelleable
        {
            get
            {
                if (RetryGateEnabled && _pruneTimeCancellableImpl == null)
                {
                    _pruneCancellationTokenSource = new CancellationTokenSource();
                    return (_pruneTimeCancellableImpl = Context.System.Scheduler.Schedule(PruneInterval, PruneInterval, Self, new Prune(),
                        _pruneCancellationTokenSource.Token));
                }
                return null;
            }
        }

        private Dictionary<ActorRef, AkkaProtocolHandle> pendingReadHandoffs = new Dictionary<ActorRef, AkkaProtocolHandle>();
        private Dictionary<ActorRef, List<InboundAssociation>> stashedInbound = new Dictionary<ActorRef, List<InboundAssociation>>();

        #region ActorBase overrides

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                var directive = Directive.Stop;

                ex.Match()
                    .With<InvalidAssociation>(ia =>
                    {
                        log.Warn("Tried to associate with unreachable remote address [{0}]. " +
                                 "Address is now gated for {1} ms, all messages to this address will be delivered to dead letters. Reason: [{2}]", 
                                 ia.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds, ia.Message);
                        endpoints.MarkAsFailed(Sender, Deadline.Now + settings.RetryGateClosedFor);
                        directive = Directive.Stop;
                    })
                    .With<ShutDownAssociation>(shutdown =>
                    {
                        log.Debug("Remote system with address [{0}] has shut down. " +
                                  "Address is not gated for {1}ms, all messages to this address will be delivered to dead letters.", 
                                  shutdown.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds);
                        endpoints.MarkAsFailed(Sender, Deadline.Now + settings.RetryGateClosedFor);
                        directive = Directive.Stop;
                    })
                    .With<HopelessAssociation>(hopeless =>
                    {
                        if (settings.QuarantineDuration.HasValue && hopeless.Uid.HasValue)
                        {
                            endpoints.MarkAsQuarantined(hopeless.RemoteAddress, hopeless.Uid.Value,
                                Deadline.Now + settings.QuarantineDuration.Value);
                            eventPublisher.NotifyListeners(new QuarantinedEvent(hopeless.RemoteAddress,
                                hopeless.Uid.Value));
                        }
                        else
                        {
                            log.Warn("Association to [{0}] with unknown UID is irrecoverably failed. " +
                                     "Address cannot be quarantined without knowing the UID, gating instead for {1} ms.",
                                hopeless.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds);
                            endpoints.MarkAsFailed(Sender, Deadline.Now + settings.RetryGateClosedFor);
                        }
                        directive = Directive.Stop;
                    })
                    .Default(msg =>
                    {
                        if (msg is EndpointDisassociatedException || msg is EndpointAssociationException) { } //no logging
                        else { log.Error(ex, ex.Message); }
                    });

                return directive;
            });
        }

        protected override void OnReceive(object message)
        {
            
        }

        protected void Accepting(object message)
        {
            
        }

        protected void Flushing(object message)
        {
            
        }

        #endregion

        #region Internal methods

        private Task<IList<Tuple<ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>>>>
            Listens()
        {
            throw new NotImplementedException();

            /*
             * Constructs chains of adapters on top of each driven given in configuration. The result structure looks like the following:
             * 
             *      AkkaProtocolTransport <-- Adapter <-- ... <-- Adapter <-- Driver
             * 
             * The transports variable contains only the heads of each chains (the AkkaProtocolTransport instances)
             */
            var transports = new List<AkkaProtocolTransport>();
            foreach (var transportSettings in settings.Transports)
            {
                var args = new object[] {Context.System, transportSettings.Config};

                //Loads the driver -- the bottom element of the chain
                //The chain at this point:
                //  Driver
                Transport.Transport driver;
                try
                {
                    var driverType = Type.GetType(transportSettings.TransportClass);
// ReSharper disable once AssignNullToNotNullAttribute
                    driver = (Transport.Transport)Activator.CreateInstance(driverType, args);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException(string.Format("Cannot instantiate transport [{0}]. " +
                                                              "Make sure it extends [Akka.Remote.Transport.Transport and has constructor with " +
                                                              "[Akka.Actor.ActorSystem] and [Akka.Configuration.Config] parameters", transportSettings.TransportClass), ex);
                }

                //Iteratively decorates the bottom level driver with a list of adapters
                //The chain at this point:
                // Adapter <-- .. <-- Adapter <-- Driver
                //var wrappedTransport = transportSettings.Adapters.Select(x =>
                //{

                //});
            }
        }

        private void AcceptPendingReader(ActorRef takingOverFrom)
        {
            if (pendingReadHandoffs.ContainsKey(takingOverFrom))
            {
                var handle = pendingReadHandoffs[takingOverFrom];
                pendingReadHandoffs.Remove(takingOverFrom);
                eventPublisher.NotifyListeners(new AssociatedEvent(handle.LocalAddress, handle.RemoteAddress, inbound:true));
                var endpoint = CreateEndpoint(handle.RemoteAddress, handle.LocalAddress,
                    transportMapping[handle.LocalAddress], settings, false, handle, refuseUid: null);
                endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint);
            }
        }

        private void RemovePendingReader(ActorRef takingOverFrom, AkkaProtocolHandle withHandle)
        {
            if (pendingReadHandoffs.ContainsKey(takingOverFrom) &&
                pendingReadHandoffs[takingOverFrom].Equals(withHandle))
            {
                pendingReadHandoffs.Remove(takingOverFrom);
            }
        }

        private void CreateAndRegisterEndpoint(AkkaProtocolHandle handle, int? refuseId)
        {
            var writing = settings.UsePassiveConnections && !endpoints.HasWriteableEndpointFor(handle.RemoteAddress);
            eventPublisher.NotifyListeners(new AssociatedEvent(handle.LocalAddress, handle.RemoteAddress, true));
            var endpoint = CreateEndpoint(
                handle.RemoteAddress,
                handle.LocalAddress,
                transportMapping[handle.LocalAddress],
                settings,
                writing,
                handle,
                refuseId);

            if (writing)
            {
                endpoints.RegisterWritableEndpoint(handle.RemoteAddress, endpoint, (int) handle.HandshakeInfo.Uid);
            }
            else
            {
                endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint);
                endpoints.RemovePolicy(handle.RemoteAddress);
            }
        }

        private InternalActorRef CreateEndpoint(Address remoteAddress, Address localAddress, AkkaProtocolTransport transport,
            RemoteSettings endpointSettings, bool writing, AkkaProtocolHandle handleOption = null, int? refuseUid = null)
        {
            System.Diagnostics.Debug.Assert(transportMapping.ContainsKey(localAddress));
            System.Diagnostics.Debug.Assert(writing || refuseUid == null);

            InternalActorRef endpointActor;

            if (writing)
            {
                endpointActor =
                    Context.ActorOf(
                        ReliableDeliverySupervisor.ReliableDeliverySupervisorProps(handleOption, localAddress,
                            remoteAddress, refuseUid, transport, endpointSettings, new AkkaPduProtobuffCodec(),
                            _receiveBuffers).WithDeploy(Deploy.Local),
                        string.Format("reliableEndpointWriter-{0}-{1}", AddressUrlEncoder.Encode(remoteAddress),
                            endpointId.Next));
            }
            else
            {
                endpointActor =
                    Context.ActorOf(
                        EndpointWriter.EndpointWriterProps(handleOption, localAddress, remoteAddress, refuseUid,
                            transport, endpointSettings, new AkkaPduProtobuffCodec(), _receiveBuffers,
                            reliableDeliverySupervisor: null).WithDeploy(Deploy.Local),
                        string.Format("endpointWriter-{0}-{1}", AddressUrlEncoder.Encode(remoteAddress), endpointId.Next));
            }

            Context.Watch(endpointActor);
            return endpointActor;
        }

        #endregion

    }
}