using System;

namespace Akka.Actor
{
    /// <summary>
    /// Used to deploy actors on remote nodes at the specified <see cref="Address"/>.
    /// </summary>
    public class RemoteScope : Scope, IEquatable<RemoteScope>
    {
        protected RemoteScope()
        {
        }

        public RemoteScope(Address address)
        {
            Address = address;
        }

        public Address Address { get; set; }

        public bool Equals(RemoteScope other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Address, other.Address);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((RemoteScope) obj);
        }

        public override int GetHashCode()
        {
            return (Address != null ? Address.GetHashCode() : 0);
        }

        public override Scope WithFallback(Scope other)
        {
            return this;
        }

        public override Scope Copy()
        {
            return new RemoteScope(Address);
        }
    }
}
