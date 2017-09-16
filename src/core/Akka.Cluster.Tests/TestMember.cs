//-----------------------------------------------------------------------
// <copyright file="TestMember.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Cluster.Tests
{
    static class TestMember
    {
        public static Member Create(Address address, MemberStatus status, int uid = 0)
        {
            return Create(address, status, ImmutableHashSet.Create<string>(), uid);
        }

        public static Member Create(Address address, MemberStatus status, ImmutableHashSet<string> roles, int uid = 0, int upNumber = 0)
        {
            return Member.Create(new UniqueAddress(address, uid), upNumber, status, roles);
        }
    }
}

