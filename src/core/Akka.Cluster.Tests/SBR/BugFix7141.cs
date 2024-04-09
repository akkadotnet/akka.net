// -----------------------------------------------------------------------
//  <copyright file="UncleanIndirectlyConnectedSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Event;
using Akka.Util;
using FluentAssertions;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

namespace Akka.Cluster.Tests.SBR;

public class BugFix7141
{
    private readonly ITestOutputHelper _log;
    private readonly Akka.Cluster.SBR.KeepMajority _resolver;

    public BugFix7141(ITestOutputHelper output)
    {
        _log = output;
        // create split brain resolver
        _resolver = new Akka.Cluster.SBR.KeepMajority(string.Empty);
    }

    [Fact]
    public void ShouldWork()
    {
        // arrange

        // create unique addresses for members
        var address1 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6001), 1);
        var address2 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6002), 2);
        var address3 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6003), 3);
        var address4 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6004), 4);
        var address5 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6005), 5);
        var address6 = new UniqueAddress(new Address("akka.tcp", "Sys", "localhost", 6006), 6);

        // create members
        var member1 = new Member(address1, 5, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member2 = new Member(address2, 4, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member3 = new Member(address3, 3, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member4 = new Member(address4, 2, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member5 = new Member(address5, 1, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);
        var member6 = new Member(address6, 0, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero);

        #region Cluster events reproduction

        // Node 1 went up
        Up(address1);

        // Leader changed to Node 1
        // Seen changed
        SeenBy(address1);
        
        // Rest of cluster joining
        Joining(address6);
        Joining(address3);
        Joining(address2);
        Joining(address5);
        Joining(address4);
        
        // Rest of cluster up
        Up(address3);
        Up(address5);
        Up(address2);
        Up(address4);
        Up(address6);
        
        // Leader changed to node 3
        // Seen changed
        SeenBy(address1);
        SeenBy(address1, address2);
        SeenBy(address6, address1, address2);
        SeenBy(address3, address6, address1, address2);
        SeenBy(address3, address6, address4, address1, address2);
        SeenBy(address3, address5, address6, address4, address1, address2);
        
        // First unreachable reported
        Unreachable(address3);
        
        // Leader passed back to node 1
        // seen changed
        SeenBy(address1);
        
        // reachability changed, unreachable: 1 -> 3
        _resolver.SetReachability(Reachability.Empty.Unreachable(address1, address3));
        
        // seen changed
        SeenBy(address5, address1);
        
        // reachability changed, unreachable: 1 -> 3, 5 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3));
        
        // reachability changed, unreachable: 1 -> 3, 5 -> 3, happened twice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3));
        
        // seen changed
        SeenBy(address1);
        
        // reachability changed, unreachable: 1 -> 3, 5 -> 3, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address2, address3));
        
        // seen changed
        SeenBy(address1, address2);
        
        // reachability changed, unreachable: 1 -> 3, 5 -> 3, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address2, address3));
        
        // reachability changed, unreachable: 1 -> 3, 5 -> 3, 6-> 3, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address3)
            .Unreachable(address2, address3));
        
        // seen changed
        SeenBy(address5, address6, address1, address2);
        
        // reachability changed, unreachable: 1 -> 3, 5 -> 3, 6-> 3, 2 -> 3, happened twice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address3)
            .Unreachable(address2, address3));
        
        // seen changed
        SeenBy(address5, address6, address1, address2);

        // reachability changed, unreachable: 1 -> 3, 5 -> 3, 6-> 3, 2 -> 3, happened thrice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address3)
            .Unreachable(address2, address3));

        // Second unreachable
        Unreachable(address4);
        
        // seen changed
        SeenBy(address1);
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 3, 5 -> 3, 6-> 3, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address3)
            .Unreachable(address2, address3));
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 3, 5 -> 3, 6-> 3, 2 -> 4, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address3));
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 3, 5 -> 3, 6 -> 4, 6-> 3, 2 -> 4, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address3));
        
        // seen changed
        SeenBy(address6, address1);
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 3, 5 -> 3, 6 -> 4, 6-> 3, 2 -> 4, 2 -> 3, happened twice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address3));

        // seen changed
        SeenBy(address6, address1, address2);
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 3, 5 -> 3, 6 -> 4, 6-> 3, 2 -> 4, 2 -> 3, happened thrice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address3));
        
        // Third unreachable
        Unreachable(address5);
        
        // seen changed
        SeenBy(address1);

        // reachability changed, unreachable: 1 -> 4, 1 -> 5, 1 -> 3, 5 -> 3, 6 -> 4, 6-> 3, 2 -> 4, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address5)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address3));
        
        // seen changed
        SeenBy(address1, address2);
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 5, 1 -> 3, 5 -> 3, 6 -> 4, 6-> 3, 2 -> 4, 2 -> 3, happened twice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address5)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address3));
        
        // seen changed
        SeenBy(address6, address1, address2);
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 5, 1 -> 3, 5 -> 3, 6 -> 4, 6 -> 5, 6-> 3, 2 -> 4, 2 -> 5, 2 -> 3
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address5)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address5)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address5)
            .Unreachable(address2, address3));
        
        
        // reachability changed, unreachable: 1 -> 4, 1 -> 5, 1 -> 3, 5 -> 3, 6 -> 4, 6 -> 5, 6-> 3, 2 -> 4, 2 -> 5, 2 -> 3, happened twice
        _resolver.SetReachability(Reachability.Empty
            .Unreachable(address1, address4)
            .Unreachable(address1, address5)
            .Unreachable(address1, address3)
            .Unreachable(address5, address3)
            .Unreachable(address6, address4)
            .Unreachable(address6, address5)
            .Unreachable(address6, address3)
            .Unreachable(address2, address4)
            .Unreachable(address2, address5)
            .Unreachable(address2, address3));
        #endregion
        
        var expectedDown = new[] { address2, address3, address4 }.ToImmutableHashSet();
        ImmutableHashSet<UniqueAddress> downedNodes = null;
        Invoking(() => downedNodes = _resolver.NodesToDown()).Should().NotThrow();
        downedNodes.Should().BeEquivalentTo(expectedDown);
    }

    private void Joining(UniqueAddress address)
        => _resolver.Add(Member.Create(address, 0, MemberStatus.Joining, ImmutableHashSet.Create("role1"), AppVersion.Zero));
    
    private void Up(UniqueAddress address)
        => _resolver.Add(Member.Create(address, 0, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero));
    
    private void Unreachable(UniqueAddress address)
        => _resolver.AddUnreachable(Member.Create(address, 0, MemberStatus.Up, ImmutableHashSet.Create("role1"), AppVersion.Zero));

    private void SeenBy(params UniqueAddress[] addresses)
        => _resolver.SetSeenBy(addresses.Select(a => a.Address).ToImmutableHashSet());
}