---
uid: multi-node-testing
title: Multi-Node Testing Distributed Akka.NET Applications
---

# Multi-Node Testing Distributed Akka.NET Applications

> [!NOTE]
> This documentation applies to the new `Akka.MultiNode.TestAdapter` multi-node test NuGet package.
>
> For the old `Akka.MultiNodeTestRunner` package, please check the [old documentation](xref:multi-node-testing-old)

One of the most powerful testing features of Akka.NET is its ability to create and simulate real-world network conditions such as latency, network partitions, process crashes, and more. Given that any of these can happen in a production environment it's important to be able to write tests which validate your application's ability to correctly recover.

This is precisely what the Multi-Node TestKit and TestRunner (MNTR) does in Akka.NET.

## MNTR Components

The Akka.NET Multi-Node TestKit consists of the following publicly available NuGet packages:

* [Akka.MultiNode.TestAdapter](https://www.nuget.org/packages/Akka.MultiNode.TestAdapter) - the Xunit test framework adapter that can launch a multi-node testing environment;
* [Akka.Remote.TestKit](https://www.nuget.org/packages/Akka.Remote.TestKit) - the base package used to create multi-node tests; and
* [Akka.Cluster.TestKit](https://www.nuget.org/packages/Akka.Cluster.TestKit) - a set of test helper methods for [Akka.Cluster](xref:cluster-overview) applications built on top of the Akka.Remote.TestKit.

## How the MNTR Works

The multi node test runner inside the TestAdapter works via the following process:

1. Consumes a .DLL that references the `Akka.MultiNode.TestAdapter` NuGet package;
2. For each detected multi-node test class, read that tests' configuration and build a corresponding network;
3. Run the test, including assertions, process barriers, and logging;
4. Provide a PASS/FAIL signal for each node participating in the test;
5. If any of the nodes failed, mark the entire test as failed; and
6. Write all of the output for each test and for each individual node in that test into its own output folder for review.

![Akka.NET MultiNodeTestRunner Execution](../../images/testing/mntr-execution.png)

Given this architecture, let's wade into how to actually write and run a test using the multi node test adapter.

## How to Write a Multi-Node Test

The first step in writing a multi-node test is to install either the `Akka.Remote.TestKit` or the `Akka.Cluster.TestKit` NuGet package into your application. If you're working with `Akka.Cluster`, you only need to use the `Akka.Cluster.TestKit` package.

Next, you need to plan out your test scenario for your application. Here's a simple one from the Akka.NET codebase itself we can use as an example:

1. Form a two-node cluster, "Seed1" and "Seed2" as the role names, using both nodes as seed nodes;
2. Restart the first seed node ("Seed1") process; and
3. Verify that the restarted "Seed1" node is able to rejoin the same cluster as "Seed2."

So this sounds like a rather complicated procedure, but in actuality the TestKit makes it easy for us to test scenarios just like these.

### Step 1 - Create a Test Configuration

The first step in creating an effective multi-node test is to define the configuration class for this test - this is going to tell the MNTR how many nodes there will need to be, how each node should be configured, and what features should be enabled for this unit test.

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=MultiNodeSpecConfig)]

The declaration of the `RoleName` properties is what the MNTR uses to determine how many nodes will be participating in this test. In this example, the test will create exactly two test processes.

The `CommonConfig` element of the [`MultiNodeConfig` implementation class](xref:Akka.Remote.TestKit.MultiNodeConfig) is the common config that will be used throughout all of the nodes inside the multi-node test. So, for instance, if you want all of the nodes in your test to run [Akka.Cluster.Sharding](xref:cluster-sharding) you'd want to include those configuration elements inside the `CommonConfig` property.

#### Configuring Individual Nodes Differently

In addition to passing a `CommonConfig` object throughout all nodes in your multi-node test, you can also provide configurations for individual nodes during each test.

For example: if you're taking advantage of the `akka.cluster.roles` property to have some nodes execute different workloads than others, this might be something you'd want to specify for nodes individually.

The `NodeConfig` method allows you to do just that:

```c#
NodeConfig(
    new List<RoleName> { First }, 
    new List<Config> { ConfigurationFactory.ParseString("akka.cluster.roles =[""a"", ""c""]") });
NodeConfig(
    new List<RoleName> { Second, Third }, 
    new List<Config> { ConfigurationFactory.ParseString("akka.cluster.roles =[""b"", ""c""]") });
```

Right after setting `CommonConfig` inside the constructor of your `MultiNodeConfig` class you can call `NodeConfig` for the specified `RoleName`s and each of them will have their `Config`s added to their `ActorSystem` configurations at startup.

> [!NOTE]
> `NodeConfig` takes precedent over `CommonConfig`

#### Enabling TestTransport to Simulate Network Errors

One final but important thing you might want to during the design of a multi-node test is to enable the `TestTransport`, which exposes a capability inside your tests that allows for you to create network partitions, disconnects, and latency on the fly.

[!code-csharp[SurviveNetworkInstabilitySpec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/SurviveNetworkInstabilitySpec.cs?name=MultiNodeSpecConfig)]

To enable the `TestTransport`, all you have to do is set `TestTransport = true` inside the `MultiNodeConfig` constructor.

Once that's done, you'll be able to use the `TestConductor` inside your multi-node tests to enable all kinds of simulated network partitions.

### Step 2 - Create Your MultiNodeClusterSpec or MultiNodeSpec

Once you've created your `MultiNodeConfig`, you'll want to create a `MultiNodeClusterSpec` if you're using Akka.Cluster or a `MultiNodeSpec` if you just want to use Akka.Remote.

We're going to show you a full code sample first and walk through how it works in detail below.

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=MultiNodeSpec)]

