//-----------------------------------------------------------------------
// <copyright file="FrozenSerializerRows.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using Akka.Configuration;

namespace Akka.Serialization
{
    /// <summary>
    /// A frozen copy of the built-in serializer rows each first-party module's shipped HOCON carried at
    /// <c>1.6.0-beta1</c> (the same rows 1.5 shipped). The modules no longer ship these rows - their serializers and
    /// bindings register from code - so these copies are what a module's serializer table has to keep matching:
    /// the alias to type, the binding to alias and the serializer ids are the wire contract with older nodes.
    /// Never edit a row here to follow a code change; a mismatch means the table broke compatibility.
    /// </summary>
    public static class FrozenSerializerRows
    {
        /// <summary>The rows <c>src/core/Akka.Remote/Configuration/Remote.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config Remote { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-containers = "Akka.Remote.Serialization.MessageContainerSerializer, Akka.Remote"
                    akka-misc = "Akka.Remote.Serialization.MiscMessageSerializer, Akka.Remote"
                    primitive = "Akka.Remote.Serialization.PrimitiveSerializers, Akka.Remote"
                    proto = "Akka.Remote.Serialization.ProtobufSerializer, Akka.Remote"
                    daemon-create = "Akka.Remote.Serialization.DaemonMsgCreateSerializer, Akka.Remote"
                    akka-system-msg = "Akka.Remote.Serialization.SystemMessageSerializer, Akka.Remote"
                    artery-control = "Akka.Remote.Artery.ArteryControlMessageSerializer, Akka.Remote"
                }
                serialization-bindings {
                    "Akka.Actor.ActorSelectionMessage, Akka" = akka-containers
                    "Akka.Remote.DaemonMsgCreate, Akka.Remote" = daemon-create
                    "Google.Protobuf.IMessage, Google.Protobuf" = proto
                    "Akka.Actor.Identify, Akka" = akka-misc
                    "Akka.Actor.ActorIdentity, Akka" = akka-misc
                    "Akka.Actor.IActorRef, Akka" = akka-misc
                    "Akka.Actor.PoisonPill, Akka" = akka-misc
                    "Akka.Actor.IntentionalRestart, Akka" = akka-misc
                    "Akka.Actor.Kill, Akka" = akka-misc
                    "Akka.Actor.Status+Failure, Akka" = akka-misc
                    "Akka.Actor.Status+Success, Akka" = akka-misc
                    "Akka.Actor.RemoteScope, Akka" = akka-misc
                    "Akka.Routing.FromConfig, Akka" = akka-misc
                    "Akka.Routing.DefaultResizer, Akka" = akka-misc
                    "Akka.Routing.RoundRobinPool, Akka" = akka-misc
                    "Akka.Routing.BroadcastPool, Akka" = akka-misc
                    "Akka.Routing.RandomPool, Akka" = akka-misc
                    "Akka.Routing.ScatterGatherFirstCompletedPool, Akka" = akka-misc
                    "Akka.Routing.TailChoppingPool, Akka" = akka-misc
                    "Akka.Routing.ConsistentHashingPool, Akka" = akka-misc
                    "Akka.Configuration.Config, Akka" = akka-misc
                    "Akka.Remote.RemoteWatcher+Heartbeat, Akka.Remote" = akka-misc
                    "Akka.Remote.RemoteWatcher+HeartbeatRsp, Akka.Remote" = akka-misc
                    "Akka.Remote.Artery.IArteryControlMessage, Akka.Remote" = artery-control
                    "Akka.Remote.Routing.RemoteRouterConfig, Akka.Remote" = akka-misc
                    "Akka.Dispatch.SysMsg.SystemMessage, Akka" = akka-system-msg
                    "System.String" = primitive
                    "System.Int32" = primitive
                    "System.Int64" = primitive
                }
                serialization-identifiers {
                    "Akka.Remote.Serialization.ProtobufSerializer, Akka.Remote" = 2
                    "Akka.Remote.Serialization.DaemonMsgCreateSerializer, Akka.Remote" = 3
                    "Akka.Remote.Serialization.MessageContainerSerializer, Akka.Remote" = 6
                    "Akka.Remote.Serialization.MiscMessageSerializer, Akka.Remote" = 16
                    "Akka.Remote.Serialization.PrimitiveSerializers, Akka.Remote" = 17
                    "Akka.Remote.Serialization.SystemMessageSerializer, Akka.Remote" = 22
                    "Akka.Remote.Artery.ArteryControlMessageSerializer, Akka.Remote" = 23
                }
            }
            """);

        /// <summary>The rows <c>src/core/Akka.Streams/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config Streams { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-stream-ref = "Akka.Streams.Serialization.StreamRefSerializer, Akka.Streams"
                }
                serialization-bindings {
                    "Akka.Streams.Implementation.StreamRef.SinkRefImpl, Akka.Streams"         = akka-stream-ref
                    "Akka.Streams.Implementation.StreamRef.SourceRefImpl, Akka.Streams"       = akka-stream-ref
                    "Akka.Streams.Implementation.StreamRef.IStreamRefsProtocol, Akka.Streams" = akka-stream-ref
                }
                serialization-identifiers {
                    "Akka.Streams.Serialization.StreamRefSerializer, Akka.Streams" = 30
                }
            }
            """);

        /// <summary>The rows <c>src/core/Akka.Cluster/Configuration/Cluster.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config Cluster { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-cluster = "Akka.Cluster.Serialization.ClusterMessageSerializer, Akka.Cluster"
                    reliable-delivery = "Akka.Cluster.Serialization.ReliableDeliverySerializer, Akka.Cluster"
                }
                serialization-bindings {
                    "Akka.Cluster.IClusterMessage, Akka.Cluster" = akka-cluster
                    "Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster" = akka-cluster
                    "Akka.Delivery.Internal.IDeliverySerializable, Akka" = reliable-delivery
                }
                serialization-identifiers {
                    "Akka.Cluster.Serialization.ClusterMessageSerializer, Akka.Cluster" = 5
                    "Akka.Cluster.Serialization.ReliableDeliverySerializer, Akka.Cluster" = 36
                }
            }
            """);

        /// <summary>The rows <c>src/contrib/cluster/Akka.Cluster.Tools/Client/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config ClusterToolsClient { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-cluster-client = "Akka.Cluster.Tools.Client.Serialization.ClusterClientMessageSerializer, Akka.Cluster.Tools"
                }
                serialization-bindings {
                    "Akka.Cluster.Tools.Client.IClusterClientMessage, Akka.Cluster.Tools" = akka-cluster-client
                    "Akka.Cluster.Tools.Client.IClusterClientProtocolMessage, Akka.Cluster.Tools" = akka-cluster-client
                }
                serialization-identifiers {
                    "Akka.Cluster.Tools.Client.Serialization.ClusterClientMessageSerializer, Akka.Cluster.Tools" = 15
                }
            }
            """);

        /// <summary>The rows <c>src/contrib/cluster/Akka.Cluster.Tools/PublishSubscribe/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config ClusterToolsPubSub { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-pubsub = "Akka.Cluster.Tools.PublishSubscribe.Serialization.DistributedPubSubMessageSerializer, Akka.Cluster.Tools"
                }
                serialization-bindings {
                    "Akka.Cluster.Tools.PublishSubscribe.IDistributedPubSubMessage, Akka.Cluster.Tools" = akka-pubsub
                    "Akka.Cluster.Tools.PublishSubscribe.Internal.SendToOneSubscriber, Akka.Cluster.Tools" = akka-pubsub
                }
                serialization-identifiers {
                    "Akka.Cluster.Tools.PublishSubscribe.Serialization.DistributedPubSubMessageSerializer, Akka.Cluster.Tools" = 9
                }
            }
            """);

        /// <summary>The rows <c>src/contrib/cluster/Akka.Cluster.Tools/Singleton/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config ClusterToolsSingleton { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-singleton = "Akka.Cluster.Tools.Singleton.Serialization.ClusterSingletonMessageSerializer, Akka.Cluster.Tools"
                }
                serialization-bindings {
                    "Akka.Cluster.Tools.Singleton.IClusterSingletonMessage, Akka.Cluster.Tools" = akka-singleton
                }
                serialization-identifiers {
                    "Akka.Cluster.Tools.Singleton.Serialization.ClusterSingletonMessageSerializer, Akka.Cluster.Tools" = 14
                }
            }
            """);

        /// <summary>The rows <c>src/contrib/cluster/Akka.Cluster.Sharding/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config Sharding { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-sharding = "Akka.Cluster.Sharding.Serialization.ClusterShardingMessageSerializer, Akka.Cluster.Sharding"
                }
                serialization-bindings {
                    "Akka.Cluster.Sharding.IClusterShardingSerializable, Akka.Cluster.Sharding" = akka-sharding
                }
                serialization-identifiers {
                    "Akka.Cluster.Sharding.Serialization.ClusterShardingMessageSerializer, Akka.Cluster.Sharding" = 13
                }
            }
            """);

        /// <summary>The rows <c>src/contrib/cluster/Akka.DistributedData/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config DistributedData { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-data-replication = "Akka.DistributedData.Serialization.ReplicatorMessageSerializer, Akka.DistributedData"
                    akka-replicated-data = "Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData"
                }
                serialization-bindings {
                    "Akka.DistributedData.IReplicatorMessage, Akka.DistributedData" = akka-data-replication
                    "Akka.DistributedData.IReplicatedDataSerialization, Akka.DistributedData" = akka-replicated-data
                }
                serialization-identifiers {
                    "Akka.DistributedData.Serialization.ReplicatedDataSerializer, Akka.DistributedData" = 11
                    "Akka.DistributedData.Serialization.ReplicatorMessageSerializer, Akka.DistributedData" = 12
                }
            }
            """);

        /// <summary>The rows <c>src/contrib/cluster/Akka.Cluster.Metrics/reference.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config Metrics { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-cluster-metrics = "Akka.Cluster.Metrics.Serialization.ClusterMetricsMessageSerializer, Akka.Cluster.Metrics"
                }
                serialization-bindings {
                    "Akka.Cluster.Metrics.Serialization.MetricsGossipEnvelope, Akka.Cluster.Metrics" = akka-cluster-metrics
                    "Akka.Cluster.Metrics.AdaptiveLoadBalancingPool, Akka.Cluster.Metrics" = akka-cluster-metrics
                    "Akka.Cluster.Metrics.MixMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
                    "Akka.Cluster.Metrics.CpuMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
                    "Akka.Cluster.Metrics.MemoryMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
                }
                serialization-identifiers {
                    "Akka.Cluster.Metrics.Serialization.ClusterMetricsMessageSerializer, Akka.Cluster.Metrics" = 10
                }
            }
            """);

        /// <summary>The rows <c>src/core/Akka.Persistence/persistence.conf</c> shipped at <c>1.6.0-beta1</c>.</summary>
        public static Config Persistence { get; } = ConfigurationFactory.ParseString("""
            akka.actor {
                serializers {
                    akka-persistence-message = "Akka.Persistence.Serialization.PersistenceMessageSerializer, Akka.Persistence"
                    akka-persistence-snapshot = "Akka.Persistence.Serialization.PersistenceSnapshotSerializer, Akka.Persistence"
                }
                serialization-bindings {
                    "Akka.Persistence.Serialization.IMessage, Akka.Persistence" = akka-persistence-message
                    "Akka.Persistence.Serialization.Snapshot, Akka.Persistence" = akka-persistence-snapshot
                }
                serialization-identifiers {
                    "Akka.Persistence.Serialization.PersistenceMessageSerializer, Akka.Persistence" = 7
                    "Akka.Persistence.Serialization.PersistenceSnapshotSerializer, Akka.Persistence" = 8
                }
            }
            """);

        /// <summary>The three Akka.Cluster.Tools files' rows, as one config.</summary>
        public static Config ClusterTools { get; } =
            ClusterToolsClient.WithFallback(ClusterToolsPubSub).WithFallback(ClusterToolsSingleton);

        /// <summary>Every module's frozen rows, by module name.</summary>
        public static IReadOnlyList<(string Module, Config Rows)> All { get; } = new[]
        {
            ("Akka.Remote", Remote),
            ("Akka.Streams", Streams),
            ("Akka.Cluster", Cluster),
            ("Akka.Cluster.Tools", ClusterTools),
            ("Akka.Cluster.Sharding", Sharding),
            ("Akka.DistributedData", DistributedData),
            ("Akka.Cluster.Metrics", Metrics),
            ("Akka.Persistence", Persistence),
        };
    }
}
