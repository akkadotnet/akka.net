# Multi-Node Testing Distributed Akka.NET Applications
 
One of the most powerful testing features of Akka.NET is its ability to create and simulate real-world network conditions such as latency, network partitions, process crashes, and more. Given that any of these can happen in a production environment it's important to be able to write tests t which validate your application's ability to correctly recover.

This is precisely what the Multi-Node TestKit and TestRunner (MNTR) does in Akka.NET.

### MNTR Components
The Akka.NET Multi-Node TestKit consists of the following publicly available NuGet packages:

* [Akka.MultiNodeTestRunner](https://www.nuget.org/packages/Akka.MultiNodeTestRunner) - the test runner that can launch a multi-node testing environment;
* [Akka.Remote.TestKit](https://www.nuget.org/packages/Akka.Remote.TestKit) - the base package used to create multi-node tests; and
* [Akka.Cluster.TestKit](https://www.nuget.org/packages/Akka.Cluster.TestKit) - a set of test helper methods for [Akka.Cluster](../cluster/cluster-overview.md) applications built on top of the Akka.Remote.TestKit.

### How the MNTR Works
The MultiNodeTestRunner works via the following process:

1. Consumes a .DLL that has Akka.Remote.TestKit or Akka.Cluster.TestKit classes contained inside it;
2. For each detected multi-node test class, read that tests' configuration and build a corresponding network;
3. Run the test, including assertions, process barriers, and logging;
4. Provide a PASS/FAIL signal for each node participating in the test; 
5. If any of the nodes failed, mark the entire test as failed; and
6. Write all of the output for each test and for each individual node in that test into its own output folder for review.

![Akka.NET MultiNodeTestRunner Execution](/images/testing/mntr-execution.png)

Given this architecture, let's wade into how to actually write and run a test using the MultiNodeTestRunner.

## How to Write a Multi-Node Test

The first step in writing a multi-node test is to install either the Akka.Remote.TestKit or the Akka.Cluster.TestKit NuGet package into your application. If you're working with Akka.Cluster, always just use the Akka.Cluster.TestKit package.

Next, you need to plan out your test scenario for your application. Here's a simple one from the Akka.NET codebase itself we can use as an example:

1. Form a two-node cluster, "Seed1" and "Seed2" as the role names, using both nodes as seed nodes;
2. Restart the first seed node ("Seed1") process; and
3. Verify that the restarted "Seed1" node is able to rejoin the same cluster as "Seed2."

So this sounds like a rather complicated procedure, but in actuality the MNTR makes it easy for us to test scenarios just like these.

### Step 1 - Create a Test Configuration
The first step in creating an effective multi-node test is to define the configuration class for this test - this is going to tell the MNTR how many nodes there will need to be, how each node should be configured, and what features should be enabled for this unit test.

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=MultiNodeSpecConfig)]

The declaration of the `RoleName` properties is what the MNTR uses to determine how many nodes will be participating in this test. In this example, the test will create exactly two test processes.

The `CommonConfig` element of the [`MultiNodeConfig` implementation class](../../api/Akka.Remote.TestKit.MultiNodeConfig.html) is the common config that will be used throughout all of the nodes inside the multi-node test. So, for instance, if you want all of the nodes in your test to run [Akka.Cluster.Sharding](../clustering/cluster-sharding.md) you'd want to include those configuration elements inside the `CommonConfig` property.

#### Configuring Individual Nodes Differently
In addition to passing a `CommonConfig` object throughout all nodes in your multi-node test, you can also provide configurations for individual nodes during each test.

For example: if you're taking advantage of the `akka.cluster.roles` property to have some nodes execute different workloads than others, this might be something you'd want to specify for nodes individually. 

The `NodeConfig` method allows you to do just that:

```csharp
NodeConfig(new List<RoleName> { First }, 
	new List<Config> { 	ConfigurationFactory.ParseString(
		@"akka.cluster.roles =[""a"", ""c""]") });
NodeConfig(new List<RoleName> { Second, Third }, 
	new List<Config> { ConfigurationFactory.ParseString(
		@"akka.cluster.roles =[""b"", ""c""]") });
```

Right after setting `CommonConfig` inside the constructor of your `MultiNodeConfig` class you can call `NodeConfig` for the specified `RoleName`s and each of them will have their `Config`s added to their `ActorSystem` configurations at startup.

> **N.B.** `NodeConfig` takes precendent over `CommonConfig`

#### Enabling `TestTransport` to Simulate Network Errors
One final but important thing you might want to during the design of a multi-node test is to enable the `TestTransport`, which exposes a capability inside your tests that allows for you to create network partitions, disconnects, and latency on the fly.

[!code-csharp[SurviveNetworkInstabilitySpec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/SurviveNetworkInstabilitySpec.cs?name=MultiNodeSpecConfig)]

To enable the `TestTransport`, all you have to do is set `TestTransport = true` inside the `MultiNodeConfig` constructor.

Once that's done, you'll be able to use the `TestConductor` inside your multi-node tests to enable all kinds of simulated network partitions.

### Step 2 - Create Your `MultiNodeClusterSpec` or `MultiNodeSpec`
Once you've created your `MultiNodeConfig`, you'll want to create a `MultiNodeClusterSpec` if you're using Akka.Cluster or a `MultiNodeSpec` if you just want to use Akka.Remote.

We're going to show you a full code sample first and walk through how it works in detail below.

[!code-csharp[RestartNode2Spec.cs](../../../src/core/Akka.Cluster.Tests.MultiNode/RestartNode2Spec.cs?name=MultiNodeSpec)]

