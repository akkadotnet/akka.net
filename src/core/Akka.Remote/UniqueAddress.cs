using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Remote
{
    // ARTERY: Document that UniqueAddress is a copy of the same class from Akka.Cluster.Member
    // ARTERY: Temporarily use internal for now, so we don't break API approval list
    /// <summary>
    /// Member identifier consisting of address and random `uid`.
    /// The `uid` is needed to be able to distinguish different
    /// incarnations of a member with same hostname and port.
    /// </summary>
    internal class UniqueAddress : IComparable<UniqueAddress>, IEquatable<UniqueAddress>, IComparable
    {
        /// <summary>
        /// The bound listening address for Akka.Remote.
        /// </summary>
        public Address Address { get; }

        /// <summary>
        /// A random long integer used to signal the incarnation of this cluster instance.
        /// </summary>
        public int Uid { get; }

        /// <summary>
        /// Creates a new unique address instance.
        /// </summary>
        /// <param name="address">The original Akka <see cref="Address"/></param>
        /// <param name="uid">The UID for the cluster instance.</param>
        public UniqueAddress(Address address, int uid)
        {
            Uid = uid;
            Address = address;
        }

        /// <summary>
        /// Compares two unique address instances to each other.
        /// </summary>
        /// <param name="other">The other address to compare to.</param>
        /// <returns><c>true</c> if equal, <c>false</c> otherwise.</returns>
        public bool Equals(UniqueAddress other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;

            return Uid == other.Uid && Address.Equals(other.Address);
        }

        /// <inheritdoc cref="object.Equals(object)"/>
        public override bool Equals(object obj) => obj is UniqueAddress addr && Equals(addr);

        /// <inheritdoc cref="object.GetHashCode"/>
        public override int GetHashCode()
        {
            return Uid.GetHashCode();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="uniqueAddress">TBD</param>
        /// <returns>TBD</returns>
        public int CompareTo(UniqueAddress uniqueAddress) => CompareTo(uniqueAddress, Address.Comparer);

        int IComparable.CompareTo(object obj)
        {
            if (obj is UniqueAddress address) return CompareTo(address);

            throw new ArgumentException($"Cannot compare {nameof(UniqueAddress)} with instance of type '{obj?.GetType().FullName ?? "null"}'.");
        }

        internal int CompareTo(UniqueAddress uniqueAddress, IComparer<Address> addresComparer)
        {
            if (uniqueAddress is null) throw new ArgumentNullException(nameof(uniqueAddress));

            var result = addresComparer.Compare(Address, uniqueAddress.Address);
            return result == 0 ? Uid.CompareTo(uniqueAddress.Uid) : result;
        }

        /// <inheritdoc cref="object.ToString"/>
        public override string ToString() => $"UniqueAddress: ({Address}, {Uid})";

        #region operator overloads

        /// <summary>
        /// Compares two specified unique addresses for equality.
        /// </summary>
        /// <param name="left">The first unique address used for comparison</param>
        /// <param name="right">The second unique address used for comparison</param>
        /// <returns><c>true</c> if both unique addresses are equal; otherwise <c>false</c></returns>
        public static bool operator ==(UniqueAddress left, UniqueAddress right)
            => Equals(left, right);

        /// <summary>
        /// Compares two specified unique addresses for inequality.
        /// </summary>
        /// <param name="left">The first unique address used for comparison</param>
        /// <param name="right">The second unique address used for comparison</param>
        /// <returns><c>true</c> if both unique addresses are not equal; otherwise <c>false</c></returns>
        public static bool operator !=(UniqueAddress left, UniqueAddress right)
            => !Equals(left, right);

        public static bool operator <(UniqueAddress left, UniqueAddress right)
            => left is null ? right is object : left.CompareTo(right) < 0;

        public static bool operator <=(UniqueAddress left, UniqueAddress right)
            => left is null || left.CompareTo(right) <= 0;

        public static bool operator >(UniqueAddress left, UniqueAddress right)
            => left is object && left.CompareTo(right) > 0;

        public static bool operator >=(UniqueAddress left, UniqueAddress right)
            => left is null ? right is null : left.CompareTo(right) >= 0;

        #endregion
    }
}
