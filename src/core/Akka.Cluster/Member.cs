//-----------------------------------------------------------------------
// <copyright file="Member.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Cluster
{
    //TODO: Keep an eye on concurrency / immutability
    //TODO: Comments
    /// <summary>
    /// Represents the address, current status, and roles of a cluster member node.
    /// 
    /// Note: `hashCode` and `equals` are solely based on the underlying `Address`, not its `MemberStatus`
    /// and roles.
    /// </summary>
    public class Member : IComparable<Member>
    {
        internal static Member Create(UniqueAddress uniqueAddress, ImmutableHashSet<string> roles)
        {
            return new Member(uniqueAddress, int.MaxValue, MemberStatus.Joining, roles);
        }

        internal static Member Removed(UniqueAddress node)
        {
            return new Member(node, int.MaxValue, MemberStatus.Removed, ImmutableHashSet.Create<string>());
        }

        public UniqueAddress UniqueAddress { get; }

        internal int UpNumber { get; }

        public MemberStatus Status { get; }

        public ImmutableHashSet<string> Roles { get; }

        internal static Member Create(UniqueAddress uniqueAddress, int upNumber, MemberStatus status, ImmutableHashSet<string> roles)
        {
            return new Member(uniqueAddress, upNumber, status, roles);
        }

        internal Member(UniqueAddress uniqueAddress, int upNumber, MemberStatus status, ImmutableHashSet<string> roles)
        {
            UniqueAddress = uniqueAddress;
            UpNumber = upNumber;
            Status = status;
            Roles = roles;
        }

        public Address Address { get { return UniqueAddress.Address; } }

        public override int GetHashCode()
        {
            return UniqueAddress.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var m = obj as Member;
            if (m == null) return false;
            return UniqueAddress.Equals(m.UniqueAddress);
        }

        public int CompareTo(Member other)
        {
            return Ordering.Compare(this, other);
        }

        public override string ToString()
        {
            return $"Member(address = {Address}, status = {Status}, role=[{string.Join(",", Roles)}], upNumber={UpNumber})";
        }

        public bool HasRole(string role)
        {
            return Roles.Contains(role);
        }

        /// <summary>
        /// Is this member older, has been part of cluster longer, than another
        /// member. It is only correct when comparing two existing members in a
        /// cluster. A member that joined after removal of another member may be
        /// considered older than the removed member.
        /// </summary>
        public bool IsOlderThan(Member other)
        {
            if (UpNumber.Equals(other.UpNumber))
            {
                return Member.AddressOrdering.Compare(Address, other.Address) < 0;
            }

            return UpNumber < other.UpNumber;
        }

        public Member Copy(MemberStatus status)
        {
            var oldStatus = Status;
            if (status == oldStatus) return this;

            //TODO: Akka exception?
            if (!AllowedTransitions[oldStatus].Contains(status))
                throw new InvalidOperationException(String.Format("Invalid member status transition {0} -> {1}", Status, status));
            
            return new Member(UniqueAddress, UpNumber, status, Roles);
        }

        public Member CopyUp(int upNumber)
        {
            return new Member(UniqueAddress, upNumber, Status, Roles).Copy(status: MemberStatus.Up);
        }

        /// <summary>
        ///  `Address` ordering type class, sorts addresses by host and port.
        /// </summary>
        public static readonly IComparer<Address> AddressOrdering = new AddressComparer();
        internal class AddressComparer : IComparer<Address>
        {
            public int Compare(Address x, Address y)
            {
                if (x.Equals(y)) return 0;
                if (!x.Host.Equals(y.Host)) return String.Compare(x.Host.GetOrElse(""), y.Host.GetOrElse(""), StringComparison.Ordinal);
                if (!x.Port.Equals(y.Port)) return Nullable.Compare(x.Port.GetOrElse(0), (y.Port.GetOrElse(0)));
                return 0;
            }
        }

        internal static readonly AgeComparer AgeOrdering = new AgeComparer();
        internal class AgeComparer : IComparer<Member>
        {
            public int Compare(Member a, Member b)
            {
                if (a.Equals(b)) return 0;
                if (a.IsOlderThan(b)) return -1;
                return 1;
            }
        }

        /// <summary>
        /// Orders the members by their address except that members with status
        /// Joining, Exiting and Down are ordered last (in that order).
        /// </summary>
        internal static readonly LeaderStatusMemberComparer LeaderStatusOrdering = new LeaderStatusMemberComparer();
        internal class LeaderStatusMemberComparer : IComparer<Member>
        {
            public int Compare(Member a, Member b)
            {
                var @as = a.Status;
                var bs = b.Status;
                if (@as == bs) return Ordering.Compare(a, b);
                if (@as == MemberStatus.Down) return 1;
                if (@bs == MemberStatus.Down) return -1;
                if (@as == MemberStatus.Exiting) return 1;
                if (@bs == MemberStatus.Exiting) return -1;
                if (@as == MemberStatus.Joining) return 1;
                if (@bs == MemberStatus.Joining) return -1;
                return Ordering.Compare(a, b);
            }
        }

        /// <summary>
        /// `Member` ordering type class, sorts members by host and port.
        /// </summary>
        internal static readonly MemberComparer Ordering = new MemberComparer();
        internal class MemberComparer : IComparer<Member>
        {
            public int Compare(Member x, Member y)
            {
                return x.UniqueAddress.CompareTo(y.UniqueAddress);
            }
        }

        public static ImmutableHashSet<Member> PickHighestPriority(IEnumerable<Member> a, IEnumerable<Member> b)
        {
            // group all members by Address => Seq[Member]
            var groupedByAddress = (a.Concat(b)).GroupBy(x => x.UniqueAddress);

            var acc = new HashSet<Member>();

            foreach (var g in groupedByAddress)
            {
                if (g.Count() == 2) acc.Add(HighestPriorityOf(g.First(), g.Skip(1).First()));
                else
                {
                    var m = g.First();
                    if (!Gossip.RemoveUnreachableWithMemberStatus.Contains(m.Status)) acc.Add(m);
                }
            }
            return acc.ToImmutableHashSet();
        }

        /// <summary>
        /// Picks the Member with the highest "priority" MemberStatus.
        /// </summary>
        public static Member HighestPriorityOf(Member m1, Member m2)
        {
            if (m1.Status.Equals(m2.Status))
            {
                return m1.IsOlderThan(m2) ? m1 : m2;
            }

            var m1Status = m1.Status;
            var m2Status = m2.Status;
            if (m1Status == MemberStatus.Removed) return m1;
            if (m2Status == MemberStatus.Removed) return m2;
            if (m1Status == MemberStatus.Down) return m1;
            if (m2Status == MemberStatus.Down) return m2;
            if (m1Status == MemberStatus.Exiting) return m1;
            if (m2Status == MemberStatus.Exiting) return m2;
            if (m1Status == MemberStatus.Leaving) return m1;
            if (m2Status == MemberStatus.Leaving) return m2;
            if (m1Status == MemberStatus.Joining) return m2;
            if (m2Status == MemberStatus.Joining) return m1;
            return m1;
        }

        internal static readonly ImmutableDictionary<MemberStatus, ImmutableHashSet<MemberStatus>> AllowedTransitions =
            new Dictionary<MemberStatus, ImmutableHashSet<MemberStatus>>
            {
                {MemberStatus.Joining, ImmutableHashSet.Create(MemberStatus.Up, MemberStatus.Down, MemberStatus.Removed)},
                {MemberStatus.Up, ImmutableHashSet.Create(MemberStatus.Leaving, MemberStatus.Down, MemberStatus.Removed)},
                {MemberStatus.Leaving, ImmutableHashSet.Create(MemberStatus.Exiting, MemberStatus.Down, MemberStatus.Removed)},
                {MemberStatus.Down, ImmutableHashSet.Create(MemberStatus.Removed)},
                {MemberStatus.Exiting, ImmutableHashSet.Create(MemberStatus.Removed, MemberStatus.Down)},
                {MemberStatus.Removed, ImmutableHashSet.Create<MemberStatus>()}
            }.ToImmutableDictionary();
    }


    /// <summary>
    /// Defines the current status of a cluster member node
    /// 
    /// Can be one of: Joining, Up, Leaving, Exiting and Down.
    /// </summary>
    public enum MemberStatus
    {
        Joining,
        Up,
        Leaving,
        Exiting,
        Down,
        Removed
    }

    /// <summary>
    /// Member identifier consisting of address and random `uid`.
    /// The `uid` is needed to be able to distinguish different
    /// incarnations of a member with same hostname and port.
    /// </summary>
    public class UniqueAddress : IComparable<UniqueAddress>, IEquatable<UniqueAddress>
    {
        public Address Address { get; }

        public int Uid { get; }

        public UniqueAddress(Address address, int uid)
        {
            Uid = uid;
            Address = address;
        }

        public bool Equals(UniqueAddress other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Uid == other.Uid && Address.Equals(other.Address);
        }

        public override bool Equals(object obj) => obj is UniqueAddress && Equals((UniqueAddress) obj);

        public override int GetHashCode()
        {
            return Uid;
        }

        public int CompareTo(UniqueAddress that)
        {
            var result = Member.AddressOrdering.Compare(Address, that.Address);
            if (result == 0)
                if (Uid < that.Uid) return -1;
                else if (Uid == that.Uid) return 0;
                else return 1;
            return result;
        }

        public override string ToString() => $"UniqueAddress: ({Address}, {Uid})";

        #region operator overloads

        public static bool operator ==(UniqueAddress left, UniqueAddress right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(UniqueAddress left, UniqueAddress right)
        {
            return !Equals(left, right);
        }

        #endregion
    }
}

