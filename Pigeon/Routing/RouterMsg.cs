using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
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
        public IEnumerable<Routee> Members { get;private set; }
        public Routees(IEnumerable<Routee> routees)
        {
            this.Members = routees.ToArray();
        }
    }
}
