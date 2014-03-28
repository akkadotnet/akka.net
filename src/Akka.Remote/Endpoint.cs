using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Remote
{
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