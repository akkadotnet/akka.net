using System;
using System.Linq;
using Akka.Actor;
using Akka.Event;
using Akka.Remote.Artery.Utils;
using Akka.Util;

namespace Akka.Remote.Artery
{
    internal class MessageDispatcher
    {
        private readonly ExtendedActorSystem _system;
        private readonly RemoteActorRefProvider _provider;

        private readonly IInternalActorRef _remoteDaemon;
        private readonly ILoggingAdapter _log;
        private readonly bool _debugLogEnabled;

        public MessageDispatcher(ExtendedActorSystem system, RemoteActorRefProvider provider)
        {
            _system = system;
            _provider = provider;

            _remoteDaemon = provider.RemoteDaemon;
            // TODO: switch to WithMarker if we port over marker logging in the future
            //_log = Logging.WithMarker(system, GetType().Name);
            _log = Logging.GetLogger(system, GetType().Name); 
            _debugLogEnabled = _log.IsDebugEnabled;
        }

        public void Dispatch(IInboundEnvelope inboundEnvelope)
        {
            throw new NotImplementedException();
        }
    }
}
