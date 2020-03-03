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

### HOCON Migrations
Most users will only need to worry about the `Hocon` namespace change described below - most of the other changes affect plugin authors or developers who were using `Config` objects outside of what's built-in to Akka.NET itself.

#### Updating HOCON Namespaces from `Akka.Configuration` to `Hocon`
The biggest source of changes are going to be changing your HOCON namespaces, but thankfully these are localized to only the areas of your code where you accessed `Akka.Configuration.Config` objects explicitly. It's straightforward to update these references:

1. Change all `Akka.Configuration.Config` calls to `Hocon.Config` and
2. Add `using Hocon;` statements to wherever you work with HOCON directly.

If you run into any other areas where HOCON isn't 100% compatiable with what you've done in the past, [please report an issue on the Akka.NET repository](https://github.com/akkadotnet/HOCON/issues).

#### HOCON Validation Changes
The new HOCON parser introduced in the stand-alone HOCON library is much more strict and provides clearer error messages when invalid HOCON is parsed from its textual form.

For instance, in Akka.NET v1.3.17 the following HOCON parsed just fine:

```
akka{
 actor{
 	provider = remote
 }

 remote{
 	dot-netty.tcp.port = 8110
 	dot-netty.tcp.hostname = "localhost"
 }
# missing }
```

The HOCON parser in HOCON 2.0.3 and later will throw a `Hocon.HoconParserException` when attempting to parse this. So please make sure all of your curly braces are closed.

#### Parsing Empty HOCON Blocks
In Akka.NET v1.3.17 and earlier the following block of code would work just fine:

```csharp
// get an empty block of HOCON
// works in Akka.NET (-, 1.3.17] - fails starting in 1.4
Config empty = ConfigurationFactory.ParseString("");
```

Running that same code in Akka.NET v1.4 will throw an error:

```
Hocon.HoconParserException : Parameter text is null or empty.

If you want to create an empty Hocon HoconRoot, use "{}" instead.
```

To resolve this type of issue, you should do exactly what the error message suggests:

```csharp
// get an empty block of HOCON
// works in all versions of Akka.NET
Config empty = HoconConfigurationFactory.ParseString("{}");

// this will also work
Config empty = HoconConfigurationFactory.Empty;
```
#### HOCON Throws When Loading Missing Keys
In previous versions of Akka.NET the following HOCON:

```
akka{
	foo = "bar"
	baz = 2	
}
```

Would produce the following results when consumed via the `Config` class:

```
var akkaString = @"
	akka{
		foo = ""bar""
		baz = 2	
	}
";
var config = ConfigurationFactory.ParseString(akkaString);
Console.WriteLine(config.GetString("akka.foo")); // will print "bar" (correct)
Console.WriteLine(config.GetInt("akka.baz")); // will print "2" (correct)
Console.WriteLine(config.GetString("akka.biz")); // will print null, since key does not exist
Console.WriteLine(config.GetInt("akka.biz")); // will print default(int), since key does not exist
```

The previous HOCON parser erred on the side of no-throw, so it would simply provide a default value using `default(type)` when a user attempted to access a missing HOCON key. This resulted in, historically, a lot of very difficult to track down and find bugs.

In stand-alone HOCON, we opted to take things in a different approach with the ultimate goal of being able to develop HOCON lint-ing, validation, and Intellisense functionality in the future.

In Akka.NET v1.4 by default `HoconConfigurationFactory.ParseString` will throw an `Hocon.HoconValueException` if you attempt to read a HOCON key that is unpopulated:

```csharp
var akkaString = @"
	akka{
		foo = ""bar""
		baz = 2	
	}
";
var config = ConfigurationFactory.ParseString(akkaString);
Console.WriteLine(config.GetString("akka.foo")); // will print "bar" (correct)
Console.WriteLine(config.GetInt("akka.baz")); // will print "2" (correct)
Console.WriteLine(config.GetString("akka.biz")); // throws Hocon.HoconValueException
Console.WriteLine(config.GetInt("akka.biz")); // throws Hocon.HoconValueException
```

The interpretation is, that your HOCON doesn't have the content that you're looking for and therefore we should fail fast and alert the developer that the specified HOCON key couldn't be found in the current `Config` object _or any of its fallbacks_.

If this isn't what you need in your use case and you prefer the old behavior, that's easy to restore - provide an explicit default value in the second parameter on `Config.Get_(string hoconKey)` methods:

```csharp
var config = HoconConfigurationFactory.Empty; // empty config
Console.WriteLine(config.GetString("akka.foo", "empty")); // will print "empty" (default value)
Console.WriteLine(config.GetInt("akka.baz", 0)); // will print "0" (default value)
Console.WriteLine(config.GetString("akka.biz", string.Empty)); // will print string.Empty (default value)
Console.WriteLine(config.GetInt("akka.biz", 0)); // will print "0" (default value)
```

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