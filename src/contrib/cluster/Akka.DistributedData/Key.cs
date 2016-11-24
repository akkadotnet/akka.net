//-----------------------------------------------------------------------
// <copyright file="Key.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.DistributedData
{
    public interface IKey
    {
        string Id { get; }
    }

    public interface IKey<out T> : IKey { }

    interface IKeyWithGenericType : IKey
    {
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
    public abstract class Key<T> : IKey<T> where T : IReplicatedData
    {
        public string Id { get; }

        protected Key(string id)
        {
            Id = id;
        }

        public bool Equals(IKey key)
        {
            if (ReferenceEquals(key, null)) return false;
            if (ReferenceEquals(this, key)) return true;

            return Id == key.Id;
        }

        public sealed override bool Equals(object obj) => obj is IKey && Equals((IKey) obj);

        public override int GetHashCode() => Id.GetHashCode();

        public override string ToString() => Id;

        public static implicit operator string(Key<T> key) => key.Id;
    }
}
