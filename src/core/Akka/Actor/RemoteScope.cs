using System;
using Akka.Util.Internal;

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

        public override bool Equals(Scope other)
        {
            return base.Equals(other) && Address.Equals(other.AsInstanceOf<RemoteScope>().Address);
        }
    }
}
