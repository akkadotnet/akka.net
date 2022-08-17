// -----------------------------------------------------------------------
//  <copyright file="MemberAgeOrderingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton
{
    public class MemberAgeOrderingSpec
    {
        [Fact(DisplayName = "MemberAgeOrdering should sort based on UpNumber")]
        public void SortByUpNumberTest()
        {
            var members = new SortedSet<Member>(MemberAgeOrdering.DescendingWithAppVersion)
            {
                Create(Address.Parse("akka://sys@darkstar:1112"), upNumber: 3),
                Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 1),
                Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 9),
            };

            var seq = members.ToList();
            seq.Count.Should().Be(3);
            seq[0].Should().Be(Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 1));
            seq[1].Should().Be(Create(Address.Parse("akka://sys@darkstar:1112"), upNumber: 3));
            seq[2].Should().Be(Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 9));
        }
        
        [Fact(DisplayName = "MemberAgeOrdering should sort based on Address if UpNumber is the same")]
        public void SortByAddressTest()
        {
            var members = new SortedSet<Member>(MemberAgeOrdering.DescendingWithAppVersion)
            {
                Create(Address.Parse("akka://sys@darkstar:1112"), upNumber: 1),
                Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 1),
                Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 1),
            };

            var seq = members.ToList();
            seq.Count.Should().Be(3);
            seq[0].Should().Be(Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 1));
            seq[1].Should().Be(Create(Address.Parse("akka://sys@darkstar:1112"), upNumber: 1));
            seq[2].Should().Be(Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 1));
        }
        
        [Fact(DisplayName = "MemberAgeOrdering should prefer AppVersion over UpNumber")]
        public void SortByAppVersionTest()
        {
            var members = new SortedSet<Member>(MemberAgeOrdering.DescendingWithAppVersion)
            {
                Create(Address.Parse("akka://sys@darkstar:1112"), upNumber: 3, appVersion: AppVersion.Create("1.0.0")),
                Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 1, appVersion: AppVersion.Create("1.0.0")),
                Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 2, appVersion: AppVersion.Create("1.0.0")),
                Create(Address.Parse("akka://sys@darkstar:1114"), upNumber: 7, appVersion: AppVersion.Create("1.0.2")),
                Create(Address.Parse("akka://sys@darkstar:1115"), upNumber: 8, appVersion: AppVersion.Create("1.0.2")),
                Create(Address.Parse("akka://sys@darkstar:1116"), upNumber: 6, appVersion: AppVersion.Create("1.0.2")),
            };

            var seq = members.ToList();
            seq.Count.Should().Be(6);
            seq[0].Should().Be(Create(Address.Parse("akka://sys@darkstar:1116"), upNumber: 6, appVersion: AppVersion.Create("1.0.2")));
            seq[1].Should().Be(Create(Address.Parse("akka://sys@darkstar:1114"), upNumber: 7, appVersion: AppVersion.Create("1.0.2")));
            seq[2].Should().Be(Create(Address.Parse("akka://sys@darkstar:1115"), upNumber: 8, appVersion: AppVersion.Create("1.0.2")));
            seq[3].Should().Be(Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 1, appVersion: AppVersion.Create("1.0.0")));
            seq[4].Should().Be(Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 2, appVersion: AppVersion.Create("1.0.0")));
            seq[5].Should().Be(Create(Address.Parse("akka://sys@darkstar:1112"), upNumber: 3, appVersion: AppVersion.Create("1.0.0")));
        }
        
        public static Member Create(
            Address address,
            MemberStatus status = MemberStatus.Up,
            ImmutableHashSet<string> roles = null,
            int uid = 0,
            int upNumber = 0,
            AppVersion appVersion = null)
        {
            return Member.Create(new UniqueAddress(address, uid), upNumber, status, roles ?? ImmutableHashSet<string>.Empty, appVersion ?? AppVersion.Zero);
        }
    }
}