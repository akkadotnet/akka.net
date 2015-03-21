#### 1.0.0 Mar 15 2015

TBD

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
