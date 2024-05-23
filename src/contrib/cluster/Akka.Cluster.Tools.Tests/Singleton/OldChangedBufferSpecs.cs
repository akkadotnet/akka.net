// -----------------------------------------------------------------------
//  <copyright file="OldChangedBufferSpecs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------
#nullable enable
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.Tools.Singleton;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tools.Tests.Singleton;

/// <summary>
/// Reproduction for https://github.com/akkadotnet/akka.net/issues/7196 - clearly, what we did
/// </summary>
// public class OldChangedBufferSpecs : AkkaSpec
// {
//     public IActorRef CreateOldestChangedBuffer(string role, bool considerAppVersion)
//     {
//         return Sys.ActorOf(Props.Create(() => new OldestChangedBuffer(role, considerAppVersion)));
//     }
//     
//     private readonly ActorSystem _otherNodeV1;
//     private readonly ActorSystem _nonHostingNode;
//     private ActorSystem? _otherNodeV2;
//     
//     public OldChangedBufferSpecs(ITestOutputHelper output) : base("""
//                                           
//                                                         akka.loglevel = INFO
//                                                         akka.actor.provider = "cluster"
//                                                         akka.cluster.roles = [singleton]
//                                                         akka.cluster.auto-down-unreachable-after = 2s
//                                                         akka.cluster.singleton.min-number-of-hand-over-retries = 5
//                                                         akka.remote {
//                                                           dot-netty.tcp {
//                                                             hostname = "127.0.0.1"
//                                                             port = 0
//                                                           }
//                                                         }
//                                           """, output)
//     {
//         _otherNodeV1 = ActorSystem.Create(Sys.Name, Sys.Settings.Config);
//         _nonHostingNode = ActorSystem.Create(Sys.Name, ConfigurationFactory.ParseString("akka.cluster.roles = [other]")
//             .WithFallback(Sys.Settings.Config));
//     }
//
//     [Fact(DisplayName = "Singletons should not move to higher AppVersion nodes until after older incarnation is downed")]
//     public async Task Bugfix7196Spec()
//     {
//         
//     }
// }

public class OldestChangedBufferStateSpecs
{
    [Fact]
    public void OldestChangedBuffer_should_initially_only_consider_nodes_with_matching_role()
    {
        // Arrange
        var targetRole = "target-role";
        var targetRoles = ImmutableHashSet.Create("role1", "role2", targetRole);
        var winningAddress = Address.Parse("akka://sys@darkstar:1112");
        var nonTargetRoles = ImmutableHashSet.Create("role1", "role2");
        var initialMembersByAge = ImmutableSortedSet<Member>.Empty
            .Add(Create(winningAddress, roles:targetRoles, upNumber: 3))
            .Add(Create(Address.Parse("akka://sys@darkstar:1113"), roles:nonTargetRoles, upNumber: 1))
            .Add(Create(Address.Parse("akka://sys@darkstar:1111"), roles:targetRoles, upNumber: 9))
            .WithComparer(MemberAgeOrdering.DescendingWithAppVersion);
        
        // Act
         var state = new OldestChangedBufferState(initialMembersByAge, targetRole);
         
         // Assert
         var oldest = state.CurrentOldest;
         oldest.Should().NotBeNull();
         oldest!.Address.Should().Be(winningAddress);
    }

    [Fact]
    public void OldestChangedBuffer_should_not_change_leader_when_higher_AppVersion_added()
    {
        // Arrange
        var winningAddress = Address.Parse("akka://sys@darkstar:1112");
        var appVersion1 = AppVersion.Create("1.0.0");
        var appVersion2 = AppVersion.Create("1.0.2");
        var initialMembersByAge = ImmutableSortedSet<Member>.Empty
            .Add(Create(winningAddress, upNumber: 3, appVersion: appVersion1))
            .Add(Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 9, appVersion: appVersion1))
            .WithComparer(MemberAgeOrdering.DescendingWithAppVersion);
        
        // Act
        var state = new OldestChangedBufferState(initialMembersByAge, string.Empty);
        var oldest = state.CurrentOldest;
        
