---
uid: akkadotnet-v15-whats-new
title: What's New in Akka.NET v1.5.0?
---

# What's New in Akka.NET v1.5?

<!-- markdownlint-disable MD033 -->
<iframe width="560" height="315" src="https://www.youtube.com/embed/-UPestlIw4k" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share" allowfullscreen></iframe>
<!-- markdownlint-enable MD033 -->

## Summary

Akka.NET v1.5 is a major new release of Akka.NET aimed at solving common pain points for existing users of the software. We've been working tirelessly at this release for over a year, with the help and feedback of many users and contributors, and we are quite pleased with the results.

A summary of our major changes include:

* **Introduction of [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting)** - a new library that eliminates the need for HOCON configuration; offers deep + seamless integration with Microsoft.Extensions.Logging / Hosting / DependencyInjection / and Configuration; makes dependency injection a first-class citizen in Akka.NET with the `ActorRegistry` and `IRequiredActor<T>` types; implements `ActorSystem` lifecycle management best practices automatically; and makes it much easier to standardize and scale Akka.NET development across large development teams.
* **Introduction of [Akka.Management](https://github.com/akkadotnet/Akka.Management)** - a new library that provides automatic [Akka.Cluster](xref:cluster-overview) bootstrapping and environment-specific service discovery capabilities. Akka.Management eliminates the need for things like Lighthouse - clusters can instead be formed by querying environments like [Kubernetes](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi), [Azure Table Storage](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/azure/Akka.Discovery.Azure), or [Amazon Web Services EC2/ECS](https://github.com/akkadotnet/Akka.Management/tree/dev/src/discovery/kubernetes/Akka.Discovery.KubernetesApi). This also enables Akka.Cluster to run in much lighter-weight PaaS enviroments such as [Akka.NET on Azure App Service](https://github.com/petabridge/azure-app-service-akkadotnet) or [Akka.NET on Azure Container Apps](https://github.com/petabridge/azure-container-app-akkadotnet).
* **Introduction of [Akka.HealthCheck](https://github.com/petabridge/akkadotnet-healthcheck)** - the Akka.HealthCheck library has actually been around for a few years, but it's been modernized to support Microsoft.Extensions.HealthCheck and includes automated liveness and readiness checks for Akka.Persistence and Akka.Cluster out of the box. It's also [very easy to write your own custom healthchecks](https://github.com/petabridge/akkadotnet-healthcheck#manually-setup-custom-akkanet-iprobeprovider-with-health-check-middleware).
* **.NET 6 Dual Targeting** - Akka.NET v1.5 now dual targets .NET Standard 2.0 (same as Akka.NET v1.4) _and_ .NET 6. We've done this in order to take advantage of newer .NET APIs for performance reasons. On .NET 6 Akka.NET in-memory messaging is now up to 50% faster as a result.
* **Akka.Cluster.Sharding Restructuring** - in Akka.NET v1.5 we've split Akka.Cluster.Sharding's `state-store-mode` into two parts: CoordinatorStore (`akka.cluster.sharding.state-store-mode`) and ShardStore (`akka.cluster.sharding.remember-entities-store`.) We've also added a new `remember-entities-store` mode: `eventsourced`. You should watch our "[Akka NET v1.5 New Features and Upgrade Guide (10:30)](https://youtu.be/-UPestlIw4k?t=630)" for a full summary of these changes, but the short version is that Akka.Cluster.Sharding is much more scalable, performant, and robust in Akka.NET v1.5.
* **Improved Logging Performance** - we made some breaking changes to the logging API (but they're still source-compatible) and the results are a 40% throughput improvement, 50%+ memory usage improvement for all of your user-defined actors. Watch our "[Akka NET v1.5 New Features and Upgrade Guide (7:30)](https://youtu.be/-UPestlIw4k?t=450)" for details.
* **Akka.Persistence.Query Backpressure Support** - this change is invisible from a coding and API standpoint, but from a behavioral standpoint it is significant. Akka.Persistence.Query can now run hundreds of thousands of parallel queries without torching your database instance. See "[
Scaling Akka.Persistence.Query to 100k+ Concurrent Queries for Large-Scale CQRS](https://petabridge.com/blog/largescale-cqrs-akkadotnet-v1.5/)" for details.
* **Fully Asynchronous TestKit APIs** - the entire [Akka.TestKit](xref:testing-actor-systems) now supports `async` methods for 100% of its capabilities. The old synchronous implementations still exist, but they're now built on top of the `async` ones. This should improve the developer experience for writing tests and reduce the completion time of your test suite.
* **Akka.Streams Improvements** - Akka.Streams now uses tremendously less memory than it did in Akka.NET v1.4 and many individual stream stages have been improved, made more robust, or extended to support new behaviors in Akka.NET v1.5.

We have many more features planned over the v1.5 lifecycle - such as:

* Continuing our work on making CQRS a first-class citizen with Akka.NET;
* Introducing better, easier to use methods for making actor-to-actor message delivery more reliable;
* Leveraging more .NET 6 APIs for improved performance;
* Retooling all of our examples and documentation to incorporate Akka.NET best practices, such as Akka.Hosting;
* Addressing structural issues with Akka.Remote, serialization, and more.

We appreciate the support of the Akka.NET community and its users in helping us deliver great software. Thank you!

### Upgrading Existing Applications to Akka.NET v1.5

Please see "[Akka.NET v1.5 Upgrade Advisories](xref:akkadotnet-v15-upgrade-advisories)" for upgrade instructions.

And in case you need help upgrading:

* [Akka.NET Discord](https://discord.gg/GSCfPwhbWP)
* [Akka.NET GitHub Discussions](https://github.com/akkadotnet/akka.net/discussions)
* [Akka.NET Commercial Support](https://petabridge.com/services/support/)

## Goals

<!-- markdownlint-disable MD033 -->
<iframe width="560" height="315" src="https://www.youtube.com/embed/pZN1ugrJtJU" title="YouTube video player" frameborder="0" allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture" allowfullscreen></iframe>
<!-- markdownlint-enable MD033 -->

Based on feedback from Akka.NET users, here are the top priorities:

1. **Better documentation and examples** - after simplifying the Akka.NET APIs around configuration and startup, modernizing all samples and deployment guidance is the biggest issue affecting end-users.
2. **Improved Akka.Remote + Cluster.Sharding performance** - we want to go from ~350,000 msg/s to 1m+ msg/s in our `RemotePingPong` benchmark by changing the design of Akka.Remote's actors and optionally, replacing our transports.
3. **Improved default serialization options** - Newtonsoft.Json sucks. Full stop.
4. **Improved Akka.Cluster formation experience** - in layman's terms this means [releasing Akka.Management out of beta](https://github.com/akkadotnet/Akka.Management/discussions/417) and incorporating it into the Akka.Cluster literature and code samples.
5. **Leveraging .NET 6+ primitives for improved performance** - thread execution, zero-allocation System.Memory data structures, and more.
6. **Make CQRS a priority in Akka.Persistence** - tags are an especially weak area for Akka.Persistence right now, given that they require table scans.
7. **Leverage Microsoft.Extensions more closely** - consume Microsoft.Extensions.Configuration from HOCON, allow Microsoft.Extensions.Logging interplay, and more.
8. **Cleanup** - there are a lot of old constructs and that can be removed from Akka.NET.

## How to Contribute

Contributing to the v1.5 effort is somewhat different than our [normal maintenance contributions to Akka.NET](xref:contributing-to-akkadotnet),in the following ways:

1. **Breaking binary compatibility changes are allowed so long as they are cost-justified** - contributors must be able to explain both the benefits and trade-offs involved in making this change in order for it to be accepted;
2. **Wire compatibility must include a viable upgrade path** - if we're to introduce changes to the wire format of Akka.NET, contributors must demonstrate and document a viable upgrade path for end-users; and
3. **Bigger bets are encouraged** - now is a good time to take some risks on the code base. So long as contributors can offer sound arguments for taking those bets (and this includes acknowledging and being realistic about trade-offs) then they should feel free to propose new changes that align with the 1.5 goals.

### Good Areas for Contribution

1. .NET 6 dual-targeting - there will be lots of areas where we can take advantage of new .NET 6 APIs for improved performance;
2. Performance optimization - performance optimization around Akka.Remote, Akka.Persistence, Akka.Cluster.Sharding, DData, and core Akka will be prioritized in this release and there are _many_ areas for improvement;
3. Akka.Hosting - there are lots of extension points and possibilities for making the APIs as expressive + concise as possible - we will need input from contributors to help make this possible;
4. API Cleanup - there are lots of utility classes or deprecated APIs that can be deleted from the code base with minimal impact. Less is more; and
5. New ideas and possibilities - this is a _great_ time to consider adding new features to Akka.NET. Be bold.

> [!IMPORTANT]
> Familiarize yourself with our [Akka.NET contribution guidelines](xref:contributing-to-akkadotnet) for best experience.
