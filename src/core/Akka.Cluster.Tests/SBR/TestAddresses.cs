//-----------------------------------------------------------------------
// <copyright file="TestAddresses.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util;

namespace Akka.Cluster.Tests.SBR
{
    public static class TestAddresses
    {
        public static Member MemberA { get; }
        public static Member MemberB { get; }
        public static Member MemberC { get; }
        public static Member MemberD { get; }
        public static Member MemberE { get; }
        public static Member MemberF { get; }
        public static Member MemberG { get; }

        public static Member MemberAWeaklyUp { get; }
        public static Member MemberBWeaklyUp { get; }

        static TestAddresses()
        {
            var addressA = new Address("akka.tcp", "sys", "a", 2552);
            MemberA =
                new Member(
                    new UniqueAddress(addressA, 0),
                    5,
                    MemberStatus.Up,
                    new[] { "role3" },
                    AppVersion.Zero);
            MemberB =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "b", addressA.Port), 0),
                      4,
                      MemberStatus.Up,
                      new[] { "role1", "role3" },
                      AppVersion.Zero);
            MemberC =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "c", addressA.Port), 0),
                      3,
                      MemberStatus.Up,
                      new[] { "role2" },
                      AppVersion.Zero);
            MemberD =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "d", addressA.Port), 0),
                      2,
                      MemberStatus.Up,
                      new[] { "role1", "role2", "role3" },
                      AppVersion.Zero);
            MemberE =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "e", addressA.Port), 0),
                      1,
                      MemberStatus.Up,
                      new string[] { },
                      AppVersion.Zero);
            MemberF =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "f", addressA.Port), 0),
                      5,
                      MemberStatus.Up,
                      new string[] { },
                      AppVersion.Zero);
            MemberG =
                new Member(
                      new UniqueAddress(new Address(addressA.Protocol, addressA.System, "g", addressA.Port), 0),
                      6,
                      MemberStatus.Up,
                      new string[] { },
                      AppVersion.Zero);

            MemberAWeaklyUp = new Member(MemberA.UniqueAddress, int.MaxValue, MemberStatus.WeaklyUp, MemberA.Roles, AppVersion.Zero);
            MemberBWeaklyUp = new Member(MemberB.UniqueAddress, int.MaxValue, MemberStatus.WeaklyUp, MemberB.Roles, AppVersion.Zero);
        }

        public static Member Joining(Member m) => Member.Create(m.UniqueAddress, m.Roles, AppVersion.Zero);

        public static Member Leaving(Member m) => m.Copy(MemberStatus.Leaving);

        public static Member Exiting(Member m) => Leaving(m).Copy(MemberStatus.Exiting);

        public static Member Downed(Member m) => m.Copy(MemberStatus.Down);
    }
}
