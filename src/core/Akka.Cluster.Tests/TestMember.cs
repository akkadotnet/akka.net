//-----------------------------------------------------------------------
// <copyright file="TestMember.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;

namespace Akka.Cluster.Tests
{
    static class TestMember
    {
        public static Member Create(Address address, MemberStatus status)
        {
            return Create(address, status, ImmutableHashSet.Create<string>());
        }

        public static Member Create(Address address, MemberStatus status, ImmutableHashSet<string> roles)
        {
            return Member.Create(new UniqueAddress(address, 0), status, roles);
        }
    }
}

