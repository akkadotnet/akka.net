---
uid: akkadotnet-v14-migration-guide
title: What's new in Akka.NET v1.4.0?
---

# What's new in Akka.NET v1.4.0?

Akka.NET v1.4.0 is the culmination of many major architectural changes, improvements, bugfixes, and updates to the core Akka.NET runtime and its associated modules.

## Summary
The high-level summary of changes:


1. [Akka.Cluster.Sharding](../../articles/clustering/cluster-sharding.md) and [Akka.DistributedData](../../articles/clustering/distributed-data.md) are now out of beta status, have stable wire formats, and all sharding functionality is fully supported using either `akka.cluster.sharding.state-store-mode = "persistence"` or `akka.cluster.sharding.state-store-mode = "ddata"`.
2. Akka.Remote's performance has significantly increased as a function of our new batching mode ([see the numbers](../../articles/remoting/performance.md#no-io-batching)) - which is tunable via HOCON configuration to best support your needs. [Learn how to performance optimize Akka.Remote here](../../articles/remoting/performance.md).
3. Added a new module, [Akka.Cluster.Metrics](../../articles/cluster/cluster-metrics.md), which allows clustering users to receive performance data about each of the nodes in their cluster and even create some routers that can direct the flow of messages based on these metrics. 
4. Added ["Stream References" to Akka.Streams](../../articles/streams/streamrefs.md), a feature which allows Akka.Streams graphs to span across the network.
5. Moved from .NET Standard 1.6 to .NET Standard 2.0 and dropped support for all versions of .NET Framework that aren't supported by .NET Standard 2.0 - this means issues caused by our polyfills (i.e. `SerializableAttribute`) should be fully resolved now.
6. Moved onto modern C# best practices, such as changing our APIs to support `System.ValueTuple` over regular tuples.