        // higher upNumber - should not affect leader
        var newMemberSameVersion  = Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 10, appVersion: appVersion1);
        
        // higher upNumber AND version - should not affect leader
        var newMemberNewVersion = Create(Address.Parse("akka://sys@darkstar:1114"), upNumber: 11, appVersion: appVersion2);
        
        // Act
        var (state1, oldestChanged1) = state.AddMember(newMemberSameVersion);
        var (state2, oldestChanged2) = state1.AddMember(newMemberNewVersion);
        
        // Assert
        oldest.Should().NotBeNull();
        oldest!.Address.Should().Be(winningAddress);
        
        state1.CurrentOldest.Should().Be(oldest);
        oldestChanged1.Should().BeFalse();
        
        state2.CurrentOldest.Should().Be(oldest);
        oldestChanged2.Should().BeFalse();

        // the members by age system is going to chose the appVersion over the upNumber
        state2.MembersByAge.FirstOrDefault().Should().NotBe(oldest);
        state2.MembersByAge.FirstOrDefault()!.AppVersion.Should().Be(appVersion2);
    }
    
    [Fact]
    public void OldestChangedBuffer_should_change_Oldest_when_previous_Oldest_removed()
    {
        // Arrange
        var winningAddress = Address.Parse("akka://sys@darkstar:1112");
        var appVersion1 = AppVersion.Create("1.0.0");
        var appVersion2 = AppVersion.Create("1.0.2");

        var originalOldest = Create(winningAddress, upNumber: 3, appVersion: appVersion1);
        
        var initialMembersByAge = ImmutableSortedSet<Member>.Empty
            .Add(originalOldest)
            .Add(Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 9, appVersion: appVersion1))
            .WithComparer(MemberAgeOrdering.DescendingWithAppVersion);
        
        // Act
        var state = new OldestChangedBufferState(initialMembersByAge, string.Empty);
        var oldest = state.CurrentOldest;
        
        // higher upNumber, same version - won't affect leader 
        var newMemberSameVersion  = Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 4, appVersion: appVersion1);
        
        // lower upNumber AND version - won't affect the leader until it gets removed
        var newMemberHigherVersion = Create(Address.Parse("akka://sys@darkstar:1114"), upNumber: 11, appVersion: appVersion2);
        
        // Act
        var (state1, oldestChanged1) = state.AddMember(newMemberSameVersion);
        var (state2, oldestChanged2) = state1.AddMember(newMemberHigherVersion);
        var (state3, oldestChanged3) = state2.RemoveMember(originalOldest);
        
        // Assert
        oldest.Should().NotBeNull();
        oldest!.Address.Should().Be(winningAddress);
        
        state1.CurrentOldest.Should().Be(originalOldest);
        oldestChanged1.Should().BeFalse();
        
        state2.CurrentOldest.Should().Be(originalOldest);
        oldestChanged2.Should().BeFalse();
        
        state3.CurrentOldest.Should().Be(newMemberHigherVersion);
        oldestChanged3.Should().BeTrue();
    }
    
    [Fact]
    public void OldestChangedBuffer_should_not_change_Oldest_when_nonOldest_node_removed()
    {
        // Arrange
        var winningAddress = Address.Parse("akka://sys@darkstar:1112");
        var appVersion1 = AppVersion.Create("1.0.0");
        var appVersion2 = AppVersion.Create("1.0.2");
        var initialMembersByAge = ImmutableSortedSet<Member>.Empty
            .Add(Create(winningAddress, upNumber: 3, appVersion: appVersion1))
            .Add(Create(Address.Parse("akka://sys@darkstar:1111"), upNumber: 9, appVersion: appVersion1))
            .WithComparer(MemberAgeOrdering.DescendingWithAppVersion);
        
        // Act
        var state = new OldestChangedBufferState(initialMembersByAge, string.Empty);
        var oldest = state.CurrentOldest;
        
        // higher upNumber - should not affect leader
        var newMemberSameVersion  = Create(Address.Parse("akka://sys@darkstar:1113"), upNumber: 10, appVersion: appVersion1);
        
        // higher upNumber AND version - should not affect leader
        var newMemberNewVersion = Create(Address.Parse("akka://sys@darkstar:1114"), upNumber: 11, appVersion: appVersion2);
        
        // Act
        var (state1, oldestChanged1) = state.AddMember(newMemberSameVersion);
        var (state2, oldestChanged2) = state1.AddMember(newMemberNewVersion);
        var (state3, oldestChanged3) = state2.RemoveMember(newMemberSameVersion);
        
        // Assert
        oldest.Should().NotBeNull();
        oldest!.Address.Should().Be(winningAddress);
        
        state1.CurrentOldest.Should().Be(oldest);
        oldestChanged1.Should().BeFalse();
        
        state2.CurrentOldest.Should().Be(oldest);
        oldestChanged2.Should().BeFalse();

        state3.CurrentOldest.Should().Be(oldest);
        oldestChanged3.Should().BeFalse();
    }
    
    public static Member Create(
        Address address,
        MemberStatus status = MemberStatus.Up,
        ImmutableHashSet<string>? roles = null,
        int uid = 0,
        int upNumber = 0,
        AppVersion? appVersion = null)
    {
        return Member.Create(new UniqueAddress(address, uid), upNumber, status, roles ?? ImmutableHashSet<string>.Empty, appVersion ?? AppVersion.Zero);
    }
}