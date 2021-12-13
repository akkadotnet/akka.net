---
uid: serializer-codes
title: Serializer ID Code Table
---
<!-- markdownlint-disable MD033 -->
# Akka Serializer ID Code Table

Serializers with ID range 1-100 are reserved for internal Akka.NET serializers. This table maps which
ID belongs to what package and how you can add them into your ActorSystem HOCON configuration or which
Akka.NET plugin initializer that automatically injects the serializer into your settings.

For example, if you are missing a serializer with id 13, you can add them by either adding a fallback
to your configuration by calling `myConfig.WithFallback(ClusterSharding.DefaultConfig())` or by
calling `ClusterSharding.Get(myActorSystem)`.

Serializers for `Akka.Remote` will be added automatically if you use `akka.actor.provider = remote` or
`akka.actor.provider = "Akka.Remote.RemoteActorRefProvider, Akka.Remote"`

Serializers for `Akka.Cluster` will be added automatically if you use `akka.actor.provider = cluster` or
`akka.actor.provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"`

**Id**|**Serializer Class Name**|**Package**|**Direct Access Method**|**Injected By**
-----|-----|-----|-----|-----
1|NewtonSoftJsonSerializer|Akka|ConfigurationFactory.Default()|ActorSystem.Create()
2|ProtobufSerializer|Akka.Remote|RemoteConfigFactory.Default()|
3|DaemonMsgCreateSerializer|Akka.Remote|RemoteConfigFactory.Default()|
4|ByteArraySerializer|Akka|ConfigurationFactory.Default()|ActorSystem,Create()
5|ClusterMessageSerializer|Akka.Cluster|ClusterConfigFactory.Default()|ClusterSharding.Get()
6|MessageContainerSerializer|Akka.Remote|RemoteConfigFactory.Default()|
7|PersistenceMessageSerializer|Akka.Persistence|Persistence.DefaultConfig()|Persistence.Instance.Apply()
8|PersistenceSnapshotSerializer|Akka.Persistence|Persistence.DefaultConfig()|Persistence.Instance.Apply()
10|ClusterMetricsMessageSerializer|Akka.Cluster.Metrics|ClusterMetrics.DefaultConfig()|ClusterMetrics.Get()
11|ReplicatedDataSerializer|Akka.DistributedData|DistributedData.DefaultConfig()<br>ClusterSharding.DefaultConfig()|ClusterSharding.Get()<br>DistributedData.Get()
12|ReplicatorMessageSerializer|Akka.DistributedData|DistributedData.DefaultConfig()<br>ClusterSharding.DefaultConfig()|ClusterSharding.Get()<br>DistributedData.Get()
13|ClusterShardingMessageSerializer|Akka.Cluster.Sharding|ClusterSharding.DefaultConfig()|ClusterSharding.Get()
14|ClusterSingletonMessageSerializer|Akka.Cluster.Tools|DistributedPubSub.DefaultConfig()<br>ClusterSingletonProxy.DefaultConfig()<br>ClusterSingletonManager.DefaultConfig()|DistributedPubSub.Get()<br>ClusterSharding.Get()
15|ClusterClientMessageSerializer|Akka.Cluster.Tools|ClusterClientReceptionist.DefaultConfig()|ClusterClientReceptionist.Get()
16|MiscMessageSerializer|Akka.Remote|RemoteConfigFactory.Default()|
17|PrimitiveSerializers|Akka.Remote|RemoteConfigFactory.Default()|
22|SystemMessageSerializer|Akka.Remote|RemoteConfigFactory.Default()|
30|StreamRefSerializer|Akka.Streams|ActorMaterializer.DefaultConfig()|ActorSystem.Materializer()
48|PersistentSnapshotSerializer|Akka.Persistence.Redis|RedisPersistence.DefaultConfig()|RedisPersistence.Get()
