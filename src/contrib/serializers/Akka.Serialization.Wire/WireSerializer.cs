//-----------------------------------------------------------------------
// <copyright file="WireSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.IO;
using Akka.Actor;
using Akka.Util;
using Wire;

// ReSharper disable once CheckNamespace
namespace Akka.Serialization
{
    /// <summary>
    /// This is a special <see cref="Serializer"/> that serializes and deserializes plain old CLR objects (POCOs).
    /// </summary>
    public class WireSerializer : Serializer
    {
        private readonly Wire.Serializer _serializer;

        /// <summary>
        /// Initializes a new instance of the <see cref="WireSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public WireSerializer(ExtendedActorSystem system) : base(system)
        {
            var akkaSurrogate = 
                Surrogate
                .Create<ISurrogated, ISurrogate>(
                from => from.ToSurrogate(system),
                to => to.FromSurrogate(system));

            _serializer =
                new Wire.Serializer(new SerializerOptions(
                    preserveObjectReferences: true, 
                    versionTolerance: true,
                    surrogates: new[]
                    {
                        akkaSurrogate
                    }));
        }

        /// <summary>
        /// Completely unique value to identify this implementation of Serializer, used to optimize network traffic
        /// </summary>
        public override int Identifier
        {
            get { return -4; }
        }

        /// <summary>
        /// Returns whether this serializer needs a manifest in the fromBinary method
        /// </summary>
        public override bool IncludeManifest
        {
            get { return false; }
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <returns>A byte array containing the serialized object </returns>
        public override byte[] ToBinary(object obj)
        {
            using (var ms = new MemoryStream())
            {
                _serializer.Serialize(obj, ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Deserializes a byte array into an object of type <paramref name="type" />.
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="type">The type of object contained in the array</param>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, Type type)
        {
            using (var ms = new MemoryStream(bytes))
            { 
                var res = _serializer.Deserialize<object>(ms);
                return res;
            }
        }
    }
}
