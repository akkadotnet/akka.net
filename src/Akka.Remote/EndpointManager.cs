using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;

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

        public sealed class Send : RemotingCommand
        {
            public Send(object message, RemoteActorRef recipient, ActorRef senderOption = null)
            {
                Recipient = recipient;
                SenderOption = senderOption;
                Message = message;
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
        private long endpointId;
        private LoggingAdapter log;
        private EventPublisher eventPublisher;

        /// <summary>
        /// Mapping between transports and the local addresses they listen to
        /// </summary>
        private Dictionary<Address, AkkaProtocolTransport> transportMapping =
            new Dictionary<Address, AkkaProtocolTransport>();

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

        protected override SupervisorStrategy SupervisorStrategy()
        {
            //TODO: need to implement the real EndointManager supervision strategy here once some of the EndpointException subclasses have been implemented
            return base.SupervisorStrategy();
        }

        

        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Listen>(m =>
                {
                    ProtocolTransportAddressPair[] res = Listens();
                    transportMapping = res.ToDictionary(k => k.Address, v => v.ProtocolTransport);
                    Sender.Tell(res);
                })
                .With<Send>(m =>
                {
                    Address recipientAddress = m.Recipient.Path.Address;
                    Address localAddress = m.Recipient.LocalAddressToUse;

                    Func<long?, ActorRef> createAndRegisterWritingEndpoint = refuseUid =>
                    {
                        AkkaProtocolTransport transport = null;
                        transportMapping.TryGetValue(localAddress, out transport);
                        RemoteActorRef recipientRef = m.Recipient;
                        Address localAddressToUse = recipientRef.LocalAddressToUse;
                        InternalActorRef endpoint = CreateEndpoint(recipientAddress, transport, localAddressToUse,
                            refuseUid);
                        endpoints.RegisterWritableEndpoint(recipientAddress, endpoint);
                        return endpoint;
                    };

                    endpoints
                        .WritableEndpointWithPolicyFor(recipientAddress)
                        .Match()
                        .With<Pass>(p => p.Endpoint.Tell(message))
                        .With<Gated>(p =>
                        {
                            if (p.TimeOfRelease.IsOverdue)
                                createAndRegisterWritingEndpoint(null).Tell(message);
                            else
                                Context.System.DeadLetters.Tell(message);
                        })
                        .With<Quarantined>(p => createAndRegisterWritingEndpoint(p.Uid).Tell(message))
                        .Default(p => createAndRegisterWritingEndpoint(null).Tell(message));

                    /*
val recipientAddress = recipientRef.path.address

      def createAndRegisterWritingEndpoint(refuseUid: Option[Int]): ActorRef =
        endpoints.registerWritableEndpoint(
          recipientAddress,
          createEndpoint(
            recipientAddress,
            recipientRef.localAddressToUse,
            transportMapping(recipientRef.localAddressToUse),
            settings,
            handleOption = None,
            writing = true,
            refuseUid))

      endpoints.writableEndpointWithPolicyFor(recipientAddress) match {
        case Some(Pass(endpoint)) ⇒
          endpoint ! s
        case Some(Gated(timeOfRelease)) ⇒
          if (timeOfRelease.isOverdue()) createAndRegisterWritingEndpoint(refuseUid = None) ! s
          else extendedSystem.deadLetters ! s
        case Some(Quarantined(uid, _)) ⇒
          // timeOfRelease is only used for garbage collection reasons, therefore it is ignored here. We still have
          // the Quarantined tombstone and we know what UID we don't want to accept, so use it.
          createAndRegisterWritingEndpoint(refuseUid = Some(uid)) ! s
        case None ⇒
          createAndRegisterWritingEndpoint(refuseUid = None) ! s

                     */
                })
                .Default(Unhandled);
        }

        private void CreateAndRegistrEndpoint(AkkaProtocolHandle handle, int? refuseId)
        {
            var writing = settings.UsePassiveConnections && !endpoints.HasWriteableEndpointFor(handle.RemoteAddress);
            eventPublisher.NotifyListeners(new AssociatedEvent(handle.LocalAddress, handle.RemoteAddress, true));
            var endpoint = CreateEndpoint(
                handle.RemoteAddress,
                handle.LocalAddress,
                transportMapping[handle.LocalAddress],
                settings,
                handle,
                writing,
                refuseId);
            endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint);
        }

        private InternalActorRef CreateEndpoint(Address recipientAddress, Address localAddressToUse,
            AkkaProtocolTransport transport, 
            RemoteSettings endpointSettings,
            AkkaProtocolHandle handleOption,
            bool writing,
            int? refuseUid)
        {
            throw new NotImplementedException();

            //string escapedAddress = Uri.EscapeDataString(recipientAddress.ToString());
            //string name = string.Format("endpointWriter-{0}-{1}", escapedAddress, endpointId++);
            //InternalActorRef actor =
            //    Context.ActorOf(
            //        Props.Create(
            //            () => new EndpointActor(localAddressToUse, recipientAddress, transport.Transport, settings)),
            //        name);
            //return actor;
        }

        private void CreateEndpoint()
        {
        }

        private ProtocolTransportAddressPair[] Listens()
        {
            throw new NotImplementedException();

            //ProtocolTransportAddressPair[] transports = settings.Transports.Select(t =>
            //{
            //    Type driverType = Type.GetType(t.TransportClass);
            //    if (driverType == null)
            //    {
            //        throw new ArgumentException("The type [" + t.TransportClass + "] could not be resolved");
            //    }
            //    var driver = (Transport.Transport) Activator.CreateInstance(driverType, Context.System, t.Config);
            //    Transport.Transport wrappedTransport = driver; //TODO: Akka applies adapters and other yet unknown stuff
            //    Address address = driver.Listen();
            //    return
            //        new ProtocolTransportAddressPair(
            //            new AkkaProtocolTransport(wrappedTransport, Context.System, new AkkaProtocolSettings(t.Config)),
            //            address);
            //}).ToArray();
            //return transports;
        }
    }
}