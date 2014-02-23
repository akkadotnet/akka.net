using Pigeon.Actor;
using Pigeon.Configuration;
using Pigeon.Event;
using Pigeon.Remote.Transport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pigeon.Remote.Transport;

namespace Pigeon.Remote
{
    public class EndpointManager : UntypedActor
    {
        private RemoteSettings settings;
        private LoggingAdapter log;

        public EndpointManager(RemoteSettings settings, Event.LoggingAdapter log)
        {
            this.settings = settings;
            this.log = log;
        }
        protected override void OnReceive(object message)
        {
            ReceiveBuilder.Match(message)
                .With<Listen>(m => 
                {
                    var res = Listens();
                    Sender.Tell(res);
                });

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
                    var driver = (Transport.Transport)Activator.CreateInstance(driverType,Context.System,this.settings.Config);
                    var wrappedTransport = driver; //TODO: Akka applies adapters and other yet unknown stuff
                    var listen = driver.Listen();
                    return new ProtocolTransportAddressPair(new AkkaProtocolTransport(wrappedTransport, Context.System, new AkkaProtocolSettings(t.Config)), listen.Item1);
                }).ToArray();
            return transports;
        }
    }
}
