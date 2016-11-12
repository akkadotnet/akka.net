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
using System.Text;

namespace Akka.DistributedData
{
    internal interface IPruningPhase { }

    internal sealed class PruningInitialized : IPruningPhase, IEquatable<PruningInitialized>
    {
        public static readonly PruningInitialized Empty = new PruningInitialized(ImmutableHashSet<Address>.Empty);

        public IImmutableSet<Address> Seen { get; }

        public PruningInitialized(params Address[] seen)
        {
            Seen = seen.ToImmutableHashSet();
        }

        public PruningInitialized(IImmutableSet<Address> seen)
        {
            Seen = seen;
        }

        public bool Equals(PruningInitialized other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Seen.SetEquals(other.Seen);
        }

        public override bool Equals(object obj) =>
            obj is PruningInitialized && Equals((PruningInitialized)obj);

        public override int GetHashCode() => Seen.GetHashCode();

        public override string ToString()
        {
            var sb = new StringBuilder("PruningInitialized(");
            if (Seen != null)
            {
                foreach (var entry in Seen)
                {
                    sb.Append(entry).Append(", ");
                }
            }
            sb.Append(')');
            return sb.ToString();
        }
    }

    internal sealed class PruningPerformed : IPruningPhase
    {
        public static PruningPerformed Instance = new PruningPerformed();

        private PruningPerformed() { }

        public override bool Equals(object obj) => obj != null && obj is PruningPerformed;

        public override int GetHashCode() => -1798412870; // "PruningPerformed".GetHashCode()
    }

    internal sealed class PruningState
    {
        public UniqueAddress Owner { get; }

        public IPruningPhase Phase { get; }

        public PruningState(UniqueAddress owner, IPruningPhase phase)
        {
            Owner = owner;
            Phase = phase;
        }

        internal PruningState AddSeen(Address node)
        {
            if (Phase is PruningPerformed) return this;
            if (Phase is PruningInitialized)
            {
                var p = (PruningInitialized)Phase;
                if (p.Seen.Contains(node) || Owner.Address == node)
                {
                    return this;
                }
                else
                {
                    return new PruningState(Owner, new PruningInitialized(p.Seen.Add(node)));
                }
            }
            else
            {
                throw new Exception("Invalid pruning phase provided");
            }
        }

        internal PruningState Merge(PruningState that)
        {
            if (Phase is PruningPerformed) return this;
            if (that.Phase is PruningPerformed) return that;
            if (Phase is PruningInitialized && that.Phase is PruningInitialized)
            {
                var p1 = (PruningInitialized)Phase;
                var p2 = (PruningInitialized)that.Phase;
                if (this.Owner == that.Owner)
                    return new PruningState(Owner, new PruningInitialized(p1.Seen.Union(p2.Seen)));
                else if (Member.AddressOrdering.Compare(this.Owner.Address, that.Owner.Address) > 0)
                    return that;
                else
                    return this;
            }
            else
            {
                throw new Exception("Invalid pruning state provided");
            }
        }

        public override bool Equals(object obj) => obj is PruningState && Equals((PruningState) obj);

        private bool Equals(PruningState other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;

            return Equals(Owner, other.Owner) && Equals(Phase, other.Phase);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Owner != null ? Owner.GetHashCode() : 0) * 397) ^ (Phase != null ? Phase.GetHashCode() : 0);
            }
        }

        public override string ToString() => $"PrunningState(owner={Owner}, phase={Phase})";
    }
}
