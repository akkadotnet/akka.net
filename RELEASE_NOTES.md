#### 1.0.6 December 3 2015 ####

#### 1.0.5 December 3 2015 ####
**Maintenance release for Akka.NET v1.0.4**
This release is a collection of bug fixes, performance enhancements, and general improvements contributed by 29 individual contributors.

**Fixes & Changes - Akka.NET Core**
* [Bugfix: Make the Put on the SimpleDnsCache idempotent](https://github.com/akkadotnet/akka.net/commit/2ed1d574f76491707deac236db3fd7c1e5af5757)
* [Add CircuitBreaker Initial based on akka 2.0.5](https://github.com/akkadotnet/akka.net/commit/7e16834ef0ff8551cdd3530eacb1016d40cb1cb8)
* [Fix for receive timeout in async await actor](https://github.com/akkadotnet/akka.net/commit/6474bd7dc3d27756e255d12ef21f331108d9922d)
* [akka-io: fixed High CPU load using the Akka.IO TCP server](https://github.com/akkadotnet/akka.net/commit/4af2cfbcaafa33ea04a1a8b1aa6486e78bd6f821)
* [akka-io: Stop select loop on idle](https://github.com/akkadotnet/akka.net/commit/e545780d36cfb805b2014746a2e97006894c2e00)
* [Serialization fixes](https://github.com/akkadotnet/akka.net/commit/6385cc20a3d310efc0bb2f9e29710c5b7bceaa87)
* [Fix issue #1301 - inprecise resizing of resizable routers](https://github.com/akkadotnet/akka.net/commit/cf714333b25190249f01d79bad606d4ce5863e47)
* [Stashing now checks message equality by reference](https://github.com/akkadotnet/akka.net/commit/884330dfb5d69b523f25a59b98450322fe3b34f4)
* [rewrote ActorPath.TryParseAddrss to now test Uris by try / catch and use Uri.TryCreate instead](https://github.com/akkadotnet/akka.net/commit/8eaf32147a08f213db818bf19d74ed9d1aadaed2)
* [Port EventBusUnsubscribers](https://github.com/akkadotnet/akka.net/commit/bd91bcd50d918e5e8ee4b085e53d603cfd46c89a)
* [Add optional PipeTo completion handlers](https://github.com/akkadotnet/akka.net/commit/dfb7f61026d5d0b2d23efe1dd73af820f70a1d1c)
* [Akka context props output to Serilog](https://github.com/akkadotnet/akka.net/commit/409cd7f4ed0b285827b681685af59ec19c5a4b73)


**Fixes & Changes - Akka.Remote, Akka.Cluster**
* [MultiNode tests can now be skipped by specifying a SkipReason](https://github.com/akkadotnet/akka.net/commit/75f966cb7d2f2c0d859e0e3a90a38d251a10c5e5)
* [Akka.Remote: Discard msg if payload size > max allowed.](https://github.com/akkadotnet/akka.net/commit/05f57b9b1ff256145bc085f94d49a591e51e1304)
* [Throw `TypeLoadException` when an actor type or dependency cannot be found in a remote actor deploy scenario](https://github.com/akkadotnet/akka.net/commit/ffed3eb088bc00f90a5e4b7367d4598fda007401)
* [MultiNode Test Visualizer](https://github.com/akkadotnet/akka.net/commit/7706bb242719b7f7197058e89f8579af5b82dfc3)
* [Fix for Akka.Cluster.Routing.ClusterRouterGroupSettings Mono Linux issue](https://github.com/akkadotnet/akka.net/commit/dbbd2ac9b16772af8f8e35d3d1c8bf5dcf354f42)
* [Added RemoteDeploymentWatcher](https://github.com/akkadotnet/akka.net/commit/44c29ccefaeca0abdc4fd1f81daf1dc27a285f66)
* [Akka IO Transport: framing support](https://github.com/akkadotnet/akka.net/commit/60b5d2a318b485652e0888190aaa930fe43b1bbc)
* [#1443 fix for cluster shutdown](https://github.com/akkadotnet/akka.net/commit/941688aead57266b454b76530a7fb5446f68e15d)

**Fixes & Changes - Akka.Persistence**
* [Fixes the NullReferenceException in #1235 and appears to adhere to the practice of including an addres with the serialized binary.](https://github.com/akkadotnet/akka.net/commit/3df119ff614c3298299f863e18efd6e0fa848858)
* [Port Finite State Machine DSL to Akka.Persistence](https://github.com/akkadotnet/akka.net/commit/dce684d907df86f5039eb2ca20727ab48d4b218a)
* [Become and BecomeStacked for ReceivePersistentActor](https://github.com/akkadotnet/akka.net/commit/b11dafc86eb9284c2d515fd9da3599fe463a5681)
* [Persistent actor stops on recovery failures](https://github.com/akkadotnet/akka.net/commit/03105719a8866e8eadac268bc8f813e738f989b9)
* [Fixed: data races inside sql journal engine](https://github.com/akkadotnet/akka.net/commit/f088f0c681fdc7ba1b4eaf7f823c2a9535d3045d)
* [fix sqlite.conf and readme](https://github.com/akkadotnet/akka.net/commit/c7e925ba624eee7e386855251169aecbafd6ae7d)
* [#1416 created ReceiveActor implementation of AtLeastOnceDeliveryActor base class](https://github.com/akkadotnet/akka.net/commit/4d1d79b568bdae6565423c3ed914f8a9606dc0e8)

A special thanks to all of our contributors, organized below by the number of changes made:

23369 5258  18111 Aaron Stannard
18827 16329 2498  Bartosz Sypytkowski
11994 9496  2498  Steffen Forkmann
6031  4637  1394  maxim.salamatko
1987  1667  320 Graeme Bradbury
1556  1149  407 Sean Gilliam
1118  1118  0 moliver
706 370 336 rogeralsing
616 576 40  Marek Kadek
501 5 496 Alex Koshelev
377 269 108 Jeff Cyr
280 208 72  willieferguson
150 98  52  Christian Palmstierna
85  63  22  Willie Ferguson
77  71  6 Emil Ingerslev
66  61  5 Grover Jackson
60  39  21  Alexander Pantyukhin
56  33  23  Uladzimir Makarau
55  54  1 rdavisau
51  18  33  alex-kondrashov
42  26  16  Silv3rcircl3
36  30  6 evertmulder
33  19  14  Filip Malachowicz
13  11  2 Suhas Chatekar
7 6 1 tintoy
4 2 2 Jonathan
2 1 1 neekgreen
2 1 1 Christopher Martin
2 1 1 Artem Borzilov 

#### 1.0.4 August 07 2015 ####
**Maintenance release for Akka.NET v1.0.3**

**Akka.IO**
This release introduces some major new features to Akka.NET, including [Akka.IO - a new set of capabilities built directly into the Akka NuGet package that allow you to communicate with your actors directly via TCP and UDP sockets](http://getakka.net/docs/IO) from external (non-actor) systems.

If you want to see a really cool example of Akka.IO in action, look at [this sample that shows off how to use the Telnet commandline to interact directly with Akka.NET actors](https://github.com/akkadotnet/akka.net/blob/dev/src/examples/TcpEchoService.Server).

**Akka.Persistence.MongoDb** and **Akka.Persistence.Sqlite**
Two new flavors of Akka.Persistence support are now available. You can install them via the commandline!

```
PM> Install-Package Akka.Persistence.MongoDb -pre
```
and

```
PM> Install-Package Akka.Persistence.Sqlite -pre
```

**Fixes & Changes - Akka.NET Core**
* [F# API - problem with discriminated union serialization](https://github.com/akkadotnet/akka.net/issues/999)
* [Fix Null Sender issue](https://github.com/akkadotnet/akka.net/issues/1212)
* [Outdated FsPickler version in Akka.FSharp can lead to runtime errors when other FsPickler-dependent packages are installed](https://github.com/akkadotnet/akka.net/issues/1206)
* [Add default Ask timeout to HOCON configuration](https://github.com/akkadotnet/akka.net/issues/1163)
* [Remote watching is repeated](https://github.com/akkadotnet/akka.net/issues/1090)
* [RemoteDaemon bug, not removing children ](https://github.com/akkadotnet/akka.net/pull/1068)
* [HOCON include support](https://github.com/akkadotnet/akka.net/pull/1169)
* [Replace SystemNanoTime with MonotonicClock](https://github.com/akkadotnet/akka.net/pull/1174)

**Akka.DI.StructureMap**
We now have support for the [StructureMap dependency injection framework](http://docs.structuremap.net/) out of the box. You can install it here!

```
PM> Install-Package Akka.DI.StructureMap
```

#### 1.0.3 June 12 2015 ####
**Bugfix release for Akka.NET v1.0.2.**

This release addresses an issue with Akka.Persistence.SqlServer and Akka.Persistence.PostgreSql where both packages were missing a reference to Akka.Persistence.Sql.Common. 

In Akka.NET v1.0.3 we've packaged Akka.Persistence.Sql.Common into its own NuGet package and referenced it in the affected packages.

#### 1.0.2 June 2 2015
**Bugfix release for Akka.NET v1.0.1.**

Fixes & Changes - Akka.NET Core
* [Routers seem ignore supervision strategy](https://github.com/akkadotnet/akka.net/issues/996)
* [Replaced DateTime.Now with DateTime.UtcNow/MonotonicClock](https://github.com/akkadotnet/akka.net/pull/1009)
* [DedicatedThreadScheduler](https://github.com/akkadotnet/akka.net/pull/1002)
* [Add ability to specify scheduler implementation in configuration](https://github.com/akkadotnet/akka.net/pull/994)
* [Added generic extensions to EventStream subscribe/unsubscribe.](https://github.com/akkadotnet/akka.net/pull/990)
* [Convert null to NoSender.](https://github.com/akkadotnet/akka.net/pull/993)
* [Supervisor strategy bad timeouts](https://github.com/akkadotnet/akka.net/pull/986)
* [Updated Pigeon.conf throughput values](https://github.com/akkadotnet/akka.net/pull/980)
* [Add PipeTo for non-generic Tasks for exception handling](https://github.com/akkadotnet/akka.net/pull/978)

Fixes & Changes - Akka.NET Dependency Injection
* [Added Extensions methods to ActorSystem and ActorContext to make DI more accessible](https://github.com/akkadotnet/akka.net/pull/966)
* [DIActorProducer fixes](https://github.com/akkadotnet/akka.net/pull/961)
* [closes akkadotnet/akka.net#1020 structuremap dependency injection](https://github.com/akkadotnet/akka.net/pull/1021)

Fixes & Changes - Akka.Remote and Akka.Cluster
* [Fixing up cluster rejoin behavior](https://github.com/akkadotnet/akka.net/pull/962)
* [Added dispatcher fixes for remote and cluster ](https://github.com/akkadotnet/akka.net/pull/983)
* [Fixes to ClusterRouterGroup](https://github.com/akkadotnet/akka.net/pull/953)
* [Two actors are created by remote deploy using Props.WithDeploy](https://github.com/akkadotnet/akka.net/issues/1025)

Fixes & Changes - Akka.Persistence
* [Renamed GuaranteedDelivery classes to AtLeastOnceDelivery](https://github.com/akkadotnet/akka.net/pull/984)
* [Changes in Akka.Persistence SQL backend](https://github.com/akkadotnet/akka.net/pull/963)
* [PostgreSQL persistence plugin for both event journal and snapshot store](https://github.com/akkadotnet/akka.net/pull/971)
* [Cassandra persistence plugin](https://github.com/akkadotnet/akka.net/pull/995)

**New Features:**

**Akka.TestKit.XUnit2**
Akka.NET now has support for [XUnit 2.0](http://xunit.github.io/)! You can install Akka.TestKit.XUnit2 via the NuGet commandline:

```
PM> Install-Package Akka.TestKit.XUnit2
```

**Akka.Persistence.PostgreSql** and **Akka.Persistence.Cassandra**
Akka.Persistence now has two additional concrete implementations for PostgreSQL and Cassandra! You can install either of the packages using the following commandline:

[Akka.Persistence.PostgreSql Configuration Docs](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/persistence/Akka.Persistence.PostgreSql)
```
PM> Install-Package Akka.Persistence.PostgreSql
```

[Akka.Persistence.Cassandra Configuration Docs](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/persistence/Akka.Persistence.Cassandra)
```
PM> Install-Package Akka.Persistence.Cassandra
```

**Akka.DI.StructureMap**
Akka.NET's dependency injection system now supports [StructureMap](http://structuremap.github.io/)! You can install Akka.DI.StructureMap via the NuGet commandline:

```
PM> Install-Package Akka.DI.StructureMap
```

#### 1.0.1 Apr 28 2015

**Bugfix release for Akka.NET v1.0.**

Fixes:
* [v1.0 F# scheduling API not sending any scheduled messages](https://github.com/akkadotnet/akka.net/issues/831)
* [PinnedDispatcher - uses single thread for all actors instead of creating persanal thread for every actor](https://github.com/akkadotnet/akka.net/issues/850)
* [Hotfix async await when awaiting IO completion port based tasks](https://github.com/akkadotnet/akka.net/pull/843)
* [Fix for async await suspend-resume mechanics](https://github.com/akkadotnet/akka.net/pull/836)
* [Nested Ask async await causes null-pointer exception in ActorTaskScheduler](https://github.com/akkadotnet/akka.net/issues/855)
* [Akka.Remote: can't reply back remotely to child of Pool router](https://github.com/akkadotnet/akka.net/issues/884)
* [Context.DI().ActorOf shouldn't require a parameterless constructor](https://github.com/akkadotnet/akka.net/issues/832)
* [DIActorContextAdapter uses typeof().Name instead of AssemblyQualifiedName](https://github.com/akkadotnet/akka.net/issues/833)
* [IndexOutOfRangeException with RoundRobinRoutingLogic & SmallestMailboxRoutingLogic](https://github.com/akkadotnet/akka.net/issues/908)

New Features:

**Akka.TestKit.NUnit**
Akka.NET now has support for [NUnit ](http://nunit.org/) inside its TestKit. You can install Akka.TestKit.NUnit via the NuGet commandline:

```
PM> Install-Package Akka.TestKit.NUnit
```

**Akka.Persistence.SqlServer**
The first full implementation of Akka.Persistence is now available for SQL Server.

[Read the full instructions for working with Akka.Persistence.SQLServer here](https://github.com/akkadotnet/akka.net/tree/dev/src/contrib/persistence/Akka.Persistence.SqlServer).

#### 1.0.0 Apr 09 2015

**Akka.NET is officially no longer in beta status**. The APIs introduced in Akka.NET v1.0 will enjoy long-term support from the Akka.NET development team and all of its professional support partners.

Many breaking changes were introduced between v0.8 and v1.0 in order to provide better future extensibility and flexibility for Akka.NET, and we will outline the major changes in detail in these release notes.

However, if you want full API documentation we recommend going to the following:

* **[Latest Stable Akka.NET API Docs](http://api.getakka.net/docs/stable/index.html "Akka.NET Latest Stable API Docs")**
* **[Akka.NET Wiki](http://getakka.net/wiki/ "Akka.NET Wiki")**

----

**Updated Packages with 1.0 Stable Release**

All of the following NuGet packages have been upgraded to 1.0 for stable release:

- Akka.NET Core
- Akka.FSharp
- Akka.Remote
- Akka.TestKit
- Akka.DI (dependency injection)
- Akka.Loggers (logging)

The following packages (and modules dependent on them) are still in *pre-release* status:

- Akka.Cluster
- Akka.Persistence

----
**Introducing Full Mono Support for Akka.NET**

One of the biggest changes in Akka.NET v1.0 is the introduction of full Mono support across all modules; we even have [Raspberry PI machines talking to laptops over Akka.Remote](https://twitter.com/AkkaDotNET/status/584109606714093568)!

We've tested everything using Mono v3.12.1 across OS X and Ubuntu. 

**[Please let us know how well Akka.NET + Mono runs on your environment](https://github.com/akkadotnet/akka.net/issues/694)!**

----

**API Changes in v1.0**

**All methods returning an `ActorRef` now return `IActorRef`**
This is the most significant breaking change introduced in AKka.NET v1.0. Rather than returning the `ActorRef` abstract base class from all of the `ActorOf`, `Sender` and other methods we now return an instance of the `IActorRef` interface instead.

This was done in order to guarantee greater future extensibility without additional breaking changes, so we decided to pay off that technical debt now that we're supporting these APIs long-term.

Here's the set of breaking changes you need to be aware of:

- Renamed:
  - `ActorRef`          --> `IActorRef`
  - `ActorRef.Nobody`   --> `ActorRefs.Nobody`
  - `ActorRef.NoSender` --> `ActorRefs.NoSender`
- `ActorRef`'s  operators `==` and `!=` has been removed. This means all expressions like `actorRef1 == actorRef2` must be replaced with `Equals(actorRef1, actorRef2)`
- `Tell(object message)`, i.e. the implicit sender overload, has been moved
to an extension method, and requires `using Akka.Actor;` to be accessible.
- Implicit cast from `ActorRef` to `Routee` has been replaced with `Routee.FromActorRef(actorRef)`

**`async` / `await` Support**

`ReceiveActor`s now support Async/Await out of the box.

```csharp
public class MyActor : ReceiveActor
{
       public MyActor()
       {
             Receive<SomeMessage>(async some => {
                    //we can now safely use await inside this receive handler
                    await SomeAsyncIO(some.Data);
                    Sender.Tell(new EverythingIsAllOK());                   
             });
       }
}
```

It is also possible to specify the behavior for the async handler, using `AsyncBehavior.Suspend` and  `AsyncBehavior.Reentrant` as the first argument.
When using `Suspend` the normal actor semantics will be preserved, the actor will not be able to process any new messages until the current async operation is completed.
While using `Reentrant` will allow the actor to multiplex messages during the `await` period.
This does not mean that messages are processed in parallel, we still stay true to "one message at a time", but each await continuation will be piped back to the actor as a message and continue under the actors concurrency constraint.

However, `PipeTo` pattern is still the preferred way to perform async operations inside an actor, as it is more explicit and clearly states what is going on.


**Switchable Behaviors**
In order to make the switchable behavior APIs more understandable for both `UntypedActor` and `ReceiveActor` we've updated the methods to the following:

``` C#
Become(newHandler); // become newHandler, without adding previous behavior to the stack (default)
BecomeStacked(newHandler); // become newHandler, without adding previous behavior to the stack (default)
UnbecomeStacked(); //revert to the previous behavior in the stack
```

The underlying behavior-switching implementation hasn't changed at all - only the names of the methods.

**Scheduler APIs**
The `Context.System.Scheduler` API has been overhauled to be both more extensible and understandable going forward. All of the previous capabilities for the `Scheduler` are still available, only in different packaging than they were before.

Here are the new APIs:

``` C#
Context.System.Scheduler
  .ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, ActorRef sender);
  .ScheduleTellOnce(TimeSpan delay, ICanTell receiver, object message, ActorRef sender, ICancelable cancelable);
  .ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, ActorRef sender);
  .ScheduleTellRepeatedly(TimeSpan initialDelay, TimeSpan interval, ICanTell receiver, object message, ActorRef sender, ICancelable cancelable);

Context.System.Scheduler.Advanced
  .ScheduleOnce(TimeSpan delay, Action action);
  .ScheduleOnce(TimeSpan delay, Action action, ICancelable cancelable);
  .ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action);
  .ScheduleRepeatedly(TimeSpan initialDelay, TimeSpan interval, Action action, ICancelable cancelable);
```

There's also a set of extension methods for specifying delays and intervals in milliseconds as well as methods for all four variants (`ScheduleTellOnceCancelable`, `ScheduleTellRepeatedlyCancelable`, `ScheduleOnceCancelable`, `ScheduleRepeatedlyCancelable`) that creates a cancelable, schedules, and returns the cancelable. 

**Akka.NET `Config` now loaded automatically from App.config and Web.config**
In previous versions Akka.NET users had to do the following to load Akka.NET HOCON configuration sections from App.config or Web.config:

```csharp
var section = (AkkaConfigurationSection)ConfigurationManager.GetSection("akka");
var config = section.AkkaConfig;
var actorSystem = ActorSystem.Create("MySystem", config);
```

As of Akka.NET v1.0 this is now done for you automatically:

```csharp
var actorSystem = ActorSystem.Create("MySystem"); //automatically loads App/Web.config, if any
```

**Dispatchers**
Akka.NET v1.0 introduces the `ForkJoinDispatcher` as well as general purpose dispatcher re-use.

**Using ForkJoinDispatcher**
ForkJoinDispatcher is special - it uses [`Helios.Concurrency.DedicatedThreadPool`](https://github.com/helios-io/DedicatedThreadPool) to create a dedicated set of threads for the exclusive use of the actors configured to use a particular `ForkJoinDispatcher` instance. All of the remoting actors depend on the `default-remote-dispatcher` for instance.

Here's how you can create your own ForkJoinDispatcher instances via Config:

```
myapp{
  my-forkjoin-dispatcher{
    type = ForkJoinDispatcher
    throughput = 100
    dedicated-thread-pool{ #settings for Helios.DedicatedThreadPool
      thread-count = 3 #number of threads
      #deadlock-timeout = 3s #optional timeout for deadlock detection
      threadtype = background #values can be "background" or "foreground"
    }
  }
}
}
```

You can then use this specific `ForkJoinDispatcher` instance by configuring specific actors to use it, whether it's via config or the fluent interface on `Props`:

**Config**
```
akka.actor.deploy{
     /myActor1{
       dispatcher = myapp.my-forkjoin-dispatcher
     }
}
```

**Props**
```csharp
var actor = Sys.ActorOf(Props.Create<Foo>().WithDispatcher("myapp.my-forkjoin-dispatcher"));
```

**FluentConfiguration [REMOVED]**
`FluentConfig` has been removed as we've decided to standardize on HOCON configuration, but if you still want to use the old FluentConfig bits you can find them here: https://github.com/rogeralsing/Akka.FluentConfig

**F# API**
The F# API has changed to reflect the other C# interface changes, as well as unique additions specific to F#.

In addition to updating the F# API, we've also fixed a long-standing bug with being able to serialize discriminated unions over the wire. This has been resolved.

**Interface Renames**
In order to comply with .NET naming conventions and standards, all of the following interfaces have been renamed with the `I{InterfaceName}` prefix.

The following interfaces have all been renamed to include the `I` prefix:

- [X] `Akka.Actor.ActorRefProvider, Akka` (Public)
- [X] `Akka.Actor.ActorRefScope, Akka` (Public)
- [X] `Akka.Actor.AutoReceivedMessage, Akka` (Public)
- [X] `Akka.Actor.Cell, Akka` (Public)
- [X] `Akka.Actor.Inboxable, Akka` (Public)
- [X] `Akka.Actor.IndirectActorProducer, Akka` (Public)
- [X] `Akka.Actor.Internal.ChildrenContainer, Akka` (Public)
- [X] `Akka.Actor.Internal.ChildStats, Akka` (Public)
- [X] `Akka.Actor.Internal.InternalSupportsTestFSMRef`2, Akka` (Public)
- [X] `Akka.Actor.Internal.SuspendReason+WaitingForChildren, Akka`
- [X] `Akka.Actor.Internals.InitializableActor, Akka` (Public)
- [X] `Akka.Actor.LocalRef, Akka`
- [X] `Akka.Actor.LoggingFSM, Akka` (Public)
- [X] `Akka.Actor.NoSerializationVerificationNeeded, Akka` (Public)
- [X] `Akka.Actor.PossiblyHarmful, Akka` (Public)
- [X] `Akka.Actor.RepointableRef, Akka` (Public)
- [X] `Akka.Actor.WithBoundedStash, Akka` (Public)
- [X] `Akka.Actor.WithUnboundedStash, Akka` (Public)
- [X] `Akka.Dispatch.BlockingMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.BoundedDequeBasedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.BoundedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.DequeBasedMailbox, Akka` (Public)
- [X] `Akka.Dispatch.DequeBasedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.MessageQueues.MessageQueue, Akka` (Public)
- [X] `Akka.Dispatch.MultipleConsumerSemantics, Akka` (Public)
- [X] `Akka.Dispatch.RequiresMessageQueue`1, Akka` (Public)
- [X] `Akka.Dispatch.Semantics, Akka` (Public)
- [X] `Akka.Dispatch.SysMsg.SystemMessage, Akka` (Public)
- [X] `Akka.Dispatch.UnboundedDequeBasedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Dispatch.UnboundedMessageQueueSemantics, Akka` (Public)
- [X] `Akka.Event.LoggingAdapter, Akka` (Public)
- [X] `Akka.FluentConfigInternals, Akka` (Public)
- [X] `Akka.Remote.InboundMessageDispatcher, Akka.Remote`
- [X] `Akka.Remote.RemoteRef, Akka.Remote`
- [X] `Akka.Routing.ConsistentHashable, Akka` (Public)

**`ConsistentHashRouter` and `IConsistentHashable`**
Akka.NET v1.0 introduces the idea of virtual nodes to the `ConsistentHashRouter`, which are designed to provide more even distributions of hash ranges across a relatively small number of routees. You can take advantage of virtual nodes via configuration:

```xml
akka.actor.deployment {
	/router1 {
		router = consistent-hashing-pool
		nr-of-instances = 3
		virtual-nodes-factor = 17
	}
}
```

Or via code:

```csharp
var router4 = Sys.ActorOf(Props.Empty.WithRouter(
	new ConsistentHashingGroup(new[]{c},hashMapping: hashMapping)
	.WithVirtualNodesFactor(5)), 
	"router4");
```

**`ConsistentHashMapping` Delegate**
There are three ways to instruct a router to hash a message:
1. Wrap the message in a `ConsistentHashableEnvelope`;
2. Implement the `IConsistentHashable` interface on your message types; or
3. Or, write a `ConsistentHashMapper` delegate and pass it to a `ConsistentHashingGroup` or a `ConsistentHashingPool` programmatically at create time.

Here's an example, taken from the `ConsistentHashSpecs`:

```csharp
ConsistentHashMapping hashMapping = msg =>
{
    if (msg is Msg2)
    {
        var m2 = msg as Msg2;
        return m2.Key;
    }

    return null;
};
var router2 =
    Sys.ActorOf(new ConsistentHashingPool(1, null, null, null, hashMapping: hashMapping)
    .Props(Props.Create<Echo>()), "router2");
```

Alternatively, you don't have to pass the `ConsistentHashMapping` into the constructor - you can use the `WithHashMapping` fluent interface built on top of both `ConsistentHashingGroup` and `ConsistentHashingPool`:

```csharp
var router2 =
    Sys.ActorOf(new ConsistentHashingPool(1).WithHashMapping(hashMapping)
    .Props(Props.Create<Echo>()), "router2");
```

**`ConsistentHashable` renamed to `IConsistentHashable`**
Any objects you may have decorated with the `ConsistentHashable` interface to work with `ConsistentHashRouter` instances will need to implement `IConsistentHashable` going forward, as all interfaces have been renamed with the `I-` prefix per .NET naming conventions.

**Akka.DI.Unity NuGet Package**
Akka.NET now ships with dependency injection support for [Unity](http://unity.codeplex.com/).

You can install our Unity package via the following command in the NuGet package manager console:

```
PM> Install-Package Akka.DI.Unity
```

----

#### 0.8.0 Feb 11 2015

__Dependency Injection support for Ninject, Castle Windsor, and AutoFac__. Thanks to some amazing effort from individual contributor (**[@jcwrequests](https://github.com/jcwrequests "@jcwrequests")**), Akka.NET now has direct dependency injection support for [Ninject](http://www.ninject.org/), [Castle Windsor](http://docs.castleproject.org/Default.aspx?Page=MainPage&NS=Windsor&AspxAutoDetectCookieSupport=1), and [AutoFac](https://github.com/autofac/Autofac).

Here's an example using Ninject, for instance:

    // Create and build your container 
    var container = new Ninject.StandardKernel(); 
	container.Bind().To(typeof(TypedWorker)); 
	container.Bind().To(typeof(WorkerService));
    
    // Create the ActorSystem and Dependency Resolver 
	var system = ActorSystem.Create("MySystem"); 
	var propsResolver = new NinjectDependencyResolver(container,system);

	//Create some actors who need Ninject
	var worker1 = system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker1");
	var worker2 = system.ActorOf(propsResolver.Create<TypedWorker>(), "Worker2");

	//send them messages
	worker1.Tell("hi!");

You can install these DI plugins for Akka.NET via NuGet - here's how:

* **Ninject** - `install-package Akka.DI.Ninject`
* **Castle Windsor** - `install-package Akka.DI.CastleWindsor`
* **AutoFac** - `install-package Akka.DI.AutoFac`

**Read the [full Dependency Injection with Akka.NET documentation](http://getakka.net/wiki/Dependency%20injection "Dependency Injection with Akka.NET") here.**

__Persistent Actors with Akka.Persistence (Alpha)__. Core contributor **[@Horusiath](https://github.com/Horusiath)** ported the majority of Akka's Akka.Persistence and Akka.Persistence.TestKit modules. 

> Even in the core Akka project these modules are considered to be "experimental," but the goal is to provide actors with a way of automatically saving and recovering their internal state to a configurable durable store - such as a database or filesystem.

Akka.Persistence also introduces the notion of *reliable delivery* of messages, achieved through the `GuaranteedDeliveryActor`.

Akka.Persistence also ships with an FSharp API out of the box, so while this package is in beta you can start playing with it either F# or C# from day one.

If you want to play with Akka.Persistence, please install any one of the following packages:

* **Akka.Persistence** - `install-package Akka.Persistence -pre`
* **Akka.Persistence.FSharp** - `install-package Akka.Persistence.FSharp -pre`
* **Akka.Persistence.TestKit** - `install-package Akka.Persistence.TestKit -pre`

**Read the [full Persistent Actors with Akka.NET documentation](http://getakka.net/wiki/Persistence "Persistent Actors with Akka.NET") here.**

__Remote Deployment of Routers and Routees__. You can now remotely deploy routers and routees via configuration, like so:

**Deploying _routees_ remotely via `Config`**:

	actor.deployment {
	    /blub {
	      router = round-robin-pool
	      nr-of-instances = 2
	      target.nodes = [""akka.tcp://${sysName}@localhost:${port}""]
	    }
	}

	var router = masterActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>()), "blub");

When deploying a router via configuration, just specify the `target.nodes` property with a list of `Address` instances for each node you want to deploy your routees.

> NOTE: Remote deployment of routees only works for `Pool` routers.

**Deploying _routers_ remotely via `Config`**:

	actor.deployment {
	    /blub {
	      router = round-robin-pool
	      nr-of-instances = 2
	      remote = ""akka.tcp://${sysName}@localhost:${port}""
	    }
	}

	var router = masterActorSystem.ActorOf(Props.Create<Echo>().WithRouter(FromConfig.Instance), "blub");

Works just like remote deployment of actors.

If you want to deploy a router remotely via explicit configuration, you can do it in code like this via the `RemoteScope` and `RemoteRouterConfig`:

**Deploying _routees_ remotely via explicit configuration**:

    var intendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@localhost:${port}"
    .Replace("${sysName}", sysName)
    .Replace("${port}", port.ToString()));
    
     var router = myActorSystem.ActorOf(new RoundRobinPool(2).Props(Props.Create<Echo>())
    .WithDeploy(new Deploy(
		new RemoteScope(intendedRemoteAddress.Copy()))), "myRemoteRouter");

**Deploying _routers_ remotely via explicit configuration**:

    var intendedRemoteAddress = Address.Parse("akka.tcp://${sysName}@localhost:${port}"
    .Replace("${sysName}", sysName)
    .Replace("${port}", port.ToString()));
    
     var router = myActorSystem.ActorOf(
		new RemoteRouterConfig(
		new RoundRobinPool(2), new[] { new Address("akka.tcp", sysName, "localhost", port) })
        .Props(Props.Create<Echo>()), "blub2");

**Improved Serialization and Remote Deployment Support**. All internals related to serialization and remote deployment have undergone vast improvements in order to support the other work that went into this release.

**Pluggable Actor Creation Pipeline**. We reworked the plumbing that's used to provide automatic `Stash` support and exposed it as a pluggable actor creation pipeline for local actors.

This release adds the `ActorProducerPipeline`, which is accessible from `ExtendedActorSystem` (to be able to configure by plugins) and allows you to inject custom hooks satisfying following interface:


    interface IActorProducerPlugin {
	    bool CanBeAppliedTo(ActorBase actor);
	    void AfterActorCreated(ActorBase actor, IActorContext context);
	    void BeforeActorTerminated(ActorBase actor, IActorContext context);
    }

- **CanBeAppliedTo** determines if plugin can be applied to specific actor instance.
- **AfterActorCreated** is applied to actor after it has been instantiated by an `ActorCell` and before `InitializableActor.Init` method will (optionally) be invoked.
- **BeforeActorTerminated** is applied before actor terminates and before `IDisposable.Dispose` method will be invoked (for disposable actors) - **auto handling disposable actors is second feature of this commit**.

For common use it's better to create custom classes inheriting from `ActorProducerPluginBase` and `ActorProducerPluginBase<TActor>` classes.

Pipeline itself provides following interface:

    class ActorProducerPipeline : IEnumerable<IActorProducerPlugin> {
	    int Count { get; } // current plugins count - 1 by default (ActorStashPlugin)
	    bool Register(IActorProducerPlugin plugin)
	    bool Unregister(IActorProducerPlugin plugin)
	    bool IsRegistered(IActorProducerPlugin plugin)
	    bool Insert(int index, IActorProducerPlugin plugin)
    }

- **Register** - registers a plugin if no other plugin of the same type has been registered already (plugins with generic types are counted separately). Returns true if plugin has been registered.
- **Insert** - same as register, but plugin will be placed in specific place inside the pipeline - useful if any plugins precedence is required.
- **Unregister** - unregisters specified plugin if it has been found. Returns true if plugin was found and unregistered.
- **IsRegistered** - checks if plugin has been already registered.

By default pipeline is filled with one already used plugin - `ActorStashPlugin`, which replaces stash initialization/unstashing mechanism used up to this moment.

**MultiNodeTestRunner and Akka.Remote.TestKit**. The MultiNodeTestRunner and the Multi Node TestKit (Akka.Remote.TestKit) underwent some drastic changes in this update. They're still not quite ready for public use yet, but if you want to see what the experience is like you can [clone the Akka.NET Github repository](https://github.com/akkadotnet/akka.net) and run the following command:

````
C:\akkadotnet> .\build.cmd MultiNodeTests
````

This will automatically launch all `MultiNodeSpec` instances found inside `Akka.Cluster.Tests`. We'll need to make this more flexible to be able to run other assemblies that require multinode tests in the future.

These tests are not enabled by default in normal build runs, but they will at some point in the future.

Here's a sample of the output from the console, to give you a sense of what the reporting looks like:

![image](https://cloud.githubusercontent.com/assets/326939/6075685/5f7c56b2-ad8c-11e4-9d93-8216a8cbabaf.png)

The MultiNodeTestRunner uses XUnit internally and will dynamically deploy as many processes are needed to satisfy any individual test. Has been tested with up to 6 processes.


#### 0.7.1 Dec 13 2014
__Brand New F# API__. The entire F# API has been updated to give it a more native F# feel while still holding true to the Erlang / Scala conventions used in actor systems. [Read more about the F# API changes](https://github.com/akkadotnet/akka.net/pull/526).

__Multi-Node TestKit (Alpha)__. Not available yet as a NuGet package, but the first pass at the Akka.Remote.TestKit is now available from source, which allow you to test your actor systems running on multiple machines or processes.

A multi-node test looks like this

    public class InitialHeartbeatMultiNode1 : InitialHeartbeatSpec
    {
    }
    
    public class InitialHeartbeatMultiNode2 : InitialHeartbeatSpec
    {
    }
    
    public class InitialHeartbeatMultiNode3 : InitialHeartbeatSpec
    {
    }
    
    public abstract class InitialHeartbeatSpec : MultiNodeClusterSpec
The MultiNodeTestRunner looks at this, works out that it needs to create 3 processes to run 3 nodes for the test.
It executes NodeTestRunner in each process to do this passing parameters on the command line. [Read more about the multi-node testkit here](https://github.com/akkadotnet/akka.net/pull/497).

__Breaking Change to the internal api: The `Next` property on `IAtomicCounter<T>` has been changed into the function `Next()`__ This was done as it had side effects, i.e. the value was increased when the getter was called. This makes it very hard to debug as the debugger kept calling the property and causing the value to be increased.

__Akka.Serilog__ `SerilogLogMessageFormatter` has been moved to the namespace `Akka.Logger.Serilog` (it used to be in `Akka.Serilog.Event.Serilog`).
Update your `using` statements from `using Akka.Serilog.Event.Serilog;` to `using Akka.Logger.Serilog;`.

__Breaking Change to the internal api: Changed signatures in the abstract class `SupervisorStrategy`__. The following methods has new signatures: `HandleFailure`, `ProcessFailure`. If you've inherited from `SupervisorStrategy`, `OneForOneStrategy` or `AllForOneStrategy` and overridden the aforementioned methods you need to update their signatures.

__TestProbe can be implicitly casted to ActorRef__. New feature. Tests requiring the `ActorRef` of a `TestProbe` can now be simplified:
``` C#
var probe = CreateTestProbe();
var sut = ActorOf<GreeterActor>();
sut.Tell("Akka", probe); // previously probe.Ref was required
probe.ExpectMsg("Hi Akka!");
```

__Bugfix for ConsistentHashableEvenlope__. When using `ConsistentHashableEvenlope` in conjunction with `ConsistentHashRouter`s, `ConsistentHashableEvenlope` now correctly extracts its inner message instead of sending the entire `ConsistentHashableEvenlope` directly to the intended routee.

__Akka.Cluster group routers now work as expected__. New update of Akka.Cluster - group routers now work as expected on cluster deployments. Still working on pool routers. [Read more about Akka.Cluster routers here](https://github.com/akkadotnet/akka.net/pull/489).

#### 0.7.0 Oct 16 2014
Major new changes and additions in this release, including some breaking changes...

__Akka.Cluster__ Support (pre-release) - Akka.Cluster is now available on NuGet as a pre-release package (has a `-pre` suffix) and is available for testing. After installing the the Akka.Cluster module you can add take advantage of clustering via configuration, like so:

    akka {
        actor {
          provider = "Akka.Cluster.ClusterActorRefProvider, Akka.Cluster"
        }

        remote {
          log-remote-lifecycle-events = DEBUG
          helios.tcp {
        hostname = "127.0.0.1"
        port = 0
          }
        }

        cluster {
          seed-nodes = [
        "akka.tcp://ClusterSystem@127.0.0.1:2551",
        "akka.tcp://ClusterSystem@127.0.0.1:2552"]

          auto-down-unreachable-after = 10s
        }
      }


And then use cluster-enabled routing on individual, named routers:

    /myAppRouter {
     router = consistent-hashing-pool
      nr-of-instances = 100
      cluster {
        enabled = on
        max-nr-of-instances-per-node = 3
        allow-local-routees = off
        use-role = backend
      }
    }

For more information on how clustering works, please see https://github.com/akkadotnet/akka.net/pull/400

__Breaking Changes: Improved Stashing__ - The old `WithUnboundedStash` and `WithBoundedStash` interfaces have been slightly changed and the `CurrentStash` property has been renamed to `Stash`. Any old stashing code can be replaced with the following in order to continue working:

    public IStash CurrentStash { get { return Stash; } set { Stash=value; } }

The `Stash` field is now automatically populated with an appropriate stash during the actor creation process and there is no need to set this field at all yourself.

__Breaking Changes: Renamed Logger Namespaces__ - The namespaces, DLL names, and NuGet packages for all logger add-ons have been changed to `Akka.Loggers.Xyz`. Please install the latest NuGet package (and uninstall the old ones) and update your Akka HOCON configurations accordingly.

__Serilog Support__ - Akka.NET now has an official [Serilog](http://serilog.net/) logger that you can install via the `Akka.Logger.Serilog` package. You can register the serilog logger via your HOCON configuration like this:

     akka.loggers=["Akka.Logger.Serilog.SerilogLogger, Akka.Logger.Serilog"]

__New Feature: Priority Mailbox__ - The `PriorityMailbox` allows you to define the priority of messages handled by your actors, and this is done by creating your own subclass of either the `UnboundedPriorityMailbox` or `BoundedPriorityMailbox` class and implementing the `PriorityGenerator` method like so:

    public class ReplayMailbox : UnboundedPriorityMailbox
    {
        protected override int PriorityGenerator(object message)
        {
            if (message is HttpResponseMessage) return 1;
            if (!(message is LoggedHttpRequest)) return 2;
            return 3;
        }
    }

The smaller the return value from the `PriorityGenerator`, the higher the priority of the message. You can then configure your actors to use this mailbox via configuration, using a fully-qualified name:


    replay-mailbox {
     mailbox-type: "TrafficSimulator.PlaybackApp.Actors.ReplayMailbox,TrafficSimulator.PlaybackApp"
    }

And from this point onward, any actor can be configured to use this mailbox via `Props`:

    Context.ActorOf(Props.Create<ReplayActor>()
                        .WithRouter(new RoundRobinPool(3))
                        .WithMailbox("replay-mailbox"));

__New Feature: Test Your Akka.NET Apps Using Akka.TestKit__ - We've refactored the testing framework used for testing Akka.NET's internals into a test-framework-agnostic NuGet package you can use for unit and integration testing your own Akka.NET apps. Right now we're scarce on documentation so you'll want to take a look at the tests inside the Akka.NET source for reference.

Right now we have Akka.TestKit adapters for both MSTest and XUnit, which you can install to your own project via the following:

MSTest:

    install-package Akka.TestKit.VsTest

XUnit:

    install-package Akka.TestKit.Xunit

__New Feature: Logging to Standard Out is now done in color__ - This new feature can be disabled by setting `StandardOutLogger.UseColors = false;`.
Colors can be customized: `StandardOutLogger.DebugColor = ConsoleColor.Green;`.
If you need to print to stdout directly use `Akka.Util.StandardOutWriter.Write()` instead of `Console.WriteLine`, otherwise your messages might get printed in the wrong color.

#### 0.6.4 Sep 9 2014
* Introduced `TailChoppingRouter`
* All `ActorSystem` extensions now take an `ExtendedActorSystem` as a dependency - all third party actor system extensions will need to update accordingly.
* Fixed numerous bugs with remote deployment of actors.
* Fixed a live-lock issue for high-traffic connections on Akka.Remote and introduced softer heartbeat failure deadlines.
* Changed the configuration chaining process.
* Removed obsolete attributes from `PatternMatch` and `UntypedActor`.
* Laying groundwork for initial Mono support.

#### 0.6.3 Aug 13 2014
* Made it so HOCON config sections chain properly
* Optimized actor memory footprint
* Fixed a Helios bug that caused Akka.NET to drop messages larger than 32kb

#### 0.6.2 Aug 05 2014
* Upgraded Helios dependency
* Bug fixes
* Improved F# API
* Resizeable Router support
* Inbox support - an actor-like object that can be subscribed to by external objects
* Web.config and App.config support for Akka HOCON configuration

#### 0.6.1 Jul 09 2014
* Upgraded Helios dependency
* Added ConsistentHash router support
* Numerous bug fixes
* Added ReceiveBuilder support

#### 0.2.1-beta Mars 22 2014
* Nuget package
