using System;
using System.Linq;
using Akka.Actor;
using Akka.Dispatch.SysMsg;
using Akka.Event;

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
                if(settings.LogReceive) log.Debug("received local message [{0}]", msgLog);
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

    /// <summary>
    /// Abstract base class for <see cref="EndpointWriter"/> and <see cref="EndpointReader"/> classes
    /// </summary>
    internal abstract class EndpointActor : UntypedActor, IActorLogging
    {
        private readonly Address localAddress;
        private Address remoteAddress;
        private RemoteSettings settings;
        private Transport.Transport transport;

        private readonly LoggingAdapter _log = Logging.GetLogger(Context);
        public LoggingAdapter Log { get { return _log; } }

        private EventPublisher _eventPublisher;

        protected bool Inbound;

        protected EndpointActor(Address localAddress, Address remoteAddress, Transport.Transport transport,
            RemoteSettings settings)
        {
            _eventPublisher = new EventPublisher(Context.System, Log, Logging.LogLevelFor(settings.RemoteLifecycleEventsLogLevel));
            this.localAddress = localAddress;
            this.remoteAddress = remoteAddress;
            this.transport = transport;
            this.settings = settings;

            //client = new TcpClient();
            //client.Connect(remoteAddress.Host, remoteAddress.Port.Value);
            //stream = client.GetStream();
        }

        #region Event publishing methods

        protected void PublishError(Exception ex, LogLevel level)
        {
            TryPublish(new AssociationErrorEvent(ex, localAddress, remoteAddress, Inbound, level));
        }

        protected void PublishDisassociated()
        {
            TryPublish(new DisassociatedEvent(localAddress, remoteAddress, Inbound));
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