using System.Collections.Immutable;
using Akka.Streams.Implementation;

namespace Akka.Streams
{
    public class Fusing
    {
        public struct StructuralInfo
        {
            public readonly IImmutableDictionary<InPort, OutPort> Upstreams;
            public readonly IImmutableDictionary<OutPort, InPort> Downstreams;
            public readonly IImmutableDictionary<InPort, IModule> InOwners;
            public readonly IImmutableDictionary<OutPort, IModule> OutOwners;
            public readonly IImmutableSet<IModule> AllModules;


            public StructuralInfo(IImmutableDictionary<InPort, OutPort> upstreams, IImmutableDictionary<OutPort, InPort> downstreams, IImmutableDictionary<InPort, IModule> inOwners, IImmutableDictionary<OutPort, IModule> outOwners, IImmutableSet<IModule> allModules)
            {
                Upstreams = upstreams;
                Downstreams = downstreams;
                InOwners = inOwners;
                OutOwners = outOwners;
                AllModules = allModules;
            }
        }
    }
}