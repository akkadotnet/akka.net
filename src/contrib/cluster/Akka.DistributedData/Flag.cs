//-----------------------------------------------------------------------
// <copyright file="Flag.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.DistributedData
{
    /// <summary>
    /// Implements a boolean flag CRDT that is initialized to `false` and
    /// can be switched to `true`. `true` wins over `false` in merge.
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public sealed class Flag : IReplicatedData<Flag>, IEquatable<Flag>, IComparable<Flag>, IComparable, IReplicatedDataSerialization
    {
        public static readonly Flag False = new Flag(false);
        public static readonly Flag True = new Flag(true);

        public bool Enabled { get; }

        public Flag(): this(false) { }

        public Flag(bool enabled)
        {
            Enabled = enabled;
        }
        
        public bool Equals(Flag other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Enabled == other.Enabled;
        }

        public override bool Equals(object obj) => obj is Flag && Equals((Flag) obj);
        public override int GetHashCode() => Enabled.GetHashCode();
        public int CompareTo(object obj) => obj is Flag ? CompareTo((Flag) obj) : 1;
        public int CompareTo(Flag other) => other == null ? 1 : Enabled.CompareTo(other.Enabled);
        public override string ToString() => Enabled.ToString();
        public IReplicatedData Merge(IReplicatedData other) => Merge((Flag) other);
        public Flag Merge(Flag other) => other.Enabled ? other : this;
        public Flag SwitchOn() => Enabled ? this : new Flag(true);

        public static implicit operator bool(Flag flag) => flag.Enabled;
    }

    [Serializable]
    public sealed class FlagKey : Key<Flag>
    {
        public FlagKey(string id) : base(id) { }
    }
}
