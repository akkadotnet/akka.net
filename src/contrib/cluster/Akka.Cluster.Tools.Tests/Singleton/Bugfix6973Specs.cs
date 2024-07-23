// -----------------------------------------------------------------------
//  <copyright file="Bugfix6973Specs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tests;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton;

public class Bugfix6973Specs
{
    private static readonly Address Address1 = new("akka.tcp", "sys", "host1", 2552);
    private static readonly Member Member1 = TestMember.Create(Address1, MemberStatus.Up, roles:ImmutableHashSet<string>.Empty, upNumber:1);
    private static readonly Address Address2 = new("akka.tcp", "sys", "host2", 2552);
    private static readonly Member Member2 = TestMember.Create(Address2, MemberStatus.Up, roles:ImmutableHashSet<string>.Empty, upNumber:2);
    private static readonly Address Address3 = new("akka.tcp", "sys", "host3", 2552);
    private static readonly Member Member3 = TestMember.Create(Address3, MemberStatus.Up, roles:ImmutableHashSet<string>.Empty, upNumber:3);

    [Fact]
    public void MembersShouldBeKeptInAgeOrder()
    {
        var membersByAge = ImmutableSortedSet<Member>.Empty.WithComparer(Member.AgeOrdering);
        membersByAge = membersByAge.Add(Member1).Add(Member3).Add(Member2);
        var me = Member2;

        membersByAge.First().Should().Be(Member1);
        
        var selfUpNumber = membersByAge.Where(c => c.UniqueAddress == me.UniqueAddress)
            .Select(c => c.UpNumber)
            .FirstOrDefault();
        selfUpNumber.Should().Be(2);
        
        var oldest = membersByAge.TakeWhile(m => m.UpNumber <= selfUpNumber).Select(c => c.UniqueAddress).ToList();
        oldest.SequenceEqual(new []{Member1.UniqueAddress, Member2.UniqueAddress}).Should().BeTrue();
        
        
    }
}