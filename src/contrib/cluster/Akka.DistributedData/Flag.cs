﻿// -----------------------------------------------------------------------
//  <copyright file="Flag.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

namespace Akka.DistributedData;

/// <summary>
///     Implements a boolean flag CRDT that is initialized to `false` and
///     can be switched to `true`. `true` wins over `false` in merge.
///     This class is immutable, i.e. "modifying" methods return a new instance.
/// </summary>
[Serializable]
public sealed class Flag :
    IReplicatedData<Flag>,
    IEquatable<Flag>,
    IComparable<Flag>,
    IComparable,
    IReplicatedDataSerialization
{
    /// <summary>
    ///     Flag with a false value set.
    /// </summary>
    public static readonly Flag False = new(false);

    /// <summary>
    ///     Flag with a true value set.
    /// </summary>
    public static readonly Flag True = new(true);

    /// <summary>
    ///     Creates a new <see cref="Flag" /> instance set to false by default.
    /// </summary>
    public Flag() : this(false)
    {
    }

    /// <summary>
    ///     Creates a new <see cref="Flag" /> instance with value set to specified parameter.
    /// </summary>
    /// <param name="enabled">TBD</param>
    public Flag(bool enabled)
    {
        Enabled = enabled;
    }

    /// <summary>
    ///     Checks if current flag value is set.
    /// </summary>
    public bool Enabled { get; }

    public int CompareTo(object obj)
    {
        return obj is Flag flag ? CompareTo(flag) : 1;
    }

    public int CompareTo(Flag other)
    {
        return other == null ? 1 : Enabled.CompareTo(other.Enabled);
    }

    /// <summary>
    ///     Checks if two flags are equal to each other.
    /// </summary>
    /// <param name="other">TBD</param>
    /// <returns>TBD</returns>
    public bool Equals(Flag other)
    {
        if (ReferenceEquals(other, null)) return false;
        if (ReferenceEquals(this, other)) return true;

        return Enabled == other.Enabled;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="other">TBD</param>
    /// <returns>TBD</returns>
    IReplicatedData IReplicatedData.Merge(IReplicatedData other)
    {
        return Merge((Flag)other);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="other">TBD</param>
    /// <returns>TBD</returns>
    public Flag Merge(Flag other)
    {
        return other.Enabled ? other : this;
    }


    public override bool Equals(object obj)
    {
        return obj is Flag flag && Equals(flag);
    }

    public override int GetHashCode()
    {
        return Enabled.GetHashCode();
    }

    public override string ToString()
    {
        return Enabled.ToString();
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <returns>TBD</returns>
    public Flag SwitchOn()
    {
        return Enabled ? this : new Flag(true);
    }

    /// <summary>
    ///     Performs an implicit conversion from <see cref="Flag" /> to <see cref="bool" />.
    /// </summary>
    /// <param name="flag">The flag to convert</param>
    /// <returns>The result of the conversion</returns>
    public static implicit operator bool(Flag flag)
    {
        return flag.Enabled;
    }
}

/// <summary>
///     A typed key for <see cref="Flag" /> CRDT. Can be used to perform read/upsert/delete
///     operations on correlated data type.
/// </summary>
[Serializable]
public sealed class FlagKey : Key<Flag>
{
    /// <summary>
    ///     Creates a new instance of <see cref="FlagKey" /> class.
    /// </summary>
    /// <param name="id">TBD</param>
    public FlagKey(string id) : base(id)
    {
    }
}