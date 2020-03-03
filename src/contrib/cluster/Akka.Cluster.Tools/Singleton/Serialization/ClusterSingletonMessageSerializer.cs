//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Tools.Singleton.Serialization
{
    /// <summary>
    /// TBD
    /// </summary>
    public class ClusterSingletonMessageSerializer : SerializerWithStringManifest
    {
        private const string HandOverToMeManifest = "A";
        private const string HandOverInProgressManifest = "B";
        private const string HandOverDoneManifest = "C";
        private const string TakeOverFromMeManifest = "D";

        private static readonly byte[] EmptyBytes = {};
        private readonly IDictionary<string, Func<byte[], IClusterSingletonMessage>> _fromBinaryMap;

        /// <summary>
        /// Initializes a new instance of the <see cref="ClusterSingletonMessageSerializer"/> class.
        /// </summary>
        /// <param name="system">The actor system to associate with this serializer.</param>
        public ClusterSingletonMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _fromBinaryMap = new Dictionary<string, Func<byte[], IClusterSingletonMessage>>
            {
                {HandOverToMeManifest, _ => HandOverToMe.Instance},
                {HandOverInProgressManifest, _ => HandOverInProgress.Instance},
                {HandOverDoneManifest, _ => HandOverDone.Instance},
                {TakeOverFromMeManifest, _ => TakeOverFromMe.Instance}
            };
        }

        /// <summary>
        /// Serializes the given object into a byte array
        /// </summary>
        /// <param name="obj">The object to serialize</param>
        /// <exception cref="System.ArgumentException">
        /// This exception is thrown when the specified <paramref name="obj"/> is of an unknown type.
        /// Acceptable values include: <see cref="HandOverToMe"/> | <see cref="HandOverInProgress"/> | <see cref="HandOverDone"/> | <see cref="TakeOverFromMe"/>
        /// </exception>
        /// <returns>A byte array containing the serialized object</returns>
        public override byte[] ToBinary(object obj)
        {
            if (obj is HandOverToMe) return EmptyBytes;
            if (obj is HandOverInProgress) return EmptyBytes;
            if (obj is HandOverDone) return EmptyBytes;
            if (obj is TakeOverFromMe) return EmptyBytes;

            throw new ArgumentException($"Cannot serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        /// <summary>
        /// Deserializes a byte array into an object using an optional <paramref name="manifest" /> (type hint).
        /// </summary>
        /// <param name="bytes">The array containing the serialized object</param>
        /// <param name="manifest">The type hint used to deserialize the object contained in the array.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="bytes"/>cannot be deserialized using the specified <paramref name="manifest"/>.
        /// </exception>
        /// <returns>The object contained in the array</returns>
        public override object FromBinary(byte[] bytes, string manifest)
        {
            if (_fromBinaryMap.TryGetValue(manifest, out var mapper))
                return mapper(bytes);

            throw new SerializationException($"Unimplemented deserialization of message with manifest [{manifest}] in [{GetType()}]");
        }

        /// <summary>
        /// Returns the manifest (type hint) that will be provided in the <see cref="FromBinary(System.Byte[],System.String)" /> method.
        /// <note>
        /// This method returns <see cref="String.Empty" /> if a manifest is not needed.
        /// </note>
        /// </summary>
        /// <param name="o">The object for which the manifest is needed.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown when the specified <paramref name="o"/> does not have an associated manifest.
        /// </exception>
        /// <returns>The manifest needed for the deserialization of the specified <paramref name="o" />.</returns>
        public override string Manifest(object o)
        {
            if (o is HandOverToMe) return HandOverToMeManifest;
            if (o is HandOverInProgress) return HandOverInProgressManifest;
            if (o is HandOverDone) return HandOverDoneManifest;
            if (o is TakeOverFromMe) return TakeOverFromMeManifest;

            throw new ArgumentException($"Cannot serialize object of type [{o.GetType()}] in [{GetType()}]");
        }
    }
}
