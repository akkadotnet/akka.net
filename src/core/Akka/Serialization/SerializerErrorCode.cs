// //-----------------------------------------------------------------------
// // <copyright file="ErrorCodes.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;

namespace Akka.Serialization
{
    internal class SerializerErrorCode
    {
        public static readonly Dictionary<int, SerializerErrorCode> ErrorCodes = new Dictionary<int, SerializerErrorCode>
        {
            [1] = new SerializerErrorCode(1, "Akka.Serialization.NewtonSoftJsonSerializer, Akka", "ConfigurationFactory.Default()", "ActorSystem.Create()"),
            [2] = new SerializerErrorCode(2, "Akka.Remote.Serialization.ProtobufSerializer, Akka.Remote", "RemoteConfigFactory.Default()", null),
            [3] = new SerializerErrorCode(3, "Akka.Remote.Serialization.DaemonMsgCreateSerializer, Akka.Remote", "RemoteConfigFactory.Default()", null),
            [4] = new SerializerErrorCode(4, "Akka.Serialization.ByteArraySerializer, Akka", "ConfigurationFactory.Default()", "ActorSystem.Create()"),
            [5] = new SerializerErrorCode(5, "Akka.Cluster.Serialization.ClusterMessageSerializer, Akka.Cluster", "ClusterConfigFactory.Default()", "ClusterSharding.Get()"),
            [6] = new SerializerErrorCode(6, "Akka.Remote.Serialization.MessageContainerSerializer, Akka.Remote", "RemoteConfigFactory.Default()", null),
            [7] = new SerializerErrorCode(7, "Akka.Persistence.Serialization.PersistenceMessageSerializer, Akka.Persistence", "Persistence.DefaultConfig()", "Persistence.Instance.Apply()"),
            [8] = new SerializerErrorCode(8, "Akka.Persistence.Serialization.PersistenceSnapshotSerializer, Akka.Persistence", "Persistence.DefaultConfig()", "Persistence.Instance.Apply()"),
            [10] = new SerializerErrorCode(10, "Akka.Cluster.Metrics.Serialization.ClusterMetricsMessageSerializer, Akka.Cluster.Metrics", "ClusterMetrics.DefaultConfig()", "ClusterMetrics.Get()"),
            [11] = new SerializerErrorCode(11, "Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData", "DistributedData.DefaultConfig() or ClusterSharding.DefaultConfig()", "ClusterSharding.Get(), DistributedData.Get()"),
            [12] = new SerializerErrorCode(12, "Akka.DistributedData.Serialization.ReplicatorMessageSerializer, Akka.DistributedData", "DistributedData.DefaultConfig() or ClusterSharding.DefaultConfig()", "ClusterSharding.Get(), DistributedData.Get()"),
            [13] = new SerializerErrorCode(13, "Akka.Cluster.Sharding.Serialization.ClusterShardingMessageSerializer, Akka.Cluster.Sharding", "ClusterSharding.DefaultConfig()", "ClusterSharding.Get()"),
            [14] = new SerializerErrorCode(14, "Akka.Cluster.Tools.Singleton.Serialization.ClusterSingletonMessageSerializer, Akka.Cluster.Tools", "DistributedPubSub.DefaultConfig(), ClusterSingletonProxy.DefaultConfig(), or ClusterSingletonManager.DefaultConfig()", "DistributedPubSub.Get(), ClusterSharding.Get()"),
            [15] = new SerializerErrorCode(15, "Akka.Cluster.Tools.Client.Serialization.ClusterClientMessageSerializer, Akka.Cluster.Tools", "ClusterClientReceptionist.DefaultConfig()", "ClusterClientReceptionist.Get()"),
            [16] = new SerializerErrorCode(16, "Akka.Remote.Serialization.MiscMessageSerializer, Akka.Remote", "RemoteConfigFactory.Default()", null),
            [17] = new SerializerErrorCode(17, "Akka.Remote.Serialization.PrimitiveSerializers, Akka.Remote", "RemoteConfigFactory.Default()", null),
            [22] = new SerializerErrorCode(22, "Akka.Remote.Serialization.SystemMessageSerializer, Akka.Remote", "RemoteConfigFactory.Default()", null),
            [30] = new SerializerErrorCode(30, "Akka.Streams.Serialization.StreamRefSerializer, Akka.Streams", "ActorMaterializer.DefaultConfig()", "ActorSystem.Materializer()"),
            [48] = new SerializerErrorCode(48, "Akka.Persistence.Redis.Serialization.PersistentSnapshotSerializer, Akka.Persistence.Redis", "RedisPersistence.DefaultConfig()", "RedisPersistence.Get()"),
        };

        public static string GetErrorForSerializerId(int id)
            => ErrorCodes.TryGetValue(id, out var err)
                ? err.ToString() : id > 100 
                    ? $"Serializer Id [{id}] is not one of the internal Akka.NET serializer. Please contact your plugin provider for more information."
                    : $"Could not find any internal Akka.NET serializer with Id [{id}]. Please create an issue in our GitHub at [https://github.com/akkadotnet/akka.net].";
        
        private SerializerErrorCode(int id, string fqcn, string directAccess, string injector)
        {
            Id = id;
            Fqcn = fqcn;
            DirectAccess = directAccess;
            Injector = injector;
        }

        public int Id { get; }
        public string Fqcn { get; }
        public string DirectAccess { get; }
        public string Injector { get; }

        public override string ToString()
        {
            var sb = new StringBuilder($"Serializer Id [{Id}] is used to instantiate [{Fqcn}]. ")
                .Append($"You can add it by adding a fallback to your ActorSystem configuration by using [{DirectAccess}].");
            if (Injector != null)
                sb.Append($" It is also automatically injected into your configuration when you call [{Injector}].");
            return sb.ToString();
        }
    }
}