---
uid: remote-performance
title: Network and Performance Optimization
---

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

Average performance: 82,539 msg/s.
Standard deviation: 46,827 msg/s.

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

Average performance: 141,091 msg/s.
Standard deviation: 15,291 msg/s.

Akka.Remote's flush batching system for DotNetty operates on the principle of trying to minimize the number of system calls to the I/O system because they are (1) expensive and (2) unpredictably long-running. As our data has shown, grouping together dozens of small writes, ~218 bytes per message in this sample, into larger payloads in the 4kb range significantly reduces the standard deviation for this benchmark, significantly improves the average, and also increases the maximum absolute throughput observed.

### Optimizing Batches for Your Use Case
To take advantage of I/O batching in DotNetty, you need to [tailor the following Akka.Remote configuration values to your use case](../../configuration/akka.remote.md):

```
akka.remote.dot-netty.tcp{
	  # PERFORMANCE TUNING
      # 
      # The batching feature of DotNetty is designed to help batch together logical writes into a smaller
      # number of physical writes across the socket. This helps significantly reduce the number of system calls
      # and can improve performance by as much as 150% on some systems when enabled.
      batching{

        # Enables the batching system. When disabled, every write will be flushed immediately.
        # Disable this setting if you're working with a VERY low-traffic system that requires
        # fast (< 40ms) acknowledgement for all periodic messages.
        enabled = true

        # The max write threshold based on the number of logical messages regardless of their size. 
        # This is a safe default value - decrease it if you have a small number of remote actors
        # who engage in frequent request->response communication which requires low latency (< 40ms).
        max-pending-writes = 30

        # The max write threshold based on the byte size of all buffered messages. If there are 4 messages
        # waiting to be written (with batching.max-pending-writes = 30) but their total size is greater than
        # batching.max-pending-bytes, a flush will be triggered immediately.
        #
        # Increase this value is you have larger message sizes and watch to take advantage of batching, but
        # otherwise leave it as-is.
        #
        # NOTE: this value should always be smaller than dot-netty.tcp.maximum-frame-size.
        max-pending-bytes = 16k

        # In the event that neither the batching.max-pending-writes or batching.max-pending-bytes
        # is hit we guarantee that all pending writes will be flushed within this interval.
        #
        # This setting, realistically, can't be enforced any lower than the OS' clock resolution (~20ms).
        # If you have a very low-traffic system, either disable pooling altogether or lower the batching.max-pending-writes
        # threshold to maximize throughput. Otherwise, leave this setting as-is.
        flush-interval = 40ms
      }
}
```

The batching system inside the DotNetty TCP transport works using the following business rules.

Only flush to the underlying transport when one of these happens:

1. **`batching.max-pending-writes`** - When 30 total messages, irrespective of their size, are queued for writing - flush the batch immediately upon receiving the 30th write;
2. **`batching.max-pending-bytes`** - When 16kb worth of messages have been queued, regardless of the number of messages, flush the batch immediately;
3. **`batching.flush-interval`** - If neither of the two previous conditions have been met flush whatever is waiting inside the queue for this connection within 40 milliseconds.

