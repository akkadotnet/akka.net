using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Cluster.Tests
{
    static class TestMember
    {
        public static Member Create(Address address, MemberStatus status)
        {
            return Create(address, status, new HashSet<string>());
        }

        public static Member Create(Address address, MemberStatus status, HashSet<string> roles)
        {
            return new Member(new UniqueAddress(address, 0), status, roles);
        }
    }
}
