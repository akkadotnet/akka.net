using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Remote.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Remote.Transport;

namespace Akka.Remote
{
    public class EndpointManager : UntypedActor
    {
        private RemoteSettings settings;
        private LoggingAdapter log;
        private IDictionary<Address, AkkaProtocolTransport> transportMapping = new Dictionary<Address, AkkaProtocolTransport>();
        private EndpointRegistry endpoints = new EndpointRegistry();

        public EndpointManager(RemoteSettings settings, Event.LoggingAdapter log)
        {
            this.settings = settings;
            this.log = log;
        }
        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Listen>(m =>
                {
                    var res = Listens();
                    transportMapping = res.ToDictionary(k => k.Address, v => v.ProtocolTransport);
                    Sender.Tell(res);
                })
                .With<Send>(m =>
                {
                    var recipientAddress = m.Recipient.Path.Address;
                    var localAddress = m.Recipient.LocalAddressToUse;

                    Func<long?,ActorRef> createAndRegisterWritingEndpoint = refuseUid =>
                    {
                        AkkaProtocolTransport transport = null;
                        transportMapping.TryGetValue(localAddress, out transport);
                        var recipientRef = m.Recipient;
                        var localAddressToUse = recipientRef.LocalAddressToUse;
                        var endpoint = CreateEndpoint(recipientAddress, transport, localAddressToUse, refuseUid);
                        this.endpoints.RegisterWritableEndpoint(recipientAddress, endpoint);
                        return endpoint;
                    };
                    
                    endpoints
                        .WritableEndpointWithPolicyFor(recipientAddress)
                        .Match()
                        .With<Pass>(p => p.Endpoint.Tell(message))
                        .With<Gated>(p => {
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

        private long endpointId = 0;
        private InternalActorRef CreateEndpoint(Address recipientAddress, AkkaProtocolTransport transport, Address localAddressToUse, long? refuseUid)
        {
            var escapedAddress = Uri.EscapeDataString(recipientAddress.ToString());
            string name = string.Format("endpointWriter-{0}-{1}", escapedAddress ,endpointId++);
            var actor = Context.ActorOf(Props.Create(() => new EndpointActor(localAddressToUse, recipientAddress, transport.Transport, this.settings)), name);
            return actor;
        }

        private void CreateEndpoint()
        {
        }

        private ProtocolTransportAddressPair[] Listens()
        {
            var transports = this.settings.Transports.Select(t =>
                {
                    var driverType = Type.GetType(t.TransportClass);
                    if (driverType == null)
                    {
                        throw new ArgumentException("The type [" + t.TransportClass + "] could not be resolved");
                    }
                    var driver = (Transport.Transport)Activator.CreateInstance(driverType,Context.System,t.Config);
                    var wrappedTransport = driver; //TODO: Akka applies adapters and other yet unknown stuff
                    var address = driver.Listen();
                    return new ProtocolTransportAddressPair(new AkkaProtocolTransport(wrappedTransport, Context.System, new AkkaProtocolSettings(t.Config)), address);
                }).ToArray();
            return transports;
        }
    }
}
