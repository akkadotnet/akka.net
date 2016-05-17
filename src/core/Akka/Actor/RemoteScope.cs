//-----------------------------------------------------------------------
// <copyright file="RemoteScope.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// This class represents a binding of an actor deployment to a remote system.
    /// Actors in this scope are deployed to a specified <see cref="Address"/>.
    /// </summary>
    public class RemoteScope : Scope, IEquatable<RemoteScope>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteScope"/> class.
        /// </summary>
        protected RemoteScope()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RemoteScope"/> class.
        /// </summary>
        /// <param name="address">The address to which actors are deployed.</param>
        public RemoteScope(Address address)
        {
            Address = address;
        }

        /// <summary>
        /// The address to which actors are deployed.
        /// </summary>
        public Address Address { get; set; }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <param name="other">An object to compare with this object.</param>
        /// <returns>
        /// <c>true</c> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <c>false</c>.
        /// </returns>
        public bool Equals(RemoteScope other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Address, other.Address);
        }

        /// <summary>
        /// Determines whether the specified <see cref="System.Object" />, is equal to this instance.
        /// </summary>
        /// <param name="obj">The <see cref="System.Object" /> to compare with this instance.</param>
        /// <returns>
        ///   <c>true</c> if the specified <see cref="System.Object" /> is equal to this instance; otherwise, <c>false</c>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((RemoteScope) obj);
        }

        /// <summary>
        /// Returns a hash code for this instance.
        /// </summary>
        /// <returns>
        /// A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table. 
        /// </returns>
        public override int GetHashCode()
        {
            return (Address != null ? Address.GetHashCode() : 0);
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.Scope" /> from this scope using another <see cref="Akka.Actor.Scope" />
        /// to backfill options that might be missing from this scope.
        ///
        /// <note>
        /// This method ignores the given scope and returns the current instance.
        /// </note>
        /// </summary>
        /// <param name="other">The <see cref="Akka.Actor.Scope" /> used for fallback configuration.</param>
        /// <returns>The instance of this scope</returns>
        public override Scope WithFallback(Scope other)
        {
            return this;
        }

        /// <summary>
        /// Creates a new <see cref="Akka.Actor.RemoteScope" /> that uses the current <see cref="Address"/>.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.RemoteScope" /></returns>
        public override Scope Copy()
        {
            return new RemoteScope(Address);
        }
    }
}