First, take note of the default public constructor:

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=PublicConstructor)]

This is an XUnit restriction - there can only be one public constructor per class, and you need to pass in your `MultiNodeConfig` to the base class constructor, which is exactly what we do in the `protected` constructor.

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=ProtectedConstructor)]

We're going to hang onto a copy of our `RestartNode2SpecConfig` class in a field called `_config`, which will be helpful when we need to look up `RoleName`s later.

Finally, we need to create our test method and decorate it with the `MultiNodeFact` attribute:

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=MultiNodeFact)]

This method is what will be executed by the multi-node test runner.

#### Addressing Nodes

All nodes in the multi-node test runner are going to be given randomized addresses and ports - thus we can never predict those addresses at the time we design our tests. Therefore, the way we always refer to nodes is by their `RoleName`s.

If we want to resolve the Akka.NET `Address` of a specific node, we can do this via the `GetAddress` method:

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=SeedNodesProperty)]

The `GetAddress` method accepts a `RoleName` and returns the `Address` that was assigned to the node by the multi-node test runner.

#### Running Code on Specific Nodes

The most important tool in the `Akka.Remote.TestKit`, the base library where all multi-node testing tools are defined, is the `RunOn` method:

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=RunOnSample)]

Notice that the first `RunOn` call takes an argument of `_config.Seed2`, whereas the second `RunOn` call takes an argument of `_config.Seed1`. The code in the first `RunOn` block will only execute on the node with `RoleName` "Seed2" and the code in the second block will only run on `RoleName` "Seed1."

Given that each node runs inside its own process, it's likely that both of these blocks of code will be executing simultaneously.

#### Synchronizing Test Progression on Different Nodes

In order to make multi-node tests effective, we must have some means of synchronizing all of the nodes in each test - such that they all reach the same assertions at the same time. This is precisely what the `EnterBarrier` method helps us do, as you can see in the code sample above.

