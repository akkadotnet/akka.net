# Akka.NET

Akka.NET is a port of the popular Java/Scala framework Akka to .NET.

This is a community driven port and is not affiliated with Typesafe who makes the original Java/Scala version.

* Subscribe to the Akka.NET dev feed: https://twitter.com/AkkaDotNet  (@AkkaDotNet)
* Support forum: https://groups.google.com/forum/#!forum/akkadotnet-user-list
* Mail: akkadotnet@gmail.com


[![Build status](https://ci.appveyor.com/api/projects/status/liybm4ueeu1cq4et)](https://ci.appveyor.com/project/AkkaDotNet/akka-net-335)

###Features
* [Actors](https://github.com/akkadotnet/akka.net/wiki/Getting started)
  * [Actor Lifecycle](https://github.com/akkadotnet/akka.net/blob/master/akka.net.Tests/ActorLifeCycleSpec.cs)
  * [Actor Props](https://github.com/akkadotnet/akka.net/wiki/Props)
  * [Addressing](https://github.com/akkadotnet/akka.net/wiki/Addressing)
* [Configuration](https://github.com/akkadotnet/akka.net/wiki/Configuration)
* [EventBus](https://github.com/akkadotnet/akka.net/wiki/EventBus)
* [Finite State Machines](https://github.com/akkadotnet/akka.net/wiki/FSM)
* [Hotswap](https://github.com/akkadotnet/akka.net/wiki/Hotswap)
* [Logging](https://github.com/akkadotnet/akka.net/wiki/Logging)
* [Performance](https://github.com/akkadotnet/akka.net/wiki/Performance)
* [Remoting](https://github.com/akkadotnet/akka.net/wiki/Remoting)
* [Routing](https://github.com/akkadotnet/akka.net/wiki/Routing)
* [Scheduling](https://github.com/akkadotnet/akka.net/wiki/Scheduler)
* [Supervision](https://github.com/akkadotnet/akka.net/wiki/Supervision)
* [The F# API](https://github.com/akkadotnet/akka.net/wiki/FSharp-API)

#####Not yet implemented:
* Akka Cluster support
* Akka Persistence

#####Install Akka.NET via NuGet

If you want to include Akka.NET in your project, you can [install it directly from NuGet](https://www.nuget.org/packages/Akka)

To install Akka.NET Distributed Actor Framework, run the following command in the Package Manager Console

````
PM> Install-Package Akka
PM> Install-Package Akka.Remote
````

And if you need F# support:

````
PM> Install-Package Akka.FSharp
````

#####Contribute
If you are interested in helping porting Akka to .NET please take a look at [Contributing to Akka.NET](https://github.com/akkadotnet/akka.net/wiki/Contributing-to-Akka.NET).

Also, please see [Building Akka .NET](https://github.com/akkadotnet/akka.net/wiki/Building-and-Distributing-Pigeon).
