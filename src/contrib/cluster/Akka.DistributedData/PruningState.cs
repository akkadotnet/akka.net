// -----------------------------------------------------------------------
//  <copyright file="PruningState.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;

namespace Akka.DistributedData;

internal sealed class PruningInitialized : IPruningState, IEquatable<PruningInitialized>
{
    public PruningInitialized(UniqueAddress owner, params Address[] seen)
        : this(owner, seen.ToImmutableHashSet())
    {
    }

    public PruningInitialized(UniqueAddress owner, IImmutableSet<Address> seen)
    {
        Owner = owner;
        Seen = seen;
    }

    public UniqueAddress Owner { get; }
    public IImmutableSet<Address> Seen { get; }

    public bool Equals(PruningInitialized other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return Equals(Owner, other.Owner) && Seen.SetEquals(other.Seen);
    }

    public IPruningState AddSeen(Address node)
    {
        return Seen.Contains(node) || Owner.Address == node
            ? this
            : new PruningInitialized(Owner, Seen.Add(node));
    }

    /// <inheritdoc />
    public IPruningState Merge(IPruningState other)
    {
        if (other is PruningPerformed) return other;

        var that = (PruningInitialized)other;
        if (Owner == that.Owner)
            return new PruningInitialized(Owner, Seen.Union(that.Seen));
        return Member.AddressOrdering.Compare(Owner.Address, that.Owner.Address) > 0 ? other : this;
    }

    public override bool Equals(object obj)
    {
        return obj is PruningInitialized initialized && Equals(initialized);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var seed = 17;
            if (Seen != null)
                foreach (var s in Seen)
                    seed *= s.GetHashCode();

            if (Owner != null) seed = seed *= Owner.GetHashCode() ^ 397;

            return seed;
        }
    }
}

internal sealed class PruningPerformed : IPruningState, IEquatable<PruningPerformed>
{
    public PruningPerformed(DateTime obsoleteTime)
    {
        ObsoleteTime = obsoleteTime;
    }

    public DateTime ObsoleteTime { get; }

    public bool Equals(PruningPerformed other)
    {
        if (ReferenceEquals(null, other)) return false;
        if (ReferenceEquals(this, other)) return true;
        return ObsoleteTime.Equals(other.ObsoleteTime);
    }

    public IPruningState AddSeen(Address node)
    {
        return this;
    }

    public IPruningState Merge(IPruningState other)
    {
        if (other is PruningPerformed that) return ObsoleteTime >= that.ObsoleteTime ? this : that;

        return this;
    }

    public bool IsObsolete(DateTime currentTime)
    {
        return ObsoleteTime <= currentTime;
    }

    public override bool Equals(object obj)
    {
        return obj is PruningPerformed performed && Equals(performed);
    }

    public override int GetHashCode()
    {
        return ObsoleteTime.GetHashCode();
    }
}

public interface IPruningState
{
    IPruningState AddSeen(Address node);

    IPruningState Merge(IPruningState other);
}