using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Transport;

namespace Akka.Remote
{
    public class EndpointManager : UntypedActor
    {
        private readonly EndpointRegistry endpoints = new EndpointRegistry();
        private readonly RemoteSettings settings;
        private long endpointId;
        private LoggingAdapter log;

        private Dictionary<Address, AkkaProtocolTransport> transportMapping =
            new Dictionary<Address, AkkaProtocolTransport>();

        public EndpointManager(RemoteSettings settings, LoggingAdapter log)
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

        private InternalActorRef CreateEndpoint(Address recipientAddress, AkkaProtocolTransport transport,
            Address localAddressToUse, long? refuseUid)
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