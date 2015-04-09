//-----------------------------------------------------------------------
// <copyright file="ByteArraySerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Serialization
{
    /**
     * This is a special Serializer that Serializes and deserializes byte arrays only,
     * (just returns the byte array unchanged/uncopied)
     */

    /// <summary>
    /// Class ByteArraySerializer.
    /// </summary>
    public class ByteArraySerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ByteArraySerializer" /> class.
        /// </summary>
        /// <param name="system"> The system. </param>
        public ByteArraySerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        /// <summary>
        /// Gets the identifier.
        /// </summary>
        /// <value> The identifier. </value>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        public override int Identifier
        {
            get { return 4; }
        }

        /// <summary>
        /// Gets a value indicating whether [include manifest].
        /// </summary>
        /// <value> <c> true </c> if [include manifest]; otherwise, <c> false </c>. </value>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        public override bool IncludeManifest
        {
            get { return false; }
        }

        /// <summary>
        /// To the binary.
        /// </summary>
        /// <param name="obj"> The object. </param>
        /// <returns> System.Byte[][]. </returns>
        /// <exception cref="System.NotSupportedException"> </exception>
        /// Serializes the given object into an Array of Byte
        public override byte[] ToBinary(object obj)
        {
            if (obj == null)
                return null;
            if (obj is byte[])
                return (byte[]) obj;
            throw new NotSupportedException();
        }

        /// <summary>
        /// Froms the binary.
        /// </summary>
        /// <param name="bytes"> The bytes. </param>
        /// <param name="type"> The type. </param>
        /// <returns> System.Object. </returns>
        /// Produces an object from an array of bytes, with an optional type;
        public override object FromBinary(byte[] bytes, Type type)
        {
            return bytes;
        }
    }
}

