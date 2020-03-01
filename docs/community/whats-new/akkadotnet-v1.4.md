---
uid: akkadotnet-v14-migration-guide
title: What's new in Akka.NET v1.4.0?
---

# What's new in Akka.NET v1.4.0?

Akka.NET v1.4.0 is the culmination of many major architectural changes, improvements, bugfixes, and updates to the core Akka.NET runtime and its associated modules.

## Summary
The high-level summary of changes:

1. All HOCON `Config` objects have been moved from the `Akka.Configuration` namespace within the core `Akka` library to the stand-alone [HOCON project](https://github.com/akkadotnet/HOCON) and its subsequent [HOCON.Configuration NuGet package](https://www.nuget.org/packages/Hocon.Configuration/). See ["HOCON Syntax and Practices in Akka.NET"](../../articles/hocon/index.md) for a full guide on all of the different APIs, standards, and syntax supported. **This is a breaking change**, so [follow the Akka.NET v1.4.0 migration guide](#migration) below.
2. [Akka.Cluster.Sharding](../../articles/clustering/cluster-sharding.md) and [Akka.DistributedData](../../articles/clustering/distributed-data.md) are now out of beta status, have stable wire formats, and all sharding functionality is fully supported using either `akka.cluster.sharding.state-store-mode = "persistence"` or `akka.cluster.sharding.state-store-mode = "ddata"`.
3. Akka.Remote's performance has significantly increased as a function of our new batching mode ([see the numbers](../../articles/remoting/performance.md#no-io-batching)) - which is tunable via HOCON configuration to best support your needs. [Learn how to performance optimize Akka.Remote here](../../articles/remoting/performance.md).
4. Added a new module, [Akka.Cluster.Metrics](../../articles/cluster/cluster-metrics.md), which allows clustering users to receive performance data about each of the nodes in their cluster and even create some routers that can direct the flow of messages based on these metrics. 
5. Added ["Stream References" to Akka.Streams](../../articles/streams/streamrefs.md), a feature which allows Akka.Streams graphs to span across the network.
6. Moved from .NET Standard 1.6 to .NET Standard 2.0 and dropped support for all versions of .NET Framework that aren't supported by .NET Standard 2.0 - this means issues caused by our polyfills (i.e. `SerializableAttribute`) should be fully resolved now.
7. Moved onto modern C# best practices, such as changing our APIs to support `System.ValueTuple` over regular tuples.

If you want a detailed summary of changes and numerous bug fixes, [please see the Akka.NET v1.4.0 milestone, which has over 140 resolved issues attached to it](https://github.com/akkadotnet/akka.net/milestone/17).

### Stand-alone HOCON Library Features
The motivating factors for moving HOCON out of Akka core and into its own library were:

1. The allow for a greater amount of innovation / experimentation on HOCON, and in general, Akka.NET configuration management - HOCON is a _much_ smaller project than Akka.NET.
2. Allow for HOCON to be adopted in more places outside of Akka.NET itself.

In Akka.NET v1.4.0 we try to realize some of this value right away for Akka.NET users by automating away annoying tasks that resulted in large amount of boilerplate code.

Some examples:

#### Native Environment Variable Substitution
Akka.NET v1.4.0 should largely eliminate the need for custom libraries like [Akka.Bootstrap.Docker](https://github.com/petabridge/akkadotnet-bootstrap/tree/dev/src/Akka.Bootstrap.Docker), because environment substitution can now be handled natively via HOCON itself:

[!code-csharp[ConfigurationSample](../../../src/core/Akka.Docs.Tests/Configuration/ConfigurationSample.cs?name=EnvironmentVariableSample)]

#### Automatic HOCON Config File Reference Loading for .NET Core
In .NET Framework HOCON and Akka.NET still supports the classic `App.config` and `Web.config` sections, but until Akka.NET v1.4.0 we've never had a way of loading HOCON files by default.

At the time of release, Akka.NET v1.4.0 will look for the following HOCON files in the root of your Akka.NET application and load them automatically if any of them are detected:

1. [.NET Core / .NET Framework] An `app.conf` or an `app.hocon` file in the current working directory of the executable when it loads;
2. [.NET Framework] - the `<hocon>` `ConfigurationSection` inside `App.config` or `Web.config`; or
3. [.NET Framework] - and a legacy option, to load the old `<akka>` HOCON section for backwards compatibility purposes with all users who have been using HOCON with Akka.NET.

We [will likely expand this support to include more default HOCON sources in the future](https://github.com/akkadotnet/HOCON/blob/dev/README.md#default-hocon-configuration-sources).

Also, if you're running on .NET Core and want to programmatically load + parse HOCON, there's a convenient method for that too:

```csharp
Config c = HoconConfigurationFactory.FromFile("myHocon.conf");
```

## Migration
Akka.NET v1.4.0 introduces some breaking changes, although they're minor. Here's how to mitigate those and work around them.

### Updating HOCON API Calls
The biggest source of changes are going to be changing your HOCON namespaces, but thankfully these are localized to only the areas of your code where you accessed `Akka.Configuration.Config` objects explicitly. It's straightforward to update these references:

1. Change all `Akka.Configuration.Config` calls to `Hocon.Config` and
2. Add `using Hocon;` statements to wherever you work with HOCON directly.

If you run into any other areas where HOCON isn't 100% compatiable with what you've done in the past, [please report an issue on the Akka.NET repository](https://github.com/akkadotnet/HOCON/issues).

### Performance Tuning Akka.Remote
Akka.Remote's performance has significantly increased as a function of our new batching mode ([see the numbers](../../articles/remoting/performance.md#no-io-batching)) - which is tunable via HOCON configuration to best support your needs. 

Akka.Remote's batching system _is enabled by default_ as of Akka.NET v1.4.0 and its defaults work well for systems under moderate load (100+ remote messages per second.) If your message volume is lower than that, you will _definitely need to performance tune Akka.Remote before you go into production with Akka.NET v1.4.0_.

[We have a detailed guide that explains how to performance tune Akka.Remote in Akka.NET here](../../articles/remoting/performance.md).

## Questions or Issues?
If you have any questions, concerns, or comments about Akka.NET v1.4.0 you can reach the project here:

1. [File a Github Issue for Akka.NET](https://github.com/akkadotnet/akka.net/issues/new);
2. [Message the contributors in Gitter](https://gitter.im/akkadotnet/akka.net);
3. [Contact Akka.NET on Twitter](https://twitter.com/akkadotnet); or
4. If your need is great or urgent, [contact Petabridge for Akka.NET Support](https://petabridge.com/services/consulting/)