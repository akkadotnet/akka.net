#### 0.7.1 NEXTVERSION
_This is a placeholder for the next released version. Add new stuff that will go into the next version below. It might be 0.7.1 or 0.8. __REMOVE THIS TEXT BEFORE RELEASING!___

__Breaking Change to the internal api: The `Next` property on `IAtomicCounter<T>` has been changed into the function `Next()`__ This was done as it had side effects, i.e. the value was increased when the getter was called. This makes it very hard to debug as the debugger kept calling the property and causing the value to be increased.

__Akka.Serilog__ `SerilogLogMessageFormatter` has been moved to the namespace `Akka.Logger.Serilog` (it used to be in `Akka.Serilog.Event.Serilog`).
Update your `using` statements from `using Akka.Serilog.Event.Serilog;` to `using Akka.Logger.Serilog;`.

__Breaking Change to the internal api: Changed signatures in the abstract class `SupervisorStrategy`__. The following methods has new signatures: `HandleFailure`, `ProcessFailure`. If you've inherited from `SupervisorStrategy`, `OneForOneStrategy` or `AllForOneStrategy` and overriden the aforementioned methods you need to update their signatures.

__TestProbe can be implicitly casted to ActorRef__. New feature. Tests requring the `ActorRef` of a `TestProbe` can now be simplified:
``` C#
var probe = CreateTestProbe();
var sut = ActorOf<GreeterActor>();
sut.Tell("Akka", probe); // previously probe.Ref was required
probe.ExpectMsg("Hi Akka!");
```


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
* All `ActorSystem` extensions now take an `ExtendedActorSystem` as a dependency - all thirdy party actor system extensions will need to update accordingly.
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
