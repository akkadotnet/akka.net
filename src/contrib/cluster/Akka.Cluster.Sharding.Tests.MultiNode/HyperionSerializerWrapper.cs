// //-----------------------------------------------------------------------
// // <copyright file="HyperionSerializerWrapper.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Serialization;

namespace Akka.Cluster.Sharding.Tests.MultiNode
{
    public class HyperionSerializerWrapper : Serializer
    {
        private readonly HyperionSerializer _serializer;
        
        public HyperionSerializerWrapper(ExtendedActorSystem system) : base(system)
        {
            _serializer = new HyperionSerializer(system);
        }

        public HyperionSerializerWrapper(ExtendedActorSystem system, Config config) : base(system)
        {
            _serializer = new HyperionSerializer(system, config);
        }
        
        public HyperionSerializerWrapper(ExtendedActorSystem system, HyperionSerializerSettings settings) : base(system)
        {
            _serializer = new HyperionSerializer(system, settings);
        }

        public override int Identifier => -6;
        
        public override bool IncludeManifest => false;
        
        public override byte[] ToBinary(object obj)
        {
            if (obj is IClusterShardingSerializable)
                throw new Exception($"THIS ISN'T SUPPOSED TO BE SERIALIZED. Type: {obj.GetType().FullName}");
            
            // IShardRegionCommand isn't serialized using cluster sharding serializer
            if (!(obj is IShardRegionCommand))
            {
                var typeName = obj.GetType().AssemblyQualifiedName;
                if(typeName.StartsWith("Akka.Cluster.Sharding") && !typeName.Contains("Tests"))
                    throw new Exception($"THIS ISN'T SUPPOSED TO BE SERIALIZED. Type: {typeName}");
            }

            return _serializer.ToBinary(obj);
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            var obj = _serializer.FromBinary(bytes, type);
            
            if (obj is IClusterShardingSerializable)
                throw new Exception($"THIS ISN'T SUPPOSED TO BE SERIALIZED. Type: {obj.GetType().FullName}");
            
            // IShardRegionCommand isn't serialized using cluster sharding serializer
            if (!(obj is IShardRegionCommand))
            {
                var typeName = obj.GetType().AssemblyQualifiedName;
                if(typeName.StartsWith("Akka.Cluster.Sharding") && !typeName.Contains("Tests"))
                    throw new Exception($"THIS ISN'T SUPPOSED TO BE SERIALIZED. Type: {typeName}");
            }
            return obj;
        }
    }
}