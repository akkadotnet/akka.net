using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Cluster
{
    //TODO: Keep an eye on concurrency / immutability
    class Member : IComparable<Member>
    {
        public readonly static HashSet<Member> None = new HashSet<Member>();
                
        public static Member Create(UniqueAddress uniqueAddress, HashSet<string> roles)
        {
            return new Member(uniqueAddress, int.MaxValue, MemberStatus.Joining, roles);
        }

        public static Member Removed(UniqueAddress node)
        {
            return new Member(node, int.MaxValue, MemberStatus.Removed, new HashSet<string>());
        }

        readonly UniqueAddress _uniqueAddress;
        public UniqueAddress UniqueAddress { get { return _uniqueAddress; } }

        readonly int _upNumber;
        public int UpNumber { get { return _upNumber; } }

        readonly MemberStatus _status;
        public MemberStatus Status { get { return _status; } }

        readonly HashSet<string> _roles;
        public HashSet<string> Roles { get { return _roles; } }

        public Member(UniqueAddress uniqueAddress, MemberStatus status, HashSet<string> roles) 
            : this(uniqueAddress, 0, status, roles)
        {
        }

        public Member(UniqueAddress uniqueAddress, int upNumber, MemberStatus status, HashSet<string> roles)
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

        //TODO: Ignoring bits of Java api. Not sure of consequences of this
        public bool IsOlderThan(Member other)
        {
            return UpNumber < other.UpNumber;
        }

        public Member Copy(MemberStatus status)
        {
            //TODO: Akka exception?
            if(!AllowedTransitions[Status].Contains(status))
                throw new InvalidOperationException(String.Format("Invalid member status transition {0} -> {1}", Status, status));
            //TODO: Copying should create new?
            return new Member(UniqueAddress, UpNumber, Status, Roles);
        }

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

        public static readonly Dictionary<MemberStatus, HashSet<MemberStatus>> AllowedTransitions = 
            new Dictionary<MemberStatus, HashSet<MemberStatus>>
            {
                {MemberStatus.Joining, new HashSet<MemberStatus>{MemberStatus.Up, MemberStatus.Down, MemberStatus.Removed}},
                {MemberStatus.Up, new HashSet<MemberStatus>{MemberStatus.Leaving, MemberStatus.Down, MemberStatus.Removed}},
                {MemberStatus.Leaving, new HashSet<MemberStatus>{MemberStatus.Exiting, MemberStatus.Down, MemberStatus.Removed}},
                {MemberStatus.Down, new HashSet<MemberStatus>{MemberStatus.Removed}},
                {MemberStatus.Exiting, new HashSet<MemberStatus>{MemberStatus.Removed, MemberStatus.Down}},
                {MemberStatus.Removed, new HashSet<MemberStatus>{}}
            };

        public static readonly MemberComparer Ordering = new MemberComparer();
        public class MemberComparer : IComparer<Member>
        {
            public int Compare(Member x, Member y)
            {
                return x.UniqueAddress.CompareTo(y.UniqueAddress);
            }
        }
    }

    enum MemberStatus
    {
        Joining,
        Up,
        Leaving,
        Exiting,
        Down,
        Removed
    }

    class UniqueAddress : IComparable<UniqueAddress>
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

        #region IComparable<Address>

        public int CompareTo(UniqueAddress that)
        {
            var result = Member.AddressOrdering.Compare(Address, that.Address);
            if (result == 0)
                if (Uid < that.Uid) return -1;
                else if (Uid == that.Uid) return 0;
                else return 1;
            return result;
        }

        #endregion
    }
}
