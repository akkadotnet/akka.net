//-----------------------------------------------------------------------
// <copyright file="TestMember.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Cluster.Tests
{
    static class TestMember
    {
        public static Member Create(Address address, MemberStatus status) => 
            Create(address, status, ImmutableHashSet<string>.Empty);

        public static Member Create(Address address, MemberStatus status, int upNumber, string dataCenter) =>
            Create(address, status, ImmutableHashSet<string>.Empty, dataCenter, upNumber);
        
        public static Member Create(Address address, MemberStatus status, ImmutableHashSet<string> roles, string dataCenter = ClusterSettings.DefaultDataCenter, int upNumber = int.MaxValue) =>
            WithUniqueAddress(new UniqueAddress(address, 0), status, roles, dataCenter, upNumber);
        
        public static Member WithUniqueAddress(UniqueAddress address, MemberStatus status, ImmutableHashSet<string> roles, string dataCenter, int upNumber = int.MaxValue) =>
            new Member(address, upNumber, status, roles.Add(ClusterSettings.DcRolePrefix + dataCenter));
    }
}

