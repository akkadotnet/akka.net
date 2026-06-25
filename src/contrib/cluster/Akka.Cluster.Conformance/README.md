# Akka.Cluster.Conformance

A **conformance test harness** for Akka.NET clustering. It stands up an instrumented *reference*
seed node and uses it to observe and validate a *node-under-test* ("worker") as the worker connects
to the cluster, converges, gracefully leaves, and cleanly shuts down.

The worker requires **no instrumentation and no cooperation** — it can be a completely stock
Akka.NET node, a node from a different Akka.NET version, or any other implementation that speaks the
cluster membership protocol. Everything is judged from the reference side.

## How it works

1. **Recording (modified core, opt-in).** Setting `akka.cluster.protocol-recorder = on` makes a node
   record the membership-protocol messages it exchanges with peers — `InitJoin` / `InitJoinAck` /
   `InitJoinNack`, `Join` / `Welcome`, `Leave`, `ExitingConfirmed`, and gossip. Each interaction is
   published to the system event stream and logged at INFO with a stable `CLUSTER-PROTOCOL` prefix.
   The flag defaults to `off`; with it off the cluster behaves exactly as before (zero overhead).

2. **Reference seed.** `ReferenceSeed` starts a single-node cluster with the recorder enabled and
   subscribes a collector to both the recorder's protocol events and the standard cluster membership
   event stream (`MemberUp`, `MemberLeft`, `MemberExited`, `MemberRemoved`, `UnreachableMember`, ...).
   It merges them, in arrival order, into a single ordered `ConformanceTrace`.

3. **ACT — the Akka Conformance Tester ("stop and teach").** `Act.Check(trace, workerAddress)` evaluates the
   lifecycle obligations **in protocol order and stops at the first one the worker has not met**,
   returning a `ConformanceResult` whose message explains — in protocol terms, without reference to
   any programming language — what the worker must do to pass that step.

## The conformance ladder

| # | Step | Satisfied when the reference node observed… |
|---|------|---------------------------------------------|
| 1 | Initial contact | an `InitJoin` from the worker and its own `InitJoinAck` reply |
| 2 | Join request | a `Join` from the worker (carrying address, roles, version) |
| 3 | Join accepted | a `Welcome` sent back to the worker |
| 4 | Gossip participation | at least one gossip message from the worker |
| 5 | Convergence to Up | the worker reaching `MemberUp` |
| 6 | Broadcast routee delivery | a reply from the worker's `/user/echo` routee to a cluster broadcast |
| 7 | Graceful leave announced | the worker reaching `Leaving` (`MemberLeft`) |
| 8 | Exiting reached | the worker reaching `Exiting` (`MemberExited`) |
| 9 | Exit confirmed | an `ExitingConfirmed` from the worker |
| 10 | Clean removal | `MemberRemoved` from `Exiting`, never `Downed`/unreachable |

Step 6 exercises application-level routing: the reference seed runs a cluster `BroadcastGroup` router
over `/user/echo` on every member, periodically broadcasts a ping, and records each node's reply. A
conforming node hosts an actor at `/user/echo` that echoes the message back to its sender.

## Usage (in-process worker)

```csharp
await using var seed = await ReferenceSeed.StartAsync("MyCluster");

var worker = InProcessWorker.Start("MyCluster", seed.SeedNodeUri);
await worker.WaitUntilUpAsync(TimeSpan.FromSeconds(20));
await seed.WaitForUpMembersAsync(2, TimeSpan.FromSeconds(20));

worker.LeaveGracefully();
await seed.WaitForRemovedAsync(worker.Address, TimeSpan.FromSeconds(25));

var result = Act.Check(seed.Trace, worker.Address);
Console.WriteLine(result);        // pass summary, or the failed step + teaching message
result.EnsurePassed();            // throws ConformanceException with the teaching message on failure
```

## Testing an external / unmodified worker

Because the reference node observes purely from its own side, the worker can be a separate process:

1. Start a `ReferenceSeed` and read `seed.SeedNodeUri`.
2. Launch the worker out-of-process, stock, with `akka.cluster.seed-nodes = ["<SeedNodeUri>"]` and
   the **same** actor-system (cluster) name.
3. Drive the worker's lifecycle (let it converge, then ask it to leave, then stop it).
4. Call `Act.Check(seed.Trace, workerAddress)` and inspect the result.

See `Akka.Cluster.Conformance.Tests` for a passing run against a conforming worker and a failing run
(with the teaching message) against a worker that crashes instead of leaving gracefully.
