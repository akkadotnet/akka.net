// -----------------------------------------------------------------------
//  <copyright file="OldestChangedBufferState.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using System.Linq;

namespace Akka.Cluster.Tools.Singleton;

/// <summary>
/// Immutable data object that represents the state of the oldest changed buffer.
/// </summary>
internal sealed record OldestChangedBufferState
{
    public OldestChangedBufferState(ImmutableSortedSet<Member> initialMembersByAge, string? role)
    {
        MembersByAge = initialMembersByAge;
        Role = role;
        CurrentOldest = MembersByAge.FirstOrDefault(this.MatchingRole);
    }

    public string? Role { get; init; }

    public Member? CurrentOldest { get; init; }

    public ImmutableSortedSet<Member> MembersByAge { get; init; }
}

internal static class OldestChangedBufferStateExtensions
{
    internal static bool MatchingRole(this OldestChangedBufferState state, Member member)
    {
        return string.IsNullOrEmpty(state.Role) || member.HasRole(state.Role);
    }

    public static (OldestChangedBufferState newState, bool oldestChanged) AddMember(this OldestChangedBufferState state, Member member)
    {
        if (MatchingRole(state, member))
        {
            // remove then add node to replace it, as it's possible that the upNumber is changed
            return ComputeNextOldest(state with { MembersByAge = state.MembersByAge.Remove(member).Add(member) });
        }

        return (state, false);
    }

    public static (OldestChangedBufferState newState, bool oldestChanged) RemoveMember(this OldestChangedBufferState state, Member member)
    {
        if (MatchingRole(state, member))
        {
            return ComputeNextOldest(state with { MembersByAge = state.MembersByAge.Remove(member) });
        }

        return (state, false);
    }

    public static (OldestChangedBufferState newState, bool oldestChanged) ComputeNextOldest(this OldestChangedBufferState state)
    {
        // if the current oldest has not been removed, then it remains the oldest
        if(state.CurrentOldest is not null && state.MembersByAge.Contains(state.CurrentOldest))
        {
            return (state, false);
        }
        
        // compute the next oldest
        var nextOldest = state.MembersByAge.FirstOrDefault();
        var oldestChanged = !Equals(nextOldest, state.CurrentOldest);
        return (state with { CurrentOldest = nextOldest }, oldestChanged);
    }
}