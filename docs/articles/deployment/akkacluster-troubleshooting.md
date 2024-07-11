---
uid: cluster-troubleshooting
title: Troubleshooting Akka.Cluster
---

# Troubleshooting Akka.Cluster

[Akka.Cluster](xref:cluster-overview) is designed to support highly available distributed Akka.NET applications and it can operate at large scale. However, prior to deploying Akka.Cluster into a large-scale environment it's useful to know how to troubleshoot various problems that may occur at runtime or exceptions you might see in your Akka.NET logs. This guide explains how to troubleshoot some routine problems that may occur with Akka.Cluster.

## Network Splits, Split Brains, and Initial Cluster Formation Issues

Even during hostile network conditions Akka.Cluster should not break apart into multiple clusters - and when configured correctly Akka.Cluster shouldn't be able to form multiple clusters during launch either. If your cluster is breaking apart into multiple discrete clusters or partitions that are unreachable, this guide will help you troubleshoot potential root causes and fixes for this issue.

### Multiple Clusters Form or Cluster Can't Form

When multiple clusters form after or during a network partition, or none form, it's for at least one of the following reasons:

1. **Inconsistent [Split Brain Resolver](xref:split-brain-resolver) configuration** - check to make sure that the configuration is _identical_ on all nodes. If it's not, then two different cluster leaders on either side of a network partition can both decide that they're the leader and down each other. This can result in multiple networks forming.
2. **Inconsistent `akka.cluster.seed-node` configurations** - if you're using a static seed node strategy, all seeds should be listed in identical order on all nodes _including the seed nodes_ themselves. Otherwise, when nodes restart they are each going to join per whatever their local configuration says - and if those values vary across the cluster you'll get different behavior. Another way to fix this issue is to use [Akka.Discovery](xref:akka-discovery) and [Akka.Cluster.Bootstrap](https://github.com/akkadotnet/Akka.Management) to automatically discover seed nodes; this will eliminate the issue by dynamically discovering the same consistent set of nodes each and every time via the Akka.Discovery mechanism.
3. **Indirectly connected nodes** - this is [a limitation of classic Akka.Remote](https://github.com/akkadotnet/akka.net/issues/4757) up until Akka.NET v1.5. Once nodes start becoming `Quarantined` in Akka.Remote they can no longer receive Akka.Cluster commands, such as `Down` and `Leave`. As a result, these nodes are unreachable but also can't be downed externally via the SBR if the cluster leader has quarantined the indirectly connected node or has been quarantined by it. The fix for this if the issue doesn't eventually resolve itself is to use [Petabridge.Cmd's `cluster down` command](https://cmd.petabridge.com/articles/commands/cluster-commands.html#cluster-down) directly on the effected node and force it to exit or to terminate the process. You an also enable `akka.cluster.split-brain-resolver.down-all-when-unstable = on` to force a cluster-wide reboot if this issue is severe.

## Unreachable Nodes

Unreachable nodes occur when the `akka.cluster.failure-detector` isn't able to receive heartbeats from a node within the expected threshold.

### Failure Detector Threshold

What is that threshold exactly?

```hocon
akka.cluster.failure-detector {

  # FQCN of the failure detector implementation.
  # It must implement akka.remote.FailureDetector and have
  # a public constructor with a com.typesafe.config.Config and
  # akka.actor.EventStream parameter.
  implementation-class = "Akka.Remote.PhiAccrualFailureDetector, Akka.Remote"

  # How often keep-alive heartbeat messages should be sent to each connection.
  heartbeat-interval = 1 s

  # Defines the failure detector threshold.
  # A low threshold is prone to generate many wrong suspicions but ensures
  # a quick detection in the event of a real crash. Conversely, a high
  # threshold generates fewer mistakes but needs more time to detect
  # actual crashes.
  threshold = 8.0

  # Number of the samples of inter-heartbeat arrival times to adaptively
  # calculate the failure timeout for connections.
  max-sample-size = 1000

  # Minimum standard deviation to use for the normal distribution in
  # AccrualFailureDetector. Too low standard deviation might result in
  # too much sensitivity for sudden, but normal, deviations in heartbeat
  # inter arrival times.
  min-std-deviation = 100 ms

  # Number of potentially lost/delayed heartbeats that will be
  # accepted before considering it to be an anomaly.
  # This margin is important to be able to survive sudden, occasional,
  # pauses in heartbeat arrivals, due to for example garbage collect or
  # network drop.
  acceptable-heartbeat-pause = 3 s

  # Number of member nodes that each member will send heartbeat messages to,
  # i.e. each node will be monitored by this number of other nodes.
  monitored-by-nr-of-members = 9

  # After the heartbeat request has been sent the first failure detection
  # will start after this period, even though no heartbeat mesage has
  # been received.
  expected-response-after = 1 s

}
```

Akka.Cluster's failure detector implements a [phi accrual strategy](https://medium.com/@arpitbhayani/phi-%CF%86-accrual-failure-detection-79c21ce53a7a), which means the amount of heartbeat latency it will tolerate is adaptive - determined by samples collected over the lifespan of an association between two `ActorSystem`s. However, once the system being monitored fails to respond to multiple heartbeat pings within an acceptable time frame then the node sending the pings will mark the node that's supposed to respond to the pings as "unreachable."

> ![IMPORTANT]
> All nodes in an Akka.NET cluster are monitored for reachability by up to 9 other nodes by default. It only takes 1 of those 9 nodes to mark a node as "unreachable."

### What Causes Unreachable Nodes?

So why would a node no longer send heartbeat pings back over the network?

1. **Crashed or terminated process** - the process or hardware hosting the `ActorSystem` is gone and the node is really down for good, in which case the unreachable node needs to be `Down`ed by the [Split Brain Resolver](xref:split-brain-resolver) and removed from the cluster.
2. **Pegged CPU, constrained bandwidth, or saturated work queue** - the process is alive, but unable to respond due to resource constraints. These resource constraints might be relieved in short order though, so a node that is temporarily unreachable might become reachable again in short order.
3. **Suspended or paused processes** - a process might be throttled by the Kubernetes control plane, a hypervisor, the OS, or possibly paused due to a runtime issue like garbage collection. These processes might become reachable again if they aren't paused for too long.
4. **Network disruptions** - if a virtual or physical network device malfunctions, causing TCP connections to drop, that will cause effected nodes to automatically mark each other as unreachable until they're able to re-establish connectivity again.

### Decreasing Frequency of Unreachable Nodes

Generally speaking, unreachable nodes are usually caused by environment problems - however, there are some user-driven behaviors that can help reduce the frequency of unreachable node occurrence.

#### Use Akka.Hosting

When you use [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting), this ensures that your `ActorSystem` is managed with the best lifecycle management practices for Akka.NET. Part of this includes making sure that when an Akka.NET process is shutdown it cleanly leaves the cluster first before terminating. One common reason for reachability problems is that during deployments users simply abort the Akka.NET process without letting the `ActorSystem` gracefully terminate, which leaves behind an unreachable node. Akka.Hosting eliminates this problem.

#### Increase Failure Detector Thresholds

One thing we can do to reduce the rate of unreachable nodes in Akka.Cluster is to make the `akka.cluster.failure-detector` less sensitive, by changing the following values:

* `akka.cluster.failure-detector.threshold` - change this value from `8.0` to `24.0`; this will make the cluster much slower at detecting true failures (i.e. hardware) but much less likely to mark a node that is temporarily busy as unreachable.
* `akka.cluster.failure-detectoracceptable-heartbeat-pause` - change this value from `3s` to `9s`, which gives the node being monitored 3x as long to respond to each heartbeat before it's considered to be a network anomaly.

All of these configuration tweaks will reduce the rate at which a truly unreachable node is detected. The out of the box defaults are pretty reasonable in most cases.

## Serialization Errors

If you see errors like the following:

```text
Cause: System.Runtime.Serialization.SerializationException:
Failed to deserialize payload object when deserializing ActorSelectionMessage with payload
[SerializerId=9, Manifest=A] addressed to [system,distributedPubSubMediator].
Could not find any internal Akka.NET serializer with Id [9].
Please create an issue in our GitHub at [https://github.com/akkadotnet/akka.net].
```

This typically means that one of the optional serializers built on top of Akka.Cluster is not registered on this node, but this node is still receiving messages from other nodes who are using it. [`DistributedPubSub`](xref:distributed-publish-subscribe) is the most likely culprit when this occurs.

To fix this issue, either use [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting) or manually register the serializers in your HOCON when you start your `ActorSystem`:

```csharp
Config myHocon = ConfigurationFactory.ParseString("{hocon}");
Config fullHocon = myHocon.WithFallback(ClusterSharding.DefaultConfig()
                .WithFallback(ClusterSingletonManager.DefaultConfig())
                .WithFallback(DistributedPubSub.DefaultConfig())
                .WithFallback(ClusterClientReceptionist.DefaultConfig()));
```

This will load all of the serializers for Akka.Cluster.Tools and Akka.Cluster.Sharding. That will usually alleviate this issue.
