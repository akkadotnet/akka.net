# Akka.NET

![Akka.NET logo](https://raw.githubusercontent.com/akkadotnet/akka.net/dev/docs/shfb/icons/AkkaNetLogo.Normal.png)

[![Akka.NET Discord server](https://img.shields.io/discord/974500337396375553?label=Discord)](https://discord.gg/GSCfPwhbWP)
[![NuGet](https://img.shields.io/nuget/v/Akka.svg?style=flat-square)](https://www.nuget.org/packages/Akka)
[![Nuget](https://img.shields.io/nuget/dt/Akka)](https://www.nuget.org/packages/Akka)


**[Akka.NET](https://getakka.net/)** is a .NET port of the popular [Akka project](https://akka.io/) from the Scala / Java community. We are an idiomatic [.NET implementation of the actor model](https://petabridge.com/blog/akkadotnet-what-is-an-actor/) built on top of the .NET Common Language Runtime.

* **Website**: [https://getakka.net/](https://getakka.net/)
* **Twitter** 🐦: [AkkaDotNet](https://twitter.com/AkkaDotNet)
* **Discussions** 📣: [Akka.NET GitHub Discussions](https://github.com/akkadotnet/akka.net/discussions)
* **Chat** 💬: [Akka.NET on Discord](https://discord.gg/GSCfPwhbWP)
* **StackOverflow** ✔️: [Akka.NET on StackOverflow](https://stackoverflow.com/questions/tagged/akka.net)

Akka.NET is a [.NET Foundation](https://dotnetfoundation.org/) project.

![.NET Foundation Logo](https://raw.githubusercontent.com/akkadotnet/akka.net/dev/docs/images/dotnetfoundationhorizontal.svg)

## How is Akka.NET Used?

Akka.NET can be used in-process or inside large, distributed real-time systems; we support a wide variety of use cases.

Akka.NET can be used to solve the following types of problems:

1. **Concurrency** - Akka.NET actors only process messages one-at-a-time and they do so in first in, first out (FIFO) order; this means that any application state internal to an actor is automatically thread-safe without having to use `lock`s or any other shared-memory synchronization mechanisms.
2. **Stream Processing** - Akka.NET actors and [Akka.Streams](https://getakka.net/articles/streams/introduction.html) make it easy to build streaming applications, used for processing incoming streams of data or incoming streams of live events such as UI or network events inside native applications.
3. **Event-Driven Programming** - actors make it easy to build event-driven applications, as actors' message-processing routines naturally express these types of designs.
4. **Event Sourcing and CQRS** - [Akka.Persistence](https://getakka.net/articles/persistence/architecture.html), used by actors to make their state re-entrant and recoverable across restarts or migrations between nodes, natively supports event sourcing. [Akka.Persistence.Query](https://getakka.net/articles/persistence/persistence-query.html) can be used to compute CQRS-style projections and materialized views from Akka.Persistence data.
5. **Location Transparency** - [Akka.Remote](https://getakka.net/articles/remoting/index.html) makes it simple for actors in remote processes to transparently communicate with each other.
6. **Highly Available, Fault-Tolerant Distributed Systems** - [Akka.Cluster](https://getakka.net/articles/clustering/cluster-overview.html), [Akka.Cluster.Sharding](https://getakka.net/articles/clustering/cluster-sharding.html), and other tools built on top of Akka.Cluster make it possible to build highly available and fault-tolerant distributed systems by leveraging peer-to-peer programming models with topology-aware message routing and distribution.
7. **Low Latency, High Throughput** - Akka.NET aims to be low latency and high throughput, processing 10s millions of messages per second in-memory and hundreds of thousands of messages per second over remote connections.

## Where Can I Learn Akka.NET?

You can start by taking the [Akka.NET Bootcamp](https://learnakka.net/), but there are many other great [learning resources for Akka.NET Online](https://getakka.net/community/online-resources.html).

* [Petabridge's Akka.NET Videos on YouTube](https://www.youtube.com/c/PetabridgeAcademy)
* "[.NET Conf - When and How to Use the Actor Model An Introduction to Akka.NET Actors](https://www.youtube.com/watch?v=0KnIMDoJpZs)"
* _[Reactive Applications with Akka.NET](https://www.manning.com/books/reactive-applications-with-akka-net)_
* _[Akka.NET Succinctly](https://www.syncfusion.com/succinctly-free-ebooks/akka-net-succinctly)_

## Build Status

| Stage                                | Status                                                                                                                                                                                                                                                             |
|------------------------------------- |------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Build                                | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=Windows%20Build)](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)                                    |
| NuGet Pack                           | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=NuGet%20Pack)](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)                                       |
| .NET Framework Unit Tests            | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%20Framework%20Unit%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)        |
| .NET 7 Unit Tests (Windows)          | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%207%20Unit%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)                |
| .NET 7 Unit Tests (Linux)            | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%207%20Unit%20Tests%20(Linux))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)                  |
| .NET 7 MultiNode Tests (Windows)     | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%207%20Multi-Node%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)          |
| .NET 7 MultiNode Tests (Linux)       | [![Build Status](https://dev.azure.com/dotnet/Akka.NET/_apis/build/status/akka.net/PR%20Validation?branchName=dev&jobName=.NET%207%20Multi-Node%20Tests%20(Windows))](https://dev.azure.com/dotnet/Akka.NET/_build/latest?definitionId=84&branchName=dev)          |                                                                                                                                                                                                                                                                   |
| Docs                                 | [![Build Status](https://dev.azure.com/petabridge/akkadotnet-tools/_apis/build/status/Akka.NET%20Docs?branchName=dev)](https://dev.azure.com/petabridge/akkadotnet-tools/_build/latest?definitionId=82&branchName=dev)                                             |


## Install Akka.NET via NuGet

If you want to include Akka.NET in your project, you can [install it directly from NuGet](https://www.nuget.org/packages/Akka)

To install Akka.NET Distributed Actor Framework, run the following command in the Package Manager Console

```
PM> Install-Package Akka.Hosting
```

> [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting) includes the base Akka NuGet package and also provides an easy interface to integrate Akka.NET with the most-used parts of the Microsoft.Extensions ecosystem: Configuration, Logging, Hosting, and DependencyInjection. We encourage developers to adopt it.

And if you need F# support:

```
PM> Install-Package Akka.FSharp
```

### Akka.NET Project Templates

To create your own Akka.NET projects using [our templates (Akka.Templates)](https://github.com/akkadotnet/akkadotnet-templates), install them via the `dotnet` CLI:

```
dotnet new install "Akka.Templates::*"
```

This will make our templates available via `dotnet new` on the CLI _and_ as new project templates inside any .NET IDE such as Visual Studio or JetBrains Rider. You can view the full list of templates included in our package here: https://github.com/akkadotnet/akkadotnet-templates#available-templates

## Builds
Please see [Building Akka.NET](http://getakka.net/community/building-akka-net.html).

To access nightly Akka.NET builds, please [see the instructions here](http://getakka.net/community/getting-access-to-nightly-builds.html).

## Support
If you need help getting started with Akka.NET, there's a number of great community resources online:

* Subscribe to the Akka.NET project feed on Twitter: https://twitter.com/AkkaDotNet  (@AkkaDotNet)
* Join the Akka.NET Discord: https://discord.gg/GSCfPwhbWP
* Ask Akka.NET questions on Stack Overflow: http://stackoverflow.com/questions/tagged/akka.net

If you and your company are interested in getting professional Akka.NET support, you can [contact Petabridge for dedicated Akka.NET support](https://petabridge.com/).
