//-----------------------------------------------------------------------
// <copyright file="ByteArraySerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes byte arrays only
    /// (just returns the byte array unchanged/uncopied).
    /// </summary>
    public class ByteArraySerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ByteArraySerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public ByteArraySerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest => false;

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if the given <paramref name="obj"/> is not a byte array.
        /// </exception>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj == null)
                return null;
            if (obj is byte[])
                return (byte[]) obj;
            throw new NotSupportedException("The object to convert is not a byte array.");
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            return bytes;
        }
    }
}
