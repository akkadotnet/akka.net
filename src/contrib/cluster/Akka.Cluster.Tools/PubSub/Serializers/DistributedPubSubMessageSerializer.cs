//-----------------------------------------------------------------------
// <copyright file="DistributedPubSubMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Tools.PubSub.Serializers
{
    /**
     * Protobuf serializer of DistributedPubSubMediator messages.
     */
    public class DistributedPubSubMessageSerializer : SerializerWithStringManifest
    {
        public const int BufferSize = 1024*4;

        public const string StatusManifest = "A";
        public const string DeltaManifest = "B";
        public const string SendManifest = "C";
        public const string SendToAllManifest = "D";
        public const string PublishManifest = "E";

        public static readonly IDictionary<string, Func<byte[], object>> _fromBinaryMap = new Dictionary<string, Func<byte[], object>>
        {
            
        };

        private readonly int _identifier;

        public DistributedPubSubMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(this.GetType(), system);
        }

        public override int Identifier { get { return _identifier; } }

        public override byte[] ToBinary(object obj)
        {
            throw new NotImplementedException();
        }

        protected override object FromBinary(byte[] bytes, string manifestString)
        {
            Func<byte[], object> deserializer;
            if (_fromBinaryMap.TryGetValue(manifestString, out deserializer))
            {
                return deserializer(bytes);
            }
            else
                throw new ArgumentException(string.Format("Unimplemented deserialization of message with manifest [{0}] in serializer {1}", manifestString, GetType()));
        }

        public override string Manifest(object o)
        {
            if (o is Internal.Status) return StatusManifest;
            if (o is Internal.Delta) return DeltaManifest;
            if (o is DistributedPubSubMediator.Send) return SendManifest;
            if (o is DistributedPubSubMediator.SendToAll) return SendToAllManifest;
            if (o is DistributedPubSubMediator.Publish) return PublishManifest;

            throw new ArgumentException(string.Format("Serializer {0} cannot serialize message of type {1}", this.GetType(), o.GetType()));
        }
    }
}