# Akka Serializer ID Code Table

**Id**|**Fully Qualified Class Name**|**Direct Access Method**|**Injected By**
-----|-----|-----|-----
1|Akka.Serialization.NewtonSoftJsonSerializer, Akka|ConfigurationFactory.Default()|ActorSystem,Create()
2|Akka.Remote.Serialization.ProtobufSerializer, Akka.Remote|RemoteConfigFactory.Default()|
3|Akka.Remote.Serialization.DaemonMsgCreateSerializer, Akka.Remote|RemoteConfigFactory.Default()|
4|Akka.Serialization.ByteArraySerializer, Akka|ConfigurationFactory.Default()|ActorSystem,Create()
5|Akka.Cluster.Serialization.ClusterMessageSerializer, Akka.Cluster|ClusterConfigFactory.Default()|ClusterSharding.Get()
6|Akka.Remote.Serialization.MessageContainerSerializer, Akka.Remote|RemoteConfigFactory.Default()|
7|Akka.Persistence.Serialization.PersistenceMessageSerializer, Akka.Persistence|Persistence.DefaultConfig()|Persistence.Instance.Apply()
8|Akka.Persistence.Serialization.PersistenceSnapshotSerializer, Akka.Persistence|Persistence.DefaultConfig()|Persistence.Instance.Apply()
10|Akka.Cluster.Metrics.Serialization.ClusterMetricsMessageSerializer, Akka.Cluster.Metrics|ClusterMetrics.DefaultConfig()|ClusterMetrics.Get()
11|Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData|DistributedData.DefaultConfig()<br>ClusterSharding.DefaultConfig()|ClusterSharding.Get()<br>DistributedData.Get()
12|Akka.DistributedData.Serialization.ReplicatorMessageSerializer, Akka.DistributedData|DistributedData.DefaultConfig()<br>ClusterSharding.DefaultConfig()|ClusterSharding.Get()<br>DistributedData.Get()
13|Akka.Cluster.Sharding.Serialization.ClusterShardingMessageSerializer, Akka.Cluster.Sharding|ClusterSharding.DefaultConfig()|ClusterSharding.Get()
14|Akka.Cluster.Tools.Singleton.Serialization.ClusterSingletonMessageSerializer, Akka.Cluster.Tools|DistributedPubSub.DefaultConfig()<br>ClusterSingletonProxy.DefaultConfig()<br>ClusterSingletonManager.DefaultConfig()|DistributedPubSub.Get()<br>ClusterSharding.Get()
15|Akka.Cluster.Tools.Client.Serialization.ClusterClientMessageSerializer, Akka.Cluster.Tools|ClusterClientReceptionist.DefaultConfig()|ClusterClientReceptionist.Get()
16|Akka.Remote.Serialization.MiscMessageSerializer, Akka.Remote|RemoteConfigFactory.Default()|
17|Akka.Remote.Serialization.PrimitiveSerializers, Akka.Remote|RemoteConfigFactory.Default()|
22|Akka.Remote.Serialization.SystemMessageSerializer, Akka.Remote|RemoteConfigFactory.Default()|
30|Akka.Streams.Serialization.StreamRefSerializer, Akka.Streams|ActorMaterializer.DefaultConfig()|ActorSystem.Materializer()
48|Akka.Persistence.Redis.Serialization.PersistentSnapshotSerializer, Akka.Persistence.Redis|RedisPersistence.DefaultConfig()|RedisPersistence.Get()