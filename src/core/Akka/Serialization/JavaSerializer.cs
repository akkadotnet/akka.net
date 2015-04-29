//-----------------------------------------------------------------------
// <copyright file="Serializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes Java objects only.
    /// </summary>
    public class JavaSerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JavaSerializer" /> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public JavaSerializer(ExtendedActorSystem system)
            : base(system)
        {
        }

        /// <summary>
        /// Completely unique value to identify this implementation of the <see cref="Serializer"/> used to optimize network traffic
        /// </summary>
        public override int Identifier
        {
            get { return 1; }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { throw new NotSupportedException(); }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotSupportedException();
        }
    }
}
