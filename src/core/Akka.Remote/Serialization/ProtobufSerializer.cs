//-----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        /// N/A
        /// </summary>
        /// <param name="obj">N/A</param>
        /// <exception cref="NotImplementedException">
        /// This exception is automatically thrown since <see cref="ProtobufSerializer"/> does not implement this function.
        /// </exception>
        /// <returns>N/A</returns>
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
        /// N/A
        /// </summary>
        /// <param name="bytes">N/A</param>
        /// <param name="type">N/A</param>
        /// <exception cref="NotImplementedException">
        /// This exception is automatically thrown since <see cref="ProtobufSerializer"/> does not implement this function.
        /// </exception>
        /// <returns>N/A</returns>
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
