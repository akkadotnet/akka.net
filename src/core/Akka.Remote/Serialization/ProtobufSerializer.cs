//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes Google protobuf messages only.
    /// </summary>
    public class ProtobufSerializer : Serializer
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProtobufSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer. </param>
        public ProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// Values from 0 to 16 is reserved for Akka internal usage
        /// </summary>
        public override int Identifier
        {
            get { return 2; }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return true; }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize </param>
        /// <returns>A byte array containing the serialized object</returns>
        /// <exception cref="NotImplementedException">This method is not currently implemented.</exception>
        public override byte[] ToBinary(object obj)
        {
            throw new NotImplementedException();
            //using (var stream = new MemoryStream())
            //{
            //    global::ProtoBuf.Serializer.Serialize(stream, obj);
            //    return stream.ToArray();
            //}
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type"/>.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        /// <exception cref="NotImplementedException">This method is not currently implemented.</exception>
        public override object FromBinary(byte[] bytes, Type type)
        {
            throw new NotImplementedException();
            //using (var stream = new MemoryStream(bytes))
            //{
            //    return global::ProtoBuf.Serializer.NonGeneric.Deserialize(type, stream);
            //}
        }
    }
}
