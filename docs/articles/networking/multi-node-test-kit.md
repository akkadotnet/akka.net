---
uid: multi-node-test-kit
title: Multi-Node TestKit
---

# Using the MultiNode TestKit
If you intend to contribute to any of the high availability modules in Akka.NET, such as Akka.Remote and Akka.Cluster, you will need to familiarize yourself with the MultiNode Testkit and the test runner.

The MultiNodeTestkit consists of three binaries within Akka.NET:

* [`Akka.MultiNodeTestRunner`](https://github.com/akkadotnet/akka.net/tree/dev/src/core/Akka.MultiNodeTestRunner) - custom Xunit2 test runner for executing the specs.
* [`Akka.NodeTestRunner`](https://github.com/akkadotnet/akka.net/tree/dev/src/core/Akka.NodeTestRunner) - test runner for an individual node process launched by `Akka.MultiNodeTestRunner`.
* [`Akka.Remote.TestKit`](https://github.com/akkadotnet/akka.net/tree/dev/src/core/Akka.Remote.TestKit) - the MultiNode TestKit itself.

## MultiNode Specs
The multi node specs are different from traditional specs in that they are intended to run across multiple machines in parallel, to simulate multiple logical nodes participating in a network or cluster.

Here's an example of a multi node spec from the Akka.Cluster.Tests project:

```csharp
public class JoinInProgressMultiNodeConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }

    public JoinInProgressMultiNodeConfig()
    {
        First = Role("first");
        Second = Role("second");

        CommonConfig = MultiNodeLoggingConfig.LoggingConfig.WithFallback(DebugConfig(true))
			.WithFallback(ConfigurationFactory.ParseString(@"
                akka.stdout-loglevel = DEBUG
                akka.cluster {
                    # simulate delay in gossip by turning it off
                    gossip-interval = 300 s
                    failure-detector {
                        threshold = 4
                        acceptable-heartbeat-pause = 1 second
                    }
                }").WithFallback(MultiNodeClusterSpec.ClusterConfig()));

        NodeConfig(new List<RoleName> { First }, new List<Config>
        {
            ConfigurationFactory.ParseString("akka.cluster.roles =[frontend]")
        });
        NodeConfig(new List<RoleName> { Second }, new List<Config>
        {
            ConfigurationFactory.ParseString("akka.cluster.roles =[backend]")
        });
    }
}

public class JoinInProgressSpec : MultiNodeClusterSpec
{
    readonly JoinInProgressMultiNodeConfig _config;

    public JoinInProgressSpec() : this(new JoinInProgressMultiNodeConfig())
    {
    }

    private JoinInProgressSpec(JoinInProgressMultiNodeConfig config) : base(config)
    {
        _config = config;
    }

    [MultiNodeFact]
    public void AClusterNodeMustSendHeartbeatsImmediatelyWhenJoiningToAvoidFalseFailureDetectionDueToDelayedGossip()
    {
        RunOn(StartClusterNode, _config.First);

        EnterBarrier("first-started");

        RunOn(() => Cluster.Join(GetAddress(_config.First)), _config.Second);

        RunOn(() =>
        {
            var until = Deadline.Now + TimeSpan.FromSeconds(5);
            while (!until.IsOverdue)
            {
                Thread.Sleep(200);
                Assert.True(Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)));
            }
        }, _config.First);

        EnterBarrier("after");
    }
}
```

The `MultiNodeFact` attribute is what's used to distinguish a multi-node spec from a typical spec, so you'll need to decorate your multi-node specs with this attribute.

### Designing a MultiNode Spec
A multi-node spec gives us the ability to do the following:

1. Launch multiple independent processes each running their own `ActorSystem`;
2. Define individual configurations for each node;
3. Run specific commands on individual nodes or groups of nodes;
4. Create barriers that are used to synchronize nodes at specific points within a test; and
5. Test assertions across one or more nodes.

> [!NOTE]
> Everything that's available in the default `Akka.TestKit` is also available inside the `Akka.Remote.TestKit`, but it's worth bearing in mind that `Akka.Remote.TestKit` only works with the `Akka.MultiNodeTestRunner` and uses Xunit 2.0 internally.

#### Step 1 - Subclass `MultiNodeConfig`
The first thing to do is define a configuration for each node you want to include in the test, so in order to do that we have to create a test-specific implementation of `MultiNodeConfig`.

```csharp
public class JoinInProgressMultiNodeConfig : MultiNodeConfig
{
    public RoleName First { get; }
    public RoleName Second { get; }

    public JoinInProgressMultiNodeConfig()
    {
        First = Role("first");
        Second = Role("second");

        CommonConfig = MultiNodeLoggingConfig.LoggingConfig.WithFallback(DebugConfig(true))
            .WithFallback(ConfigurationFactory.ParseString(@"
                akka.stdout-loglevel = DEBUG
                akka.cluster {
                    # simulate delay in gossip by turning it off
                    gossip-interval = 300 s
                    failure-detector {
                        threshold = 4
                        acceptable-heartbeat-pause = 1 second
                    }
                }").WithFallback(MultiNodeClusterSpec.ClusterConfig()));


        NodeConfig(new List<RoleName> { First }, new List<Config>
        {
            ConfigurationFactory.ParseString("akka.cluster.roles =[frontend]")
        });
        NodeConfig(new List<RoleName> { Second }, new List<Config>
        {
            ConfigurationFactory.ParseString("akka.cluster.roles =[backend]")
        });
    }
}
```

In the `JoinInProgressMultiNodeConfig`, we define two `RoleName`s for the two nodes who will be participating in this multi node spec, and then we define a `Config` object and have it set to the `CommonConfig` property, which is shared across all nodes.

Also we configured each node to represent specific role `[frontend,backend]` in the cluster. You can attach arbitrary config instance(s) to individual node or group of nodes by calling `NodeConfig(IEnumerable<RoleName> roles, IEnumerable<Config> configs)`.

#### Step 2 - Define a Class for Your Spec, Inherit from `MultiNodeSpec`
The next step is to subclass `MultiNodeSpec` and create a class that each of your individual nodes will run.

```csharp
public class JoinInProgressSpec : MultiNodeClusterSpec
{
    readonly JoinInProgressMultiNodeConfig _config;

    public JoinInProgressSpec() : this(new JoinInProgressMultiNodeConfig())
    {
    }

    private JoinInProgressSpec(JoinInProgressMultiNodeConfig config) : base(config)
    {
        _config = config;
    }
}
```

Decorate each of the independent tests with the `MultiNodeFact` attribute - the `MultiNodeTestRunner` will pick these up once it runs.

You'll need to pass in a copy of your `MultiNodeConfig` object into the constructor of your base class, like this:

```csharp
protected JoinInProgressSpec() : this(new JoinInProgressMultiNodeConfig())
{
}

private JoinInProgressSpec(JoinInProgressMultiNodeConfig config) : base(config)
{
    _config = config;
}
```

The second constructor overload can be used for allowing individual nodes to run with non-shared configurations.

#### Step 3 - Write the Actual Test Methods
Decorate each of the independent tests with the `MultiNodeFact` attribute - the `MultiNodeTestRunner` will pick these up once it runs.

```csharp
public class JoinInProgressSpec : MultiNodeClusterSpec
{
    readonly JoinInProgressMultiNodeConfig _config;

    public JoinInProgressSpec() : this(new JoinInProgressMultiNodeConfig())
    {
    }

    private JoinInProgressSpec(JoinInProgressMultiNodeConfig config) : base(config)
    {
        _config = config;
    }

    [MultiNodeFact]
    public void AClusterNodeMustSendHeartbeatsImmediatelyWhenJoiningToAvoidFalseFailureDetectionDueToDelayedGossip()
    {
        RunOn(StartClusterNode, _config.First);

        EnterBarrier("first-started");

        RunOn(() => Cluster.Join(GetAddress(_config.First)), _config.Second);

        RunOn(() =>
        {
            var until = Deadline.Now + TimeSpan.FromSeconds(5);
            while (!until.IsOverdue)
            {
                Thread.Sleep(200);
                Assert.True(Cluster.FailureDetector.IsAvailable(GetAddress(_config.Second)));
            }
        }, _config.First);

        EnterBarrier("after");
    }
}
```

So a couple of special methods to pay attention to....

* `RunOn(Action thunk, params RoleName[] roles)` - this will run a method ONLY on the specified `roles`.
* `EnterBarrier(string barrierName)` - this creates a named barrier and waits for all nodes to synchronize on this barrier before moving onto the next portion of the spec.

There's also the `TestConductor` property, which you can use for doing things like disconnecting a node from the spec:

```csharp
 public void AClusterOf3MembersMustNotReachConvergenceWhileAnyNodesAreUnreachable()
{
    var thirdAddress = GetAddress(_config.Third);
    EnterBarrier("before-shutdown");

    RunOn(() =>
    {
        //kill 'third' node
        TestConductor.Exit(_config.Third, 0).Wait();
        MarkNodeAsUnavailable(thirdAddress);
    }, _config.First);

    RunOn(() => Within(TimeSpan.FromSeconds(28), () =>
    {
        //third becomes unreachable
        AwaitAssert(() => ClusterView.UnreachableMembers.Count.ShouldBe(1));
        AwaitSeenSameState(GetAddress(_config.First), GetAddress(_config.Second));
        // still one unreachable
        ClusterView.UnreachableMembers.Count.ShouldBe(1);
        ClusterView.UnreachableMembers.First().Address.ShouldBe(thirdAddress);
        ClusterView.Members.Count.ShouldBe(3);
    }), _config.First, _config.Second);

    EnterBarrier("after-2");
}
```

If you have multiple phases that need to be executed as part of a test, you can write them like this:

```csharp
[MultiNodeFact]
public void ConvergenceSpecTests()
{
    AClusterOf3MembersMustReachInitialConvergence();
    AClusterOf3MembersMustNotReachConvergenceWhileAnyNodesAreUnreachable();
    AClusterOf3MembersMustNotMoveANewJoiningNodeToUpWhileThereIsNoConvergence();
}
```

This unfortunate design is a byproduct of Xunit and how it recreates the entire test class on each method.


### Running MultiNode Specs
To actually run this specification, we have to execute the `Akka.MultiNodeTestRunner.exe` against the .DLL that contains our specs.

Here's the set of arguments that the MultiNodeTestRunner takes:

    Akka.MultiNodeTestRunner.exe path-to-dll # path to DLL containing tests
	[-Dmultinode.enable-filesink=(on|off)] # writes test output to disk
	[-Dmultinode.spec=("fully qualified spec method name)] # execute a specific test method
															    # instead of all of them

Here's an example of what invoking the test runner might look like if all of our multinodetests were packaged into Akka.MultiNodeTests.dll.


    C:\> Akka.MultiNodeTestRunner.exe "Akka.MultiNodetests.dll" -Dmultinode.enable-filesink=on

The output of a multi node test run will include the results for each specification for every node participating in the test. Here's a sample of what the final output at the end of a full test run looks like:

![Akka.MultiNodeTestRunner.exe final output](/images/multinode-testkit-output.png)