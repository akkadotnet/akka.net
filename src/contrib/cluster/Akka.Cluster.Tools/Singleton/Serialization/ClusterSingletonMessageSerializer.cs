//-----------------------------------------------------------------------
// <copyright file="ClusterSingletonMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Cluster.Tools.Singleton.Serialization
{
    public class ClusterSingletonMessageSerializer : SerializerWithStringManifest
    {
        private const string HandOverToMeManifest = "A";
        private const string HandOverInProgressManifest = "B";
        private const string HandOverDoneManifest = "C";
        private const string TakeOverFromMeManifest = "D";

        private static readonly byte[] EmptyBytes = new byte[0];
        private static readonly IDictionary<string, ClusterSingletonMessage> FromBinaryMap = new Dictionary<string, ClusterSingletonMessage>
        {
            {HandOverToMeManifest, ClusterSingletonMessage.HandOverToMe},
            {HandOverInProgressManifest, ClusterSingletonMessage.HandOverInProgress},
            {HandOverDoneManifest, ClusterSingletonMessage.HandOverDone},
            {TakeOverFromMeManifest, ClusterSingletonMessage.TakeOverFromMe}
        };

        private readonly int _identifier;

        public ClusterSingletonMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            _identifier = SerializerIdentifierHelper.GetSerializerIdentifierFromConfig(this.GetType(), system);
        }

        public override int Identifier { get { return _identifier; } }
        public override byte[] ToBinary(object o)
        {
            if (o is ClusterSingletonMessage)
            {
                switch ((ClusterSingletonMessage)o)
                {
                    case ClusterSingletonMessage.HandOverToMe:
                    case ClusterSingletonMessage.HandOverInProgress:
                    case ClusterSingletonMessage.HandOverDone:
                    case ClusterSingletonMessage.TakeOverFromMe:
                        return EmptyBytes;
                    default: throw new ArgumentException(string.Format("Cannot serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
                }
            }
            else throw new ArgumentException(string.Format("Cannot serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
        }

        public override object FromBinary(byte[] binary, string manifest)
        {
            ClusterSingletonMessage mapping;
            if (FromBinaryMap.TryGetValue(manifest, out mapping))
            {
                return mapping;
            }
            else throw new ArgumentException(string.Format("Unimplemented deserialization of message with manifest [{0}] in [{1}]", manifest, GetType()));
        }

        public override string Manifest(object o)
        {
            if (o is ClusterSingletonMessage)
            {
                switch ((ClusterSingletonMessage)o)
                {
                    case ClusterSingletonMessage.HandOverToMe: return HandOverToMeManifest;
                    case ClusterSingletonMessage.HandOverInProgress: return HandOverInProgressManifest;
                    case ClusterSingletonMessage.HandOverDone: return HandOverDoneManifest;
                    case ClusterSingletonMessage.TakeOverFromMe: return TakeOverFromMeManifest;
                    default: throw new ArgumentException(string.Format("Cannot serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
                }
            }
            else throw new ArgumentException(string.Format("Cannot serialize object of type [{0}] in [{1}]", o.GetType(), GetType()));
        }
    }
}