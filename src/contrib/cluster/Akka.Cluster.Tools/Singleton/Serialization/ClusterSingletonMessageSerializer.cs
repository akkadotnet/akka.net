//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Tools.Singleton.Serialization
{
    internal class ClusterSingletonMessageSerializer : SerializerWithStringManifest
    {
        private const string HandOverToMeManifest = "A";
        private const string HandOverInProgressManifest = "B";
        private const string HandOverDoneManifest = "C";
        private const string TakeOverFromMeManifest = "D";

        private static readonly byte[] EmptyBytes = new byte[0];
        private readonly IDictionary<string, Func<byte[], IClusterSingletonMessage>> _fromBinaryMap;

        public override int Identifier { get; }

        public ClusterSingletonMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            Identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(this.GetType(), system);
            _fromBinaryMap = new Dictionary<string, Func<byte[], IClusterSingletonMessage>>
            {
                {HandOverToMeManifest, _ => HandOverToMe.Instance},
                {HandOverInProgressManifest, _ => HandOverInProgress.Instance},
                {HandOverDoneManifest, _ => HandOverDone.Instance},
                {TakeOverFromMeManifest, _ => TakeOverFromMe.Instance}
            };
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is HandOverToMe) return EmptyBytes;
            if (obj is HandOverInProgress) return EmptyBytes;
            if (obj is HandOverDone) return EmptyBytes;
            if (obj is TakeOverFromMe) return EmptyBytes;

            throw new ArgumentException(string.Format("Cannot serialize object of type [{0}] in [{1}]", obj.GetType(), GetType()));
        }

        public override object FromBinary(byte[] binary, string manifest)
        {
            Func<byte[], IClusterSingletonMessage> mapper;
            if (_fromBinaryMap.TryGetValue(manifest, out mapper))
            {
                return mapper(binary);
            }

            throw new ArgumentException(string.Format("Unimplemented deserialization of message with manifest [{0}] in [{1}]", manifest, GetType()));
        }

        public override string Manifest(object o)
        {
            if (o is HandOverToMe) return HandOverToMeManifest;
            if (o is HandOverInProgress) return HandOverInProgressManifest;
            if (o is HandOverDone) return HandOverDoneManifest;
            if (o is TakeOverFromMe) return TakeOverFromMeManifest;

            throw new ArgumentException(string.Format("Cannot serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
        }
    }
}