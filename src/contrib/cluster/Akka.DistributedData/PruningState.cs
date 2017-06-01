//-----------------------------------------------------------------------
// <copyright file="PruningState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Cluster;
using System;
using System.Collections.Immutable;

namespace Akka.DistributedData
{
    internal sealed class PruningInitialized : IPruningState
    {
        public UniqueAddress Owner { get; }
        public IImmutableSet<Address> Seen { get; }

        public PruningInitialized(UniqueAddress owner, params Address[] seen)
            : this(owner, seen.ToImmutableHashSet()) { }

        public PruningInitialized(UniqueAddress owner, IImmutableSet<Address> seen)
        {
            Owner = owner;
            Seen = seen;
        }

        public IPruningState AddSeen(Address node) =>
            Seen.Contains(node) || Owner.Address == node
                ? this
                : new PruningInitialized(Owner, Seen.Add(node));
        /// <inheritdoc/>
        /// <inheritdoc/>
        /// <inheritdoc/>

        public IPruningState Merge(IPruningState other)
        {
            if (other is PruningPerformed) return other;

            var that = (PruningInitialized)other;
            if (this.Owner == that.Owner)
                return new PruningInitialized(this.Owner, this.Seen.Union(that.Seen));
            else if (Member.AddressOrdering.Compare(this.Owner.Address, that.Owner.Address) > 0)
                return other;
            else
                return this;
        }
    }

    internal sealed class PruningPerformed : IPruningState
    {
        public PruningPerformed(DateTime obsoleteTime)
        {
            ObsoleteTime = obsoleteTime;
        }

        public DateTime ObsoleteTime { get; }

        public bool IsObsolete(DateTime currentTime) => ObsoleteTime <= currentTime;
        public IPruningState AddSeen(Address node) => this;

        public IPruningState Merge(IPruningState other)
        {
            var that = other as PruningPerformed;
            if (that != null)
            {
                return this.ObsoleteTime >= that.ObsoleteTime ? this : that;
            }
            else return this;
        }
    }

    public interface IPruningState
    {
        IPruningState AddSeen(Address node);

        IPruningState Merge(IPruningState other);
    }
}