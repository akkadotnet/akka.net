//-----------------------------------------------------------------------
// <copyright file="AkkaIOTransport.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote.Transport.AkkaIO
{
    /// <summary>
    /// TBD
    /// </summary>
    public class AkkaIOTransport : Transport
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly string Protocal = "tcp";

        private readonly IActorRef _manager;
        private readonly Settings _settings;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="config">TBD</param>
        public AkkaIOTransport(ActorSystem system, Config config)
        {
            _settings = new Settings(config);
            _manager = system.ActorOf(Props.Create(() => new TransportManager()), "IO-TRANSPORT");
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override string SchemeIdentifier
        {
            get { return Protocal; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override long MaximumPayloadBytes
        {
            get { return 128000; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remote">TBD</param>
        /// <returns>TBD</returns>
        public override bool IsResponsibleFor(Address remote)
        {
            return true;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            return
                _manager.Ask<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>>(
                    new Listen(_settings.Hostname, _settings.Port));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="remoteAddress">TBD</param>
        /// <returns>TBD</returns>
        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            return _manager.Ask<AssociationHandle>(new Associate(remoteAddress));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override Task<bool> Shutdown()
        {
            return _manager.GracefulStop(TimeSpan.FromSeconds(1));
        }

        private class Settings
        {
            public Settings(Config config)
            {
                Port = config.GetInt("port");
                Hostname = config.GetString("hostname");
            }

            public int Port { get; private set; }
            public string Hostname { get; private set; }
        }
    }
}