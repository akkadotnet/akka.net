using System;

namespace Akka.IO
{
    public interface IDnsProvider
    {
        DnsBase Cache { get; }
        Type ActorClass { get; }
        Type ManagerClass { get; }
    }
}
