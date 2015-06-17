using System;

namespace Akka.IO
{
    public class InetAddressDnsProvider : IDnsProvider
    {
        private readonly DnsBase _cache = new SimpleDnsCache();

        public DnsBase Cache { get { return _cache; }}
        public Type ActorClass { get { return typeof (InetAddressDnsResolver); } }
        public Type ManagerClass { get { return typeof (SimpleDnsManager); } }
    }
}
