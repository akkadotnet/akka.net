//-----------------------------------------------------------------------
// <copyright file="LWWRegister.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster;
using Akka.Util;

namespace Akka.DistributedData
{
    /// <summary>
    /// Delegate responsible for managing <see cref="LWWRegister{T}"/> clock.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="currentTimestamp">The current timestamp value of the <see cref="LWWRegister{T}"/>.</param>
    /// <param name="value">The register value to set and associate with the returned timestamp.</param>
    /// <returns>Next timestamp</returns>
    public delegate long Clock<in T>(long currentTimestamp, T value);

    [Serializable]
    public sealed class LWWRegisterKey<T> : Key<LWWRegister<T>>
    {
        public LWWRegisterKey(string id) : base(id)
        {
        }
    }
    
    /// <summary>
    /// Implements a 'Last Writer Wins Register' CRDT, also called a 'LWW-Register'.
    /// 
    /// It is described in the paper
    /// <a href="http://hal.upmc.fr/file/index/docid/555588/filename/techreport.pdf">A comprehensive study of Convergent and Commutative Replicated Data Types</a>.
    /// 
    /// Merge takes the register with highest timestamp. Note that this
    /// relies on synchronized clocks. <see cref="LWWRegister{T}"/> should only be used when the choice of
    /// value is not important for concurrent updates occurring within the clock skew.
    /// 
    /// Merge takes the register updated by the node with lowest address (<see cref="UniqueAddress"/> is ordered)
    /// if the timestamps are exactly the same.
    /// 
    /// Instead of using timestamps based on `DateTime.UtcNow` time it is possible to
    /// use a timestamp value based on something else, for example an increasing version number
    /// from a database record that is used for optimistic concurrency control.
    /// 
    /// For first-write-wins semantics you can use the <see cref="LWWRegister.ReverseClock"/> instead of the
    /// [[LWWRegister#defaultClock]]
    /// 
    /// This class is immutable, i.e. "modifying" methods return a new instance.
    /// </summary>
    [Serializable]
    public class LWWRegister<T> : IReplicatedData<LWWRegister<T>>, IReplicatedDataSerialization, IEquatable<LWWRegister<T>>
    {
        /// <summary>
        /// Default clock is using max between DateTime.UtcNow.Ticks and current timestamp + 1.
        /// </summary>
        public static readonly Clock<T> DefaultClock =
            (timestamp, value) => Math.Max(DateTime.UtcNow.Ticks, timestamp + 1);

        /// <summary>
        /// Reverse clock can be used for first-write-wins semantics. It's counting backwards, 
        /// using min between -DateTime.UtcNow.Ticks and current timestamp - 1.
        /// </summary>
        public static readonly Clock<T> ReverseClock =
            (timestamp, value) => Math.Min(-DateTime.UtcNow.Ticks, timestamp - 1);

        public LWWRegister(UniqueAddress node, T initial)
        {
            UpdatedBy = node;
            Value = initial;
            Timestamp = DefaultClock(0L, initial);
        }

        public LWWRegister(UniqueAddress node, T value, long timestamp)
        {
            UpdatedBy = node;
            Value = value;
            Timestamp = timestamp;
        }

        public LWWRegister(UniqueAddress node, T initial, Clock<T> clock)
        {
            UpdatedBy = node;
            Value = initial;
            Timestamp = clock(0L, initial);
        }

        /// <summary>
        /// Returns a timestamp used to determine predecende in current register updates.
        /// </summary>
        public long Timestamp { get; }

        /// <summary>
        /// Returns value of the current register.
        /// </summary>
        public T Value { get; }

        /// <summary>
        /// Returns a unique address of the last cluster node, that updated current register value.
        /// </summary>
        public UniqueAddress UpdatedBy { get; }

        /// <summary>
        /// Change the value of the register.
        /// 
        /// You can provide your <paramref name="clock"/> implementation instead of using timestamps based
        /// on DateTime.UtcNow.Ticks time. The timestamp can for example be an
        /// increasing version number from a database record that is used for optimistic
        /// concurrency control.
        /// </summary>
        public LWWRegister<T> WithValue(UniqueAddress node, T value, Clock<T> clock = null)
        {
            var c = clock ?? DefaultClock;
            return new LWWRegister<T>(node, value, c(Timestamp, value));
        }

        public LWWRegister<T> Merge(LWWRegister<T> other)
        {
            if (other.Timestamp > Timestamp) return other;
            if (other.Timestamp < Timestamp) return this;
            if (other.UpdatedBy.Uid < UpdatedBy.Uid) return other;
            return this;
        }

        public IReplicatedData Merge(IReplicatedData other) => Merge((LWWRegister<T>)other);
        public bool Equals(LWWRegister<T> other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(this, other)) return true;

            return Timestamp == other.Timestamp && UpdatedBy == other.UpdatedBy && Equals(Value, other.Value);
        }

        public override bool Equals(object obj) => obj is LWWRegister<T> && Equals((LWWRegister<T>)obj);

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = UpdatedBy.GetHashCode();
                hashCode = (hashCode * 397) ^ EqualityComparer<T>.Default.GetHashCode(Value);
                hashCode = (hashCode * 397) ^ Timestamp.GetHashCode();
                return hashCode;
            }
        }

        public override string ToString() => $"LWWRegister(value={Value}, timestamp={Timestamp}, updatedBy={UpdatedBy})";
    }
}