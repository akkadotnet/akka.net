//-----------------------------------------------------------------------
// <copyright file="Key.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.DistributedData
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IKey
    {
        /// <summary>
        /// TBD
        /// </summary>
        string Id { get; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public interface IKey<out T> : IKey where T : IReplicatedData { }

    /// <summary>
    /// TBD
    /// </summary>
    interface IKeyWithGenericType : IKey
    {
        /// <summary>
        /// TBD
        /// </summary>
        Type Type { get; }
    }

    /// <summary>
    /// Key for the key-value data in <see cref="Replicator"/>. The type of the data value
    /// is defined in the key. KeySet are compared equal if the `id` strings are equal,
    /// i.e. use unique identifiers.
    /// 
    /// Specific classes are provided for the built in data types, e.g. <see cref="ORSetKey{T}"/>,
    /// and you can create your own keys.
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public abstract class Key<T> : IKey<T> where T : IReplicatedData
    {
        /// <summary>
        /// TBD
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="id">TBD</param>
        protected Key(string id)
        {
            Id = id;
        }

        /// <inheritdoc/>
        public bool Equals(IKey key)
        {
            if (ReferenceEquals(key, null)) return false;
            if (ReferenceEquals(this, key)) return true;

            return Id == key.Id;
        }

        /// <inheritdoc/>
        public sealed override bool Equals(object obj) => obj is IKey && Equals((IKey) obj);

        /// <inheritdoc/>
        public override int GetHashCode() => Id.GetHashCode();

        /// <inheritdoc/>
        public override string ToString() => Id;

        /// <summary>
        /// Performs an implicit conversion from <see cref="Akka.DistributedData.Key{T}" /> to <see cref="System.String" />.
        /// </summary>
        /// <param name="key">The key to convert</param>
        /// <returns>The result of the conversion</returns>
        public static implicit operator string(Key<T> key) => key.Id;
    }
}