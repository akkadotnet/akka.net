# Using Akka.NET MultiNode TestRunner

One of the most important sets of tests for `Akka.Remote` and `Akka.Cluster` are the `MultiNodeSpec`s - these are specs that test how distributed Akka.NET clusters behave across a variety of network scenarios.

But more importantly, you can use the `Akka.Remote.TestKit` (which contains the framework for writing a `MultiNodeSpec`) and the `Akka.MultiNodeTestRunner` for your own distributed tests!

This README explains how to run the `Akka.MultiNodeTestRunner` to execute any `MultiNodeSpec` instances found in a given .NET assembly.

## Running the MultiNodeTestRunner

Right now the only options for running the `MultiNodeTestRunner` is to build from the Akka.NET source and manually copy the binaries out of `src\core\Akka.MultiNodeTestRunner\bin\[Debug|Release]`:



The `Akka.MultiNodeTestRunner` process requires only one argument - the full path or name of the assembly containing `MultiNodeSpec` tests.

    C:> Akka.MultiNodeTestRunner.exe [assembly name]

### Built-in Tests for Akka.Cluster

`Akka.Cluster.Tests` is already linked as a dependency by the `Akka.MultiNodeTestRunner`, so to run all of the `MultiNodeSpec` tests for `Akka.Cluster` you only need to do the following:

    C:> Akka.MultiNodeTestRunner.exe "Akka.Cluster.Tests.dll"

## Sample Output



