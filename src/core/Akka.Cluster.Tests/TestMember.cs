//-----------------------------------------------------------------------
// <copyright file="TestMember.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Annotations;
using Akka.Util;

namespace Akka.Cluster.Tests
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    [InternalApi]
    public static class TestMember
    {
        public static Member Create(Address address, MemberStatus status, int uid = 0, AppVersion appVersion = null)
        {
            return Create(address, status, ImmutableHashSet.Create<string>(), uid, appVersion: appVersion);
        }

        public static Member Create(Address address, MemberStatus status, ImmutableHashSet<string> roles, int uid = 0, int upNumber = 0, AppVersion appVersion = null)
        {
            return Member.Create(new UniqueAddress(address, uid), upNumber, status, roles, appVersion ?? AppVersion.Zero);
        }
    }
}