If you want a detailed summary of changes and numerous bug fixes, [please see the Akka.NET v1.4.0 milestone, which has over 140 resolved issues attached to it](https://github.com/akkadotnet/akka.net/milestone/17).

### Changes Between Akka.NET v1.4.1-RC1 and Akka.NET v1.4.1-RC2/3/Stable
In Akka.NET v1.4.1-RC1 we introduced an ambitious change to migrate all HOCON configuration from an internal namespace (`Akka.Configuration`, defined inside the core `Akka` NuGet package) to a stand-alone library:

> All HOCON `Config` objects have been moved from the `Akka.Configuration` namespace within the core `Akka` library to the stand-alone [HOCON project](https://github.com/akkadotnet/HOCON) and its subsequent [HOCON.Configuration NuGet package](https://www.nuget.org/packages/Hocon.Configuration/). See ["HOCON Syntax and Practices in Akka.NET"](../../articles/hocon/index.md) for a full guide on all of the different APIs, standards, and syntax supported. **This is a breaking change**, so [follow the Akka.NET v1.4.0 migration guide](#migration) below.

In Akka.NET v1.4.1-RC2 we rolled this change back in order to:

1. Make Akka.NET v1.4 a non-breaking change for all users and
2. Resolve stability and performance issues that were discovered with stand-alone HOCON after release.

More details in the next section below.

#### Post Mortem: Stand-alone HOCON
In the previous releases of HOCON, we let the OSS project do its own thing without any real top-down plan for integrating it into Akka.NET and replacing the stand-alone HOCON engine built into the `Akka.Configuration.Config` class. 

This was a mistake and led to us having to drop stand-alone HOCON integration as part of the Akka.NET v1.4.1 milestone.

The specific problems we had with stand-alone HOCON were:

1. Mutability - we couldn't guarantee that the object model was immutable following a parse operation, because the same architecture that was used for object construction during parse was also used for reads after parse, a fundamental architectural mistake. In addition to some performance problems, this also lead to 
2. Performance - appending a new fallback to a `HOCON.Config` object kicked off a processes of recursive deep-copying, and this was quickly found to be non-performant.
3. Inadequacies in the Akka.NET test suite - the Akka.NET test suite is very extensive, but as we discovered during the Akka.NET v1.4.1-RC1 process: our test configurations are not nearly as complex as real-world test cases are. 

#### Stand-alone HOCON Future
Over the course of the Akka.NET v1.4 development cycle, where we will begin introducing lots of the usual bug fixes, feature additions, and performance improvements we will begin the process of gradually introducing abstractions to make it desirable, safe, and backwards-compatible to introduce stand-alone HOCON.

If you want to learn more, [read the HOCON 3.0 specification and development plan here](https://github.com/akkadotnet/HOCON/issues/267).

In addition to that, we're in the process of [developing automated integration tests](https://github.com/akkadotnet/akka.net-integration-tests) that mimic what Akka.NET users do in the real world using [Akka.NET nightly builds](../getting-access-to-nightly-builds.md). This should address the deficiencies discovered in our testing process following Akka.NET v1.4.1-RC1.

## Akka.NET v1.4 Migration Guide and Key Changes

### Performance Tuning Akka.Remote
Akka.Remote's performance has significantly increased as a function of our new batching mode ([see the numbers](../../articles/remoting/performance.md#no-io-batching)) - which is tunable via HOCON configuration to best support your needs. 

Akka.Remote's batching system _is enabled by default_ as of Akka.NET v1.4.0 and its defaults work well for systems under moderate load (100+ remote messages per second.) If your message volume is lower than that, you will _definitely need to performance tune Akka.Remote before you go into production with Akka.NET v1.4.0_.

[We have a detailed guide that explains how to performance tune Akka.Remote in Akka.NET here](../../articles/remoting/performance.md).

### Akka.Cluster: `allow-weakly-up-members = on` by Default
In Akka.NET 1.3.3 we introduce the notion of a `WeaklyUp` member status in Akka.Cluster - this allows nodes to join the cluster while other members of the cluster are currently marked as unreachable. By default, nodes can't join an Akka.NET cluster until all of the current member nodes are contacted by the joining node.

Since its introduction, the `WeaklyUp` membership status has been an opt-in feature - disabled by default.

Beginning in Akka.NET v1.4 `akka.cluster.allow-weakly-up-members` is now set to `on` by default. This will change some of the data you see coming from a tool like [Petabridge.Cmd's `cluster show` output](https://cmd.petabridge.com/articles/commands/cluster-commands.html#cluster-show). 

If you want to disable this feature, you can override the value in your application's HOCON.

### Akka.Cluster.Sharding: `akka.cluster.sharding.state-store-mode = ddata` Ready for Production
At the moment, Akka.Cluster.Sharding still defaults to what it did in Akka.NET v1.3.17 and earlier: it uses Akka.Persistence under the covers to store all of its shard allocation data. This means that in order to distribute entities across your cluster you need to have some sort of Akka.Persistence plugin configured and enabled inside your cluster.

Akka.NET v1.4 changes that - now that [Akka.DistributedData](../../articles/clustering/distributed-data.md) is out of beta we can change our Akka.Cluster.Sharding to use DistributedData to manage all of our shard allocation data instead.

```
akka.cluster{
	sharding{
		state-store-mode = ddata
	}
}
```

With DistributedData all of the shard allocation data is maintained in-memory using a CRDT (conflict free replicated data type) plus a replication system spread among the participating Akka.Cluster.Sharding nodes. Using this mode will make it easier to restart entire clusters that use Akka.Cluster.Sharding and will ultimately reduce the amount of I/O the sharding system needs to maintain its internal state. We highly encourage you to use it.

## Questions or Issues?
If you have any questions, concerns, or comments about Akka.NET v1.4.0 you can reach the project here:

1. [File a Github Issue for Akka.NET](https://github.com/akkadotnet/akka.net/issues/new);
2. [Message the contributors in Gitter](https://gitter.im/akkadotnet/akka.net);
3. [Contact Akka.NET on Twitter](https://twitter.com/akkadotnet); or
4. If your need is great or urgent, [contact Petabridge for Akka.NET Support](https://petabridge.com/services/consulting/)