`EnterBarrier` creates a synchronization barrier between processes - no processes can advance past it until all processes have reached it. If one process fails to reach the barrier within 30 seconds (this is configurable via the [Akka.Remote.TestKit reference configuration](https://github.com/akkadotnet/akka.net/blob/dev/src/core/Akka.Remote.TestKit/Internals/Reference.conf)), the test will throw an assertion error and fail.

#### Terminating, Aborting, and Disconnecting Nodes

One of the most useful features of the multi-node testkit is its ability to simulate real-world networking issues, and this can be accomplished using some of the APIs found in the Akka.Remote.TestKit.

##### Creating Network Partitions

In order to create a network partition between two or more nodes, the `TestTransport` must be enabled inside the `MultiNodeConfig` class constructor. This allows access to the `TestConductor`, which can be used to render two or more nodes unreachable:

```c#
RunOn(() =>
{
    TestConductor.Blackhole(_config.First, _config.Second, 
        ThrottleTransportAdapter.Direction.Both).Wait();
}, _config.First);
EnterBarrier("blackhole-2");
```

In this example, the `TestConductor.Blackhole` method is used to create 100% packet loss between the `RoleName` "First" and "Second". Those two nodes will still be running as part of the test, but they won't be able to communicate with each other over Akka.Remote.

The `Task` returned by `TestConductor.Blackhole` will complete once the Akka.Remote transport has enabled "blackhole" mode for that connection, which usually doesn't take longer than a few milliseconds.

To stop black-holing these nodes, we'd need to call the `TestConductor.PassThrough` method on these same two `RoleName` instances:

```c#
RunOn(() =>
{
    TestConductor.PassThrough(_config.First, _config.Second, 
        ThrottleTransportAdapter.Direction.Both).Wait();
}, _config.First);
EnterBarrier("repair-2");
```

This will allow Akka.Remote to resume normal execution over the network.

##### Killing Nodes

There are two ways to kill a node in a running multi-node test.

The first is to call the `Shutdown` method on the `ActorSystem` of the node you wish to have exit the test. This will cause the `ActorSystem` to terminate gracefully - this simulates the planned shutdown of a node.

```c#
// shutdown seed1System
RunOn(() =>
{
    Shutdown(seed1System.Value, RemainingOrDefault);
}, _config.Seed1);
EnterBarrier("seed1-shutdown");
```

The other way to shutdown a node is to use the `TestConductor.Exit` command - this is intended to simulate the _unplanned_ shutdown of a node, i.e. a process crash.

```c#
RunOn(() => {
    TestConductor.Exit(_config.Third, 0).Wait();
}, _config.First);
```

Once a node has exited the test, it will no longer be able to wait on `EnterBarrier` calls and the multi-node test runner will not try to collect any data from that node from that point onward.

## Setting Up The Multi-Node Test Project

The `Akka.MultiNode.TestAdapter` NuGet package is a test adapter based on the popular XUnit that is compatible with `dotnet test`, Microsoft Visual Studio, and JetBrains Rider.

### Turn Off Xunit Parallelization

It is advised that you don't use any test parallelization when running multi node tests. To do this, you can do either of these methods.

#### Using The Convenience Custom Test Framework
  
`Akka.MultiNode.TestAdapter` came with a convenience test framework that automatically ignores Xunit collection parallelization feature. To use it, you would need to declare a single assembly level attribute inside your project:

```c#
using Xunit;

[assembly: TestFramework("Akka.MultiNode.TestAdapter.MultiNodeTestFramework", "Akka.MultiNode.TestAdapter")]
```

#### Using Xunit Configuration File

You can manually set Xunit to turn off collection parallelization by including a special json configuration file named `xunit.runner.json` inside your project. Make sure that the file will be copied to the output directory during build.

```json
{
   "parallelizeTestCollections" : false,
   "parallelizeAssembly" : false
}
```

> [!NOTE]
> Be careful that this method might not be honored in some IDE.

### Configuring MNTR Behavior

By default, MNTR will create a folder named `TestResults` inside the test assembly folder and write all test log output in there. To change this and other default behavior, you can include a json configuration named `xunit.multinode.runner.json` inside your project:

```json
{
  "outputDirectory": "TestResults",
  "failedSpecsDirectory": "FAILED_SPECS_LOGS",
  "listenAddress": "127.0.0.1",
  "listenPort":  0,
  "clearOutputDirectory": false
}
```

* **outputDirectory**: The directory path for all log output files. This can be a relative or an absolute folder path; if set as a relative path, it is relative to the test assembly folder.
* **failedSpecsDirectory**: Determines output directory name for aggregated failed test logs. This will be the name of a folder inside the output directory, not a path to a directory.
* **listenAddress**: Determines the address that this multi-node test runner will use to listen for log messages from individual spec.
* **listenPort**: Determines the port number that this multi-node test runner will use to listen for log messages from individual spec.
* **ClearOutputDirectory**: Clear the output directory before running the test session. If set to false, all test logs are appended to the out file.

## Running Multi-Node Tests

Once you've code your multi-node tests and compiled them, it's now time to run them. There are currently three officially supported way to run a multi-node test:

1. .NET CLI `dotnet test`
2. Microsoft Visual Studio test runner
3. JetBrains Rider test runner

A multi-node test can be treated just like any other unit tests with the exception that it could not be run in parallel.

_Hint:_ Each test run will append new log entries to output files. If this is not desired, you can set the `clearOutputDirectory` option in the JSON setting file to true to delete previous output before writing new ones.

If you're lost and need more examples, please explore the Akka.NET source code and take a look at some of the MNTR output produced by our CI system on any open pull request.

## Debugging Failed Tests

As already mentioned, after each spec is finished, test runner will emit log files for it to output directory subfolder with the full name of the spec. In this folder you will find individual logs for each node (named according to the roles they were assigned), and `aggregated.txt` file, which contains all nodes logs aggregated into single timeline.

Also, `FAILED_SPECS_LOGS` subdirectory will be generated. If any of your specs failed, this folder will contain aggregated logs for each spec - basically, the same `aggregated.txt` files but with their spec's names. This is a good place to get a full picture of what has failed and why.
