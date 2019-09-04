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