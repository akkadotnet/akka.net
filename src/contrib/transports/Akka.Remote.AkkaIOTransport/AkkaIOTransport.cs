//-----------------------------------------------------------------------
// <copyright file="AkkaIOTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.Transport;

namespace Akka.Remote.AkkaIOTransport
{
    public class AkkaIOTransport : Transport.Transport
    {
        public static readonly string Protocal = "tcp"; 

        class Settings
        {
            public Settings(Config config)
            {
                Port = config.GetInt("port");
            }

            public int Port { get; set; }
        }

        private readonly IActorRef _manager;
        private readonly Settings _settings;

        public AkkaIOTransport(ActorSystem system, Config config)
        {
            _settings = new Settings(config);
            _manager = system.ActorOf(Props.Create(() => new TransportManager()), "IO-TRANSPORT");
        }

        public override string SchemeIdentifier { get { return Protocal; } }
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            return _manager.Ask<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>>(new Listen(_settings.Port));
        }
        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            return _manager.Ask<AssociationHandle>(new Associate(remoteAddress));
        }

        public override Task<bool> Shutdown()
        {
            return _manager.GracefulStop(TimeSpan.FromSeconds(1));
        }
    }
}
