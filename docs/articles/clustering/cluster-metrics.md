---
uid: cluster-metrics
title: Akka.Cluster.Metrics module
---

# Akka.Cluster.Metrics module

The member nodes of the cluster can collect system health metrics and publish that to other cluster nodes and 
to the registered subscribers on the system event bus with the help of Cluster Metrics Extension.

Cluster metrics information is primarily used for load-balancing routers, 
and can also be used to implement advanced metrics-based node life cycles, such as “Node Let-it-crash” when CPU steal time becomes excessive.

Cluster members with status `WeaklyUp`, if that feature is enabled, will participate in Cluster Metrics collection and dissemination.

## Metrics Collector

Metrics collection is delegated to an implementation of `Akka.Cluster.Metrics.IMetricsCollector`.

Different collector implementations may provide different subsets of metrics published to the cluster. 
Metrics currently supported are defined in `Akka.Cluster.Metrics.StandardMetrics` class:
* `MemoryUsed` - total memory allocated to the currently running process
* `MemoryAvailable` - memory, available for the process
* `MaxMemoryRecommended` - if set, memory limit recommended for current process
* `Processors` - number of available processors
* `CpuProcessUsage` - CPU usage by current process
* `CpuTotalUsage` - total CPU usage

> Note: currently, due to some .NET Core limitations `CpuTotalUsage` is the same as `CpuProcessUsage` metrics, 
> but this is something to be fixed in near future (see [this issue](https://github.com/akkadotnet/akka.net/issues/4142) for details).

Cluster metrics extension comes with built-in `Akka.Cluster.Metrics.Collectors.DefaultCollector` collector implementation, 
which collects all metrics defined above.

You can also plug-in your own metrics collector implementation.

By default, metrics extension will use collector provider fall back and will try to load them in this order:
1. configured user-provided collector (see `Configuration` section for details)
2. built-in `Akka.Cluster.Metrics.Collectors.DefaultCollector` collector

## Metrics Events

Metrics extension periodically publishes current snapshot of the cluster metrics to the node system event bus.

The publication interval is controlled by the `akka.cluster.metrics.collector.sample-interval` setting.

The payload of the `Akka.Cluster.Metrics.Events.ClusterMetricsChanged` event will contain latest metrics of the node as well as 
other cluster member nodes metrics gossip which was received during the collector sample interval.

You can subscribe your metrics listener actors to these events in order to implement custom node lifecycle:
```c#
ClusterMetrics.Get(Sys).Subscribe(metricsListenerActor);
```

## Adaptive Load Balancing

The `AdaptiveLoadBalancingPool` / `AdaptiveLoadBalancingGroup` performs load balancing of messages to cluster nodes based on the cluster metrics data. 
It uses random selection of routees with probabilities derived from the remaining capacity of the corresponding node. 
It can be configured to use a specific `IMetricsSelector` implementation to produce the probabilities, a.k.a. weights:

* `memory` / `MemoryMetricsSelector` - Used and max available memory. Weights based on remaining memory capacity: (max - used) / max
* `cpu` / `CpuMetricsSelector` - CPU utilization in percentage. Weights based on remaining cpu capacity: 1 - utilization
* `mix` / `MixMetricsSelector` - Combines memory and cpu. Weights based on mean of remaining capacity of the combined selectors.

The collected metrics values are smoothed with [exponential weighted moving average](https://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average). 
In the cluster configuration you can adjust how quickly past data is decayed compared to new data.

Let’s take a look at this router in action. What can be more demanding than calculating factorials?

The backend worker that performs the factorial calculation:

[!code-csharp[RouterUsageSample](../../../src/core/Akka.Docs.Tests/Cluster.Metrics/RouterUsageSample.cs?name=FactorialBackend)]

The frontend that receives user jobs and delegates to the backends via the router:

[!code-csharp[RouterUsageSample](../../../src/core/Akka.Docs.Tests/Cluster.Metrics/RouterUsageSample.cs?name=FactorialFrontend)]

As you can see, the router is defined in the same way as other routers, and in this case it is configured as follows:

```
akka.actor.deployment {
  /factorialFrontend/factorialBackendRouter = {
    # Router type provided by metrics extension.
    router = cluster-metrics-adaptive-group
    # Router parameter specific for metrics extension.
    # metrics-selector = memory
    # metrics-selector = cpu
    metrics-selector = mix
    #
    routees.paths = ["/user/factorialBackend"]
    cluster {
      enabled = on
      use-roles = ["backend"]
      allow-local-routees = off
    }
  }
}
```

It is only `router` type and the `metrics-selector` parameter that is specific to this router, other things work in the same way as other routers.

The same type of router could also have been defined in code:

### Group Router

[!code-csharp[RouterInCodeSample](../../../src/core/Akka.Docs.Tests/Cluster.Metrics/RouterInCodeSample.cs?name=RouterInCodeSample1)]

### Pool Router

[!code-csharp[RouterInCodeSample](../../../src/core/Akka.Docs.Tests/Cluster.Metrics/RouterInCodeSample.cs?name=RouterInCodeSample2)]

## Subscribe to Metrics Events

It is possible to subscribe to the metrics events directly to implement other functionality.

[!code-csharp[MetricsListenerSample](../../../src/core/Akka.Docs.Tests/Cluster.Metrics/MetricsListenerSample.cs)]

## Custom Metrics Collector

Metrics collection is delegated to the implementation of `Akka.Cluster.Metrics.IMetricsCollector`.

You can plug-in your own metrics collector instead of built-in `Akka.Cluster.Metrics.Collectors.DefaultCollector`.

Custom metrics collector implementation class must be specified in the `akka.cluster.metrics.collector.provider` configuration property.

## Configuration

The Cluster metrics extension can be configured with the following properties:

```
##############################################
# Akka Cluster Metrics Reference Config File #
##############################################

# This is the reference config file that contains all the default settings.
# Make your edits in your application.conf in order to override these settings.

# Cluster metrics extension.
# Provides periodic statistics collection and publication throughout the cluster.
akka.cluster.metrics {

  # Full path of dispatcher configuration key.
  dispatcher = "akka.actor.default-dispatcher"

  # How long should any actor wait before starting the periodic tasks.
  periodic-tasks-initial-delay = 1s

  # Metrics supervisor actor.
  supervisor {

    # Actor name. Example name space: /system/cluster-metrics
    name = "cluster-metrics"

    # Supervision strategy.
    strategy {

      # FQCN of class providing `akka.actor.SupervisorStrategy`.
      # Must have a constructor with signature `<init>(com.typesafe.config.Config)`.
      # Default metrics strategy provider is a configurable extension of `OneForOneStrategy`.
      provider = "Akka.Cluster.Metrics.ClusterMetricsStrategy, Akka.Cluster.Metrics"
      
      # Configuration of the default strategy provider.
      # Replace with custom settings when overriding the provider.
      configuration = {

        # Log restart attempts.
        loggingEnabled = true

        # Child actor restart-on-failure window.
        withinTimeRange = 3s

        # Maximum number of restart attempts before child actor is stopped.
        maxNrOfRetries = 3
      }
    }
  }

  # Metrics collector actor.
  collector {

    # Enable or disable metrics collector for load-balancing nodes.
    # Metrics collection can also be controlled at runtime by sending control messages
    # to /system/cluster-metrics actor: `akka.cluster.metrics.{CollectionStartMessage,CollectionStopMessage}`
    enabled = on

    # FQCN of the metrics collector implementation.
    # It must implement `akka.cluster.metrics.MetricsCollector` and
    # have public constructor with akka.actor.ActorSystem parameter.
    # Will try to load in the following order of priority:
    # 1) configured custom collector 2) internal `SigarMetricsCollector` 3) internal `JmxMetricsCollector`
    provider = ""

    # Try all 3 available collector providers, or else fail on the configured custom collector provider.
    fallback = true

    # How often metrics are sampled on a node.
    # Shorter interval will collect the metrics more often.
    # Also controls frequency of the metrics publication to the node system event bus.
    sample-interval = 3s

    # How often a node publishes metrics information to the other nodes in the cluster.
    # Shorter interval will publish the metrics gossip more often.
    gossip-interval = 3s

    # How quickly the exponential weighting of past data is decayed compared to
    # new data. Set lower to increase the bias toward newer values.
    # The relevance of each data sample is halved for every passing half-life
    # duration, i.e. after 4 times the half-life, a data sample’s relevance is
    # reduced to 6% of its original relevance. The initial relevance of a data
    # sample is given by 1 – 0.5 ^ (collect-interval / half-life).
    # See http://en.wikipedia.org/wiki/Moving_average#Exponential_moving_average
    moving-average-half-life = 12s
  }
}

# Cluster metrics extension serializers and routers.
akka.actor {

  # Protobuf serializer for remote cluster metrics messages.
  serializers {
    akka-cluster-metrics = "Akka.Cluster.Metrics.Serialization.ClusterMetricsMessageSerializer, Akka.Cluster.Metrics"
  }

  # Interface binding for remote cluster metrics messages.
  serialization-bindings {
    "Akka.Cluster.Metrics.Serialization.MetricsGossipEnvelope, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.AdaptiveLoadBalancingPool, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.MixMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.CpuMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
    "Akka.Cluster.Metrics.MemoryMetricsSelector, Akka.Cluster.Metrics" = akka-cluster-metrics
  }

  # Globally unique metrics extension serializer identifier.
  serialization-identifiers {
    "Akka.Cluster.Metrics.Serialization.ClusterMetricsMessageSerializer, Akka.Cluster.Metrics" = 10
  }

  #  Provide routing of messages based on cluster metrics.
  router.type-mapping {
    cluster-metrics-adaptive-pool = "Akka.Cluster.Metrics.AdaptiveLoadBalancingPool, Akka.Cluster.Metrics"
    cluster-metrics-adaptive-group = "Akka.Cluster.Metrics.AdaptiveLoadBalancingGroup, Akka.Cluster.Metrics"
  }
}
```