using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote.Transport
{
    public abstract class Transport
    {
        protected Transport(ActorSystem system, Config config)
        {
            System = system;
            Config = config;
        }

        public Config Config { get; private set; }

        public ActorSystem System { get; private set; }

        public string SchemeIdentifier { get; protected set; }
        public abstract Address Listen();

        public abstract bool IsResponsibleFor(Address remote);
    }

    sealed class AssoicationHandle
    {
        public LocalAddress
    }
}