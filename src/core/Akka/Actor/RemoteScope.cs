using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
{
    public class RemoteScope : Scope
    {
        [Obsolete("For Serialization only", true)]
        public RemoteScope()
        {
        }

        public RemoteScope(Address address)
        {
            Address = address;
        }

        public Address Address { get; set; }
    }
}
