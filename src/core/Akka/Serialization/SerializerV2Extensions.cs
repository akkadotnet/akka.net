//-----------------------------------------------------------------------
// <copyright file="SerializerV2Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;

namespace Akka.Serialization
{
    /// <summary>
    /// Helpers for unwrapping <see cref="SerializerV1Adapter"/> instances when callers need access
    /// to the underlying legacy <see cref="Serializer"/> type — typically because they hold a
    /// strongly-typed reference (e.g. a custom <c>SerializerWithStringManifest</c> field) rather
    /// than the abstract <see cref="SerializerV2"/> reference returned by
    /// <see cref="Serialization.FindSerializerFor"/>.
    /// </summary>
    public static class SerializerV2Extensions
    {
        /// <summary>
        /// Returns the inner V1 serializer if this V2 instance is a <see cref="SerializerV1Adapter"/>
        /// wrapping a <typeparamref name="T"/>; throws <see cref="InvalidCastException"/> otherwise.
        /// </summary>
        public static T AsV1<T>(this SerializerV2 serializer) where T : Serializer
        {
            return TryAsV1<T>(serializer)
                ?? throw new InvalidCastException(
                    $"SerializerV2 instance [{serializer.GetType().FullName}] (id [{serializer.Identifier}]) is not a SerializerV1Adapter wrapping a [{typeof(T).Name}].");
        }

        /// <summary>
        /// Returns the inner V1 serializer if this V2 instance is a <see cref="SerializerV1Adapter"/>
        /// wrapping a <typeparamref name="T"/>; <c>null</c> otherwise.
        /// </summary>
        public static T? TryAsV1<T>(this SerializerV2 serializer) where T : Serializer
        {
            return (serializer as SerializerV1Adapter)?.Inner as T;
        }
    }
}
