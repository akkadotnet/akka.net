//-----------------------------------------------------------------------
// <copyright file="Member.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
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
        public readonly static ImmutableHashSet<Member> None = ImmutableHashSet.Create<Member>();
                
        public static Member Create(UniqueAddress uniqueAddress, ImmutableHashSet<string> roles)
        {
            return new Member(uniqueAddress, int.MaxValue, MemberStatus.Joining, roles);
        }

        public static Member Removed(UniqueAddress node)
        {
            return new Member(node, int.MaxValue, MemberStatus.Removed, ImmutableHashSet.Create<string>());
        }

        readonly UniqueAddress _uniqueAddress;
        public UniqueAddress UniqueAddress { get { return _uniqueAddress; } }

        readonly int _upNumber;

        /// <summary>
        /// TODO: explain what this does
        /// </summary>
        internal int UpNumber { get { return _upNumber; } }

        readonly MemberStatus _status;
        public MemberStatus Status { get { return _status; } }

        readonly ImmutableHashSet<string> _roles;
        public ImmutableHashSet<string> Roles { get { return _roles; } }

        public static Member Create(UniqueAddress uniqueAddress, MemberStatus status, ImmutableHashSet<string> roles)
        {
            return new Member(uniqueAddress, 0, status, roles);
        }

        Member(UniqueAddress uniqueAddress, int upNumber, MemberStatus status, ImmutableHashSet<string> roles)
        {
            _uniqueAddress = uniqueAddress;
            _upNumber = upNumber;
            _status = status;
            _roles = roles;
        }

        public Address Address { get { return UniqueAddress.Address; } }

        public override int GetHashCode()
        {
            return _uniqueAddress.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            var m = obj as Member;
            if (m == null) return false;
            return _uniqueAddress.Equals(m.UniqueAddress);
        }

        public int CompareTo(Member other)
        {
            return Ordering.Compare(this, other);
        }

        public override string ToString()
        {
            return String.Format("Member(address = {0}, status = {1}", Address, Status);
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
            return UpNumber < other.UpNumber;
        }

        public Member Copy(MemberStatus status)
        {
            var oldStatus = _status;
            if (status == oldStatus) return this;

            //TODO: Akka exception?
            if (!AllowedTransitions[oldStatus].Contains(status))
                throw new InvalidOperationException(String.Format("Invalid member status transition {0} -> {1}", Status, status));
            
            return new Member(_uniqueAddress, _upNumber, status, _roles);
        }

        public Member CopyUp(int upNumber)
        {
            return new Member(_uniqueAddress, upNumber, _status, _roles).Copy(status: MemberStatus.Up);
        }

        /// <summary>
        ///  `Address` ordering type class, sorts addresses by host and port.
        /// </summary>
        public static readonly AddressComparer AddressOrdering = new AddressComparer();
        public class AddressComparer : IComparer<Address>
        {
            public int Compare(Address x, Address y)
            {
                if (x.Equals(y)) return 0;
                if (!x.Host.Equals(y.Host)) return String.Compare(x.Host.GetOrElse(""), y.Host.GetOrElse(""), StringComparison.Ordinal);
                if (!x.Port.Equals(y.Port)) return Nullable.Compare(x.Port.GetOrElse(0), (y.Port.GetOrElse(0)));
                return 0;
            }
        }

        /// <summary>
        /// Orders the members by their address except that members with status
        /// Joining, Exiting and Down are ordered last (in that order).
        /// </summary>
        public static readonly LeaderStatusMemberComparer LeaderStatusOrdering = new LeaderStatusMemberComparer();
        public class LeaderStatusMemberComparer : IComparer<Member>
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
        public static readonly MemberComparer Ordering = new MemberComparer();
        public class MemberComparer : IComparer<Member>
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
            return m1; //case (Up, Up)     ⇒ m1
        }

        public static readonly ImmutableDictionary<MemberStatus, ImmutableHashSet<MemberStatus>> AllowedTransitions =
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
    public class UniqueAddress : IComparable<UniqueAddress>
    {
        readonly Address _address;
        public Address Address { get { return _address;} }
        
        readonly int _uid;
        public int Uid { get { return _uid; } }

        public UniqueAddress(Address address, int uid)
        {
            _uid = uid;
            _address = address;
        }

        public override bool Equals(object obj)
        {
            var u = obj as UniqueAddress;
            if (u == null) return false;
            return Uid.Equals(u.Uid) && Address.Equals(u.Address);
        }

        public override int GetHashCode()
        {
            return _uid;
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

        public override string ToString()
        {
            return string.Format("UniqueAddress: ({0}, {1})", Address, _uid);
        }

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

