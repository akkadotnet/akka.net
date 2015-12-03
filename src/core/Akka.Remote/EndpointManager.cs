//-----------------------------------------------------------------------
// <copyright file="EndpointManager.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using Akka.Util.Internal;

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

        /// <summary>
        /// We will always accept a 
        /// </summary>
        public class Pass : EndpointPolicy
        {
            public Pass(IActorRef endpoint, int? uid)
                : base(false)
            {
                Uid = uid;
                Endpoint = endpoint;
            }

            public IActorRef Endpoint { get; private set; }

            public int? Uid { get; private set; }
        }

        /// <summary>
        /// A Gated node can't be connected to from this process for <see cref="TimeOfRelease"/>,
        /// but we may accept an inbound connection from it if the remote node recovers on its own.
        /// </summary>
        public class Gated : EndpointPolicy
        {
            public Gated(Deadline deadline)
                : base(true)
            {
                TimeOfRelease = deadline;
            }

            public Deadline TimeOfRelease { get; private set; }
        }

        /// <summary>
        /// We do not accept connection attempts for a quarantined node until it restarts and resets its UID.
        /// </summary>
        public class Quarantined : EndpointPolicy
        {
            public Quarantined(long uid, Deadline deadline)
                : base(true)
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
        public abstract class RemotingCommand : INoSerializationVerificationNeeded { }

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
            public Send(object message, RemoteActorRef recipient, IActorRef senderOption = null, SeqNo seqOpt = null)
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
            public IActorRef SenderOption { get; private set; }

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

        public sealed class Prune : INoSerializationVerificationNeeded { }

        public sealed class ListensResult : INoSerializationVerificationNeeded
        {
            public ListensResult(TaskCompletionSource<IList<ProtocolTransportAddressPair>> addressesPromise, List<Tuple<ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>>> results)
            {
                Results = results;
                AddressesPromise = addressesPromise;
            }

            public TaskCompletionSource<IList<ProtocolTransportAddressPair>> AddressesPromise { get; private set; }

            public IList<Tuple<ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>>> Results
            { get; private set; }
        }

        public sealed class ListensFailure : INoSerializationVerificationNeeded
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
                    hash = hash * 23 + (LocalAddress == null ? 0 : LocalAddress.GetHashCode());
                    hash = hash * 23 + (RemoteAddress == null ? 0 : RemoteAddress.GetHashCode());
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

        public EndpointManager(Config config, ILoggingAdapter log)
        {
            conf = config;
            settings = new RemoteSettings(conf);
            this.log = log;
            eventPublisher = new EventPublisher(Context.System, log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
        }

        /// <summary>
        /// Mapping between addresses and endpoint actors. If passive connections are turned off, incoming connections
        /// will not be part of this map!
        /// </summary>
        private readonly EndpointRegistry endpoints = new EndpointRegistry();
        private readonly RemoteSettings settings;
        private readonly Config conf;
        private AtomicCounterLong endpointId = new AtomicCounterLong(0L);
        private ILoggingAdapter log;
        private EventPublisher eventPublisher;

        /// <summary>
        /// Used to indicate when an abrupt shutdown occurs
        /// </summary>
        private bool _normalShutdown = false;

        /// <summary>
        /// Mapping between transports and the local addresses they listen to
        /// </summary>
        private Dictionary<Address, AkkaProtocolTransport> _transportMapping =
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
                return null;
            }
        }

        private Dictionary<IActorRef, AkkaProtocolHandle> pendingReadHandoffs = new Dictionary<IActorRef, AkkaProtocolHandle>();
        private Dictionary<IActorRef, List<InboundAssociation>> stashedInbound = new Dictionary<IActorRef, List<InboundAssociation>>();

        #region ActorBase overrides

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(ex =>
            {
                var directive = Directive.Stop;

                ex.Match()
                    .With<InvalidAddressAssociationException>(ia =>
                    {
                        log.Warning("Tried to associate with unreachable remote address [{0}]. Address is now gated for {1} ms, all messages to this address will be delivered to dead letters. Reason: [{2}]",
                                 ia.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds, ia.Message);
                        endpoints.MarkAsFailed(Sender, Deadline.Now + settings.RetryGateClosedFor);
                        AddressTerminatedTopic.Get(Context.System).Publish(new AddressTerminated(ia.RemoteAddress));
                        directive = Directive.Stop;
                    })
                    .With<ShutDownAssociationException>(shutdown =>
                    {
                        log.Debug("Remote system with address [{0}] has shut down. Address is now gated for {1}ms, all messages to this address will be delivered to dead letters.",
                                  shutdown.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds);
                        endpoints.MarkAsFailed(Sender, Deadline.Now + settings.RetryGateClosedFor);
                        AddressTerminatedTopic.Get(Context.System).Publish(new AddressTerminated(shutdown.RemoteAddress));
                        directive = Directive.Stop;
                    })
                    .With<HopelessAssociationException>(hopeless =>
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
                            log.Warning("Association to [{0}] with unknown UID is irrecoverably failed. Address cannot be quarantined without knowing the UID, gating instead for {1} ms.",
                                hopeless.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds);
                            endpoints.MarkAsFailed(Sender, Deadline.Now + settings.RetryGateClosedFor);
                        }
                        AddressTerminatedTopic.Get(Context.System).Publish(new AddressTerminated(hopeless.RemoteAddress));
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

        protected override void PreStart()
        {
            if(PruneTimerCancelleable != null)
                log.Debug("Starting prune timer for endpoint manager...");
            base.PreStart();
        }

        protected override void PostStop()
        {
            if(PruneTimerCancelleable != null)
                _pruneTimeCancelable.Cancel();
            foreach(var h in pendingReadHandoffs.Values)
                h.Disassociate(DisassociateInfo.Shutdown);

            if (!_normalShutdown)
            {
                // Remaining running endpoints are children, so they will clean up themselves.
                // We still need to clean up any remaining transports because handles might be in mailboxes, and for example
                // Netty is not part of the actor hierarchy, so its handles will not be cleaned up if no actor is taking
                // responsibility of them (because they are sitting in a mailbox).
                log.Error("Remoting system has been terminated abrubtly. Attempting to shut down transports");
                foreach (var t in _transportMapping.Values)
                    t.Shutdown();
            }
        }

        protected override void OnReceive(object message)
        {
            message.Match()
                /*
                 * the first command the EndpointManager receives.
                 * instructs the EndpointManager to fire off its "Listens" command, which starts
                 * up all inbound transports and binds them to specific addresses via configuration.
                 * those results will then be piped back to Remoting, who waits for the results of
                 * listen.AddressPromise.
                 * */
                .With<Listen>(listen => Listens.ContinueWith<INoSerializationVerificationNeeded>(listens =>
                {
                    if (listens.IsFaulted)
                    {
                        return new ListensFailure(listen.AddressesPromise, listens.Exception);
                    }
                    else
                    {
                        return new ListensResult(listen.AddressesPromise, listens.Result);
                    }
                }, TaskContinuationOptions.ExecuteSynchronously)
                    .PipeTo(Self))
                .With<ListensResult>(listens =>
                {
                    _transportMapping = (from mapping in listens.Results
                                        group mapping by mapping.Item1.Address
                                            into g
                                            select new { address = g.Key, transports = g.ToList() }).Select(x =>
                        {
                            if (x.transports.Count > 1)
                            {
                                throw new RemoteTransportException(
                                    string.Format("There are more than one transports listening on local address {0}",
                                        x.address));
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
                })
                .With<ListensFailure>(failure => failure.AddressesPromise.SetException(failure.Cause))
                // defer the inbound association until we can enter "Accepting" behavior
                .With<InboundAssociation>(ia => Context.System.Scheduler.ScheduleTellOnce(TimeSpan.FromMilliseconds(10), Self, ia, Self))
                .With<ManagementCommand>(mc => Sender.Tell(new ManagementCommandAck(status:false)))
                // transports are all started. Ready to start accepting inbound associations.
                .With<StartupFinished>(sf => Context.Become(Accepting))
                .With<ShutdownAndFlush>(sf =>
                {
                    Sender.Tell(true);
                    Context.Stop(Self);
                });
        }

        /// <summary>
        /// Message-processing behavior when the <see cref="EndpointManager"/> is able to accept
        /// inbound association requests.
        /// </summary>
        /// <param name="message">Messages from local system and the network.</param>
        protected void Accepting(object message)
        {
            message.Match()
                .With<ManagementCommand>(mc =>
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
                        },
                            TaskContinuationOptions.ExecuteSynchronously)
                        .PipeTo(sender);
                })
                .With<Quarantine>(quarantine =>
                {
                    //Stop writers
                    if (endpoints.WritableEndpointWithPolicyFor(quarantine.RemoteAddress) is Pass)
                    {
                        var pass = (Pass) endpoints.WritableEndpointWithPolicyFor(quarantine.RemoteAddress);
                        Context.Stop(pass.Endpoint);
                        if (!pass.Uid.HasValue)
                        {
                            log.Warning("Association to [{0}] with unknown UID is reported as quarantined, but address cannot be quarantined without knowing the UID, gated instead for {0} ms",
                                quarantine.RemoteAddress, settings.RetryGateClosedFor.TotalMilliseconds);
                            endpoints.MarkAsFailed(pass.Endpoint, Deadline.Now + settings.RetryGateClosedFor);
                        }
                    }

                    //Stop inbound read-only association
                    var read = endpoints.ReadOnlyEndpointFor(quarantine.RemoteAddress);
                    if (read != null)
                    {
                        Context.Stop((IInternalActorRef)read);
                    }

                    if (quarantine.Uid.HasValue)
                    {
                        endpoints.MarkAsQuarantined(quarantine.RemoteAddress, quarantine.Uid.Value, Deadline.Now + settings.QuarantineDuration);
                        eventPublisher.NotifyListeners(new QuarantinedEvent(quarantine.RemoteAddress, quarantine.Uid.Value));
                    }
                })
                .With<Send>(send =>
                {
                    var recipientAddress = send.Recipient.Path.Address;
                    Func<int?, IActorRef> createAndRegisterWritingEndpoint = refuseUid => endpoints.RegisterWritableEndpoint(recipientAddress,
                        CreateEndpoint(recipientAddress, send.Recipient.LocalAddressToUse,
                            _transportMapping[send.Recipient.LocalAddressToUse], settings, writing: true,
                            handleOption: null, refuseUid: refuseUid), refuseUid);

                    endpoints.WritableEndpointWithPolicyFor(recipientAddress).Match()
                        .With<Pass>(
                            pass =>
                            {
                                pass.Endpoint.Tell(send);
                            })
                        .With<Gated>(gated =>
                        {
                            if(gated.TimeOfRelease.IsOverdue) createAndRegisterWritingEndpoint(null).Tell(send);
                            else Context.System.DeadLetters.Tell(send);
                        })
                        .With<Quarantined>(quarantined =>
                        {
                            // timeOfRelease is only used for garbage collection reasons, therefore it is ignored here. We still have
                            // the Quarantined tombstone and we know what UID we don't want to accept, so use it.
                            createAndRegisterWritingEndpoint((int)quarantined.Uid).Tell(send);
                        })
                        .Default(msg => createAndRegisterWritingEndpoint(null).Tell(send));
                })
                .With<InboundAssociation>(HandleInboundAssociation)
                .With<EndpointWriter.StoppedReading>(endpoint => AcceptPendingReader(endpoint.Writer))
                .With<Terminated>(terminated =>
                {
                    AcceptPendingReader(terminated.ActorRef);
                    endpoints.UnregisterEndpoint(terminated.ActorRef);
                    HandleStashedInbound(terminated.ActorRef);
                })
                .With<EndpointWriter.TookOver>(tookover => RemovePendingReader(tookover.Writer, tookover.ProtocolHandle))
                .With<ReliableDeliverySupervisor.GotUid>(gotuid =>
                {
                    endpoints.RegisterWritableEndpointUid(Sender, gotuid.Uid);
                    HandleStashedInbound(Sender);
                })
                .With<Prune>(prune => endpoints.Prune())
                .With<ShutdownAndFlush>(shutdown =>
                {
                    //Shutdown all endpoints and signal to Sender when ready (and whether all endpoints were shutdown gracefully)
                    var sender = Sender;

                    // The construction of the Task for shutdownStatus has to happen after the flushStatus future has been finished
                    // so that endpoints are shut down before transports.
                    var shutdownStatus = Task.WhenAll(endpoints.AllEndpoints.Select(
                            x => x.GracefulStop(settings.FlushWait, EndpointWriter.FlushAndStop.Instance))).ContinueWith(
                                result =>
                                {
                                    if (result.IsFaulted || result.IsCanceled)
                                    {
                                        if (result.Exception != null)
                                            result.Exception.Handle(e => true);
                                        return false;
                                    }
                                    return result.Result.All(x => x);
                                }, TaskContinuationOptions.ExecuteSynchronously);

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
                              }, TaskContinuationOptions.ExecuteSynchronously)).Unwrap().PipeTo(sender);


                    foreach (var handoff in pendingReadHandoffs.Values)
                    {
                       handoff.Disassociate(DisassociateInfo.Shutdown);
                    }
                    
                    //Ignore all other writes
                    _normalShutdown = true;
                    Context.Become(Flushing);
                });
        }

        protected void Flushing(object message)
        {
            message.Match()
                .With<Send>(send => Context.System.DeadLetters.Tell(send))
                .With<InboundAssociation>(
                    ia => ia.Association.AsInstanceOf<AkkaProtocolHandle>().Disassociate(DisassociateInfo.Shutdown))
                .With<Terminated>(terminated => { });
        }

        #endregion

        #region Internal methods

        private void HandleInboundAssociation(InboundAssociation ia)
        {
            var readonlyEndpoint = endpoints.ReadOnlyEndpointFor(ia.Association.RemoteAddress);
            var handle = ((AkkaProtocolHandle) ia.Association);
            if (readonlyEndpoint != null)
            {
                if (pendingReadHandoffs.ContainsKey(readonlyEndpoint)) pendingReadHandoffs[readonlyEndpoint].Disassociate();
                pendingReadHandoffs.AddOrSet(readonlyEndpoint, handle);
                readonlyEndpoint.Tell(new EndpointWriter.TakeOver(handle, Self));
            }
            else
            {
                if (endpoints.IsQuarantined(handle.RemoteAddress, (int)handle.HandshakeInfo.Uid))
                    handle.Disassociate(DisassociateInfo.Quarantined);
                else
                {
                    if (endpoints.WritableEndpointWithPolicyFor(handle.RemoteAddress) is Pass)
                    {
                        var pass = (Pass) endpoints.WritableEndpointWithPolicyFor(handle.RemoteAddress);
                        if (!pass.Uid.HasValue)
                        {
                            if (stashedInbound.ContainsKey(pass.Endpoint)) stashedInbound[pass.Endpoint].Add(ia);
                            else stashedInbound.AddOrSet(pass.Endpoint, new List<InboundAssociation>() {ia});
                        }
                        else
                        {
                            if (handle.HandshakeInfo.Uid == pass.Uid)
                            {
                                if (pendingReadHandoffs.ContainsKey(pass.Endpoint))
                                    pendingReadHandoffs[pass.Endpoint].Disassociate();
                                pendingReadHandoffs.AddOrSet(pass.Endpoint, handle);
                                pass.Endpoint.Tell(new EndpointWriter.StopReading(pass.Endpoint, Self));
                            }
                            else
                            {
                                Context.Stop(pass.Endpoint);
                                endpoints.UnregisterEndpoint(pass.Endpoint);
                                pendingReadHandoffs.Remove(pass.Endpoint);
                                CreateAndRegisterEndpoint(handle, pass.Uid);
                            }
                        }
                    }
                    else
                    {
                        var state = endpoints.WritableEndpointWithPolicyFor(handle.RemoteAddress);
                        CreateAndRegisterEndpoint(handle, null);
                    }
                }
            }
        }

        private void HandleStashedInbound(IActorRef endpoint)
        {
            var stashed = stashedInbound.GetOrElse(endpoint, new List<InboundAssociation>());
            stashedInbound.Remove(endpoint);
            foreach(var ia in stashed)
                HandleInboundAssociation(ia);
        }

        private Task<List<Tuple<ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>>>>
            _listens;
        private Task<List<Tuple<ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>>>>
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
                    foreach (var transportSettings in settings.Transports)
                    {
                        var args = new object[] { Context.System, transportSettings.Config };

                        //Loads the driver -- the bottom element of the chain
                        //The chain at this point:
                        //  Driver
                        Transport.Transport driver;
                        try
                        {
                            var driverType = Type.GetType(transportSettings.TransportClass);
                            if(driverType==null)
                                throw new TypeLoadException(string.Format("Cannot instantiate transport [{0}]. Cannot find the type.",transportSettings.TransportClass));

                            if(!typeof(Transport.Transport).IsAssignableFrom(driverType))
                                throw new TypeLoadException(string.Format("Cannot instantiate transport [{0}]. It does not implement [{1}].",transportSettings.TransportClass,typeof(Transport.Transport).FullName));

                            var constructorInfo = driverType.GetConstructor(new []{typeof(ActorSystem),typeof(Config)});
                            if(constructorInfo==null)
                                throw new TypeLoadException(string.Format("Cannot instantiate transport [{0}]. " +
                                                                          "It has no public constructor with " +
                                                                          "[{1}] and [{2}] parameters",transportSettings.TransportClass,typeof(ActorSystem).FullName,typeof(Config).FullName));

                            // ReSharper disable once AssignNullToNotNullAttribute
                            driver = (Transport.Transport)Activator.CreateInstance(driverType, args);
                        }
                        catch (Exception ex)
                        {
                            var ei = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex);
                            var task = new Task<List<Tuple<ProtocolTransportAddressPair, TaskCompletionSource<IAssociationEventListener>>>>(() =>
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
                        transports.Add(new AkkaProtocolTransport(wrappedTransport, Context.System, new AkkaProtocolSettings(conf), new AkkaPduProtobuffCodec()));
                    }

                    // Collect all transports, listen addresses, and listener promises in one Task
                    var tasks = transports.Select(x => x.Listen().ContinueWith(
                        result => Tuple.Create(new ProtocolTransportAddressPair(x, result.Result.Item1), result.Result.Item2), TaskContinuationOptions.ExecuteSynchronously));
                    _listens = Task.WhenAll(tasks).ContinueWith(transportResults => transportResults.Result.ToList(), TaskContinuationOptions.ExecuteSynchronously);
                }
                return _listens;
            }
        }

        private void AcceptPendingReader(IActorRef takingOverFrom)
        {
            if (pendingReadHandoffs.ContainsKey(takingOverFrom))
            {
                var handle = pendingReadHandoffs[takingOverFrom];
                pendingReadHandoffs.Remove(takingOverFrom);
                eventPublisher.NotifyListeners(new AssociatedEvent(handle.LocalAddress, handle.RemoteAddress, inbound: true));
                var endpoint = CreateEndpoint(handle.RemoteAddress, handle.LocalAddress,
                    _transportMapping[handle.LocalAddress], settings, false, handle, refuseUid: null);
                endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint);
            }
        }

        private void RemovePendingReader(IActorRef takingOverFrom, AkkaProtocolHandle withHandle)
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
                _transportMapping[handle.LocalAddress],
                settings,
                writing,
                handle,
                refuseId);

            if (writing)
            {
                endpoints.RegisterWritableEndpoint(handle.RemoteAddress, endpoint, (int)handle.HandshakeInfo.Uid);
            }
            else
            {
                endpoints.RegisterReadOnlyEndpoint(handle.RemoteAddress, endpoint);
                endpoints.RemovePolicy(handle.RemoteAddress);
            }
        }

        private IActorRef CreateEndpoint(
            Address remoteAddress, 
            Address localAddress, 
            AkkaProtocolTransport transport,
            RemoteSettings endpointSettings, 
            bool writing, 
            AkkaProtocolHandle handleOption = null, 
            int? refuseUid = null)
        {
            System.Diagnostics.Debug.Assert(_transportMapping.ContainsKey(localAddress));
            System.Diagnostics.Debug.Assert(writing || refuseUid == null);

            IActorRef endpointActor;

            if (writing)
            {
                endpointActor =
                    Context.ActorOf(RARP.For(Context.System)
                    .ConfigureDispatcher(
                        ReliableDeliverySupervisor.ReliableDeliverySupervisorProps(handleOption, localAddress,
                            remoteAddress, refuseUid, transport, endpointSettings, new AkkaPduProtobuffCodec(),
                            _receiveBuffers, endpointSettings.Dispatcher)
                            .WithDeploy(Deploy.Local)),
                        string.Format("reliableEndpointWriter-{0}-{1}", AddressUrlEncoder.Encode(remoteAddress),
                            endpointId.Next()));
            }
            else
            {
                endpointActor =
                    Context.ActorOf(RARP.For(Context.System)
                    .ConfigureDispatcher(
                        EndpointWriter.EndpointWriterProps(handleOption, localAddress, remoteAddress, refuseUid,
                            transport, endpointSettings, new AkkaPduProtobuffCodec(), _receiveBuffers,
                            reliableDeliverySupervisor: null)
                            .WithDeploy(Deploy.Local)),
                        string.Format("endpointWriter-{0}-{1}", AddressUrlEncoder.Encode(remoteAddress), endpointId.Next()));
            }

            Context.Watch(endpointActor);
            return endpointActor;
        }

        #endregion

    }
}

