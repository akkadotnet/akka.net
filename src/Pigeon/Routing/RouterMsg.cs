using System.Collections.Generic;
using System.Linq;

namespace Akka.Routing
{
    public static class RouterMessage
    {
        public static readonly GetRoutees GetRoutees = new GetRoutees();
    }

    public abstract class RouterManagementMesssage
    {
    }

    public sealed class GetRoutees : RouterManagementMesssage
    {
    }

    public sealed class Routees
    {
        public Routees(IEnumerable<Routee> routees)
        {
            Members = routees.ToArray();
        }

        public IEnumerable<Routee> Members { get; private set; }
    }
}