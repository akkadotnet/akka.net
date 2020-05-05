---
uid: remote-performance
title: Network and Performance Optimization
---

# Akka.Remote Performance Optimization

If you're building a large-scale system using Akka.NET, the performance of Akka.Remote is going to be one of the most critical factors of your application as it's directly responsible for managing the rate at which messages move across a single connection between two nodes. 

Akka.NET is [horizontally scalable using Akka.Cluster](../clustering/cluster-overview.md), meaning that Akka.NET applications typically respond to surges in demand by scaling up and down the number of nodes inside the network - however, ensuring that the messages per node throughput is consistently high will give you the most value on a per-node basis.

## Optimizing the DotNetty TCP Transport
The default transport in Akka.Remote is the DotNetty TCP transport, which is a good choice for most Akka.NET developers because TCP preserves message order and guarantees delivery of every message over the wire so long as the network remains available, which is consistent with `IActorRef` default messaging. The same cannot be said for alternative transports such as a UDP transport.

As of Akka.NET v1.4.0-beta4, we've added a new feature to Akka.Remote called a "batch writer" which has tremendously improved the performance of outbound writes in Akka.Remote by grouping many logical writes into a smaller number of physical writes (actual flush calls to the underlying socket.)

Here are some performance numbers from our [RemotePingPong benchmark](https://github.com/akkadotnet/akka.net/tree/dev/src/benchmark/RemotePingPong), which uses high volumes of small messages. 

These numbers were all produced on a 12 core Intel i7 2.6Ghz Dell laptop over a single Akka.Remote connection running .NET Core 2.1 on Windows 10:

### No I/O Batching

| Num clients (actors) | Total [msg] | Msgs/sec | Total [ms] |
|----------------------|-------------|----------|------------|
| 1                    | 200000      | 69736    | 2868.60    |
| 5                    | 1000000     | 141243   | 7080.98    |
| 10                   | 2000000     | 136771   | 14623.27   |
| 15                   | 3000000     | 38190    | 78556.49   |
| 20                   | 4000000     | 32401    | 123454.60  |
| 25                   | 5000000     | 33341    | 149967.08  |
| 30                   | 6000000     | 126093   | 47584.92   |

Average performance: **82,539 msg/s**.

Standard deviation: **46,827 msg/s**.

### With I/O Batching

| Num clients (actors) | Total [msg] | Msgs/sec | Total [ms] |
|----------------------|-------------|----------|------------|
| 1                    | 200000      | 106610   | 1876.02    |
| 5                    | 1000000     | 161031   | 6210.60    |
| 10                   | 2000000     | 145805   | 13717.31   |
| 15                   | 3000000     | 145209   | 20660.47   |
| 20                   | 4000000     | 143211   | 27931.22   |
| 25                   | 5000000     | 142621   | 35058.92   |
| 30                   | 6000000     | 143147   | 41915.11   |

Average performance: **141,091 msg/s**.

Standard deviation: **15,291 msg/s**.

Akka.Remote's flush batching system for DotNetty operates on the principle of trying to minimize the number of system calls to the I/O system because they are (1) expensive and (2) unpredictably long-running. Our data shows grouping together dozens of small writes, ~218 bytes per message in this sample, into larger payloads in the 4kb range significantly reduces the standard deviation for this benchmark, significantly improves the average, and also increases the maximum absolute throughput observed.

You can achieve similar results for your application.

### Optimizing Batches for Your Use Case
To take advantage of I/O batching in DotNetty, you need to [tailor the following Akka.Remote configuration values to your use case](../../configuration/akka.remote.md):

```
akka.remote.dot-netty.tcp{
      batching{

        enabled = true

        max-pending-writes = 30

        max-pending-bytes = 16k

        flush-interval = 40ms
      }
}
```

The batching system inside the DotNetty TCP transport works using the following business rules.

Only flush to the underlying transport when one of these happens:

1. **`batching.max-pending-writes`** - When 30 total messages, irrespective of their size, are queued for writing - flush the batch immediately upon receiving the 30th write;
2. **`batching.max-pending-bytes`** - When 16kb worth of messages have been queued, regardless of the number of messages, flush the batch immediately;
3. **`batching.flush-interval`** - If neither of the two previous conditions have been met flush whatever is waiting inside the queue for this connection within 40 milliseconds.

The key to performance tuning the DotNetty TCP transport is picking the best values for your application. Some scenarios:

* If your application sends a relatively small number of messages, but those messages can have some large payload sizes, increase the `max-pending-bytes` value to an appropriate figure.
* If your application has a small number of remote actors who need to communicate with the lowest possible latency, decrease the `max-pending-writes` value so you don't have to wait for the `flush-interval` to hit.
* If your application has a large number of actors sending a large number of messages that vary in size, use the default values - they're tailored for that case.

> [!TIP]
> If you're worried about adverse impact of implementing batching in your Akka.NET application because you aren't sure yet how it should be calibrated for your specific use cases, you can disable batching via `akka.remote.dot-netty.tcp.batching.enabled = false` - this will allow the DotNetty transport to continue to run as it did prior to Akka.NET v1.4.0-beta4.

> [!IMPORTANT]
> Later on in the Akka.NET v1.4.0 release cycle we will be releasing some tools built into Akka.Remote that will make it easier to collect profiling data about your application's performance. This should make it much easier to determine what sort of configuration you may want to use with the DotNetty TCP batching system.

## Further Reading
See [Petabridge](https://petabridge.com/)'s video on the subject, "[Akka.Remote Performance Optimization in Akka.NET v1.4](https://www.youtube.com/watch?v=mP3amXEntmQ)"

<iframe width="560" height="315" src="https://www.youtube.com/embed/mP3amXEntmQ" frameborder="0" allow="accelerometer; autoplay; encrypted-media; gyroscope; picture-in-picture" allowfullscreen></iframe